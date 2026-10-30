using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// The recovery in Play Mode: the development data registers itself before the scenes, but nothing runs in the
    /// Editor; restored lab JSON gives a gun the recorded behaviour. Settings in memory, files in a temporary folder.
    /// </summary>
    public class DeviceDataRecoveryPlayTests
    {
        sealed class Prefs : IRecoveryPrefs
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public bool HasKey(string key) => Values.ContainsKey(key);
            public string GetString(string key) => Values.TryGetValue(key, out var v) && v is string s ? s : "";
            public int GetInt(string key) => Values.TryGetValue(key, out var v) && v is int i ? i : 0;
            public void SetString(string key, string value) => Values[key] = value;
            public void SetInt(string key, int value) => Values[key] = value;
            public void Save() { }
        }

        static string BundleJson()
        {
            var type = Type.GetType("Deadlock.Verification.IosIncident20261028Bundle, Assembly-CSharp");
            return type?.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
        }

        string dir;

        [SetUp]
        public void SetUp() => dir = Path.Combine(Path.GetTempPath(), "DeadlockRecoveryPlay_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true); // this test's own temporary folder only
        }

        [UnityTest]
        public IEnumerator InPlayMode_DataIsRegistered_ButNothingRunsInTheEditor()
        {
            string json = BundleJson();
            if (json == null)
                Assert.Ignore("generated recovery data not present");
            string layoutBefore = PlayerPrefs.GetString("deadlock.uiLayout.home", "<none>");
            yield return null;
            Assert.AreEqual(json, DeviceDataRecoveryBoot.RegisteredBundle, "registered before the first scene");
            Assert.AreEqual(layoutBefore, PlayerPrefs.GetString("deadlock.uiLayout.home", "<none>"), "the Editor's settings are not touched");
            string reports = Path.Combine(Application.temporaryCachePath, DeviceDataRecoveryBoot.ReportFolderName);
            Assert.IsTrue(!Directory.Exists(reports) || Directory.GetFiles(reports).Length == 0, "no recovery ran in the Editor");
        }

        [UnityTest]
        public IEnumerator RestoredRifleJson_GivesTheGunTheRecordedRecoil()
        {
            string json = BundleJson();
            if (json == null)
                Assert.Ignore("generated recovery data not present");
            var prefs = new Prefs();
            string weapons = Path.Combine(dir, "WeaponBalanceLab");
            var report = DeviceDataRecovery.Run(JsonUtility.FromJson<RecoveryBundle>(json), RecoveryMode.Apply,
                new[] { IosIncident20261028.RifleItem }, prefs, weapons);
            Assert.IsTrue(report.ok, report.error);
            yield return null;

            Assert.IsTrue(new WeaponBalanceStore(weapons).TryLoad("proto_rifle", out var profile, out string error), error);
            var gun = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                profile.ApplyTo(gun);
                Assert.AreEqual(RecoilAxis.Diagonal, gun.recoilProfile.axis);
                Assert.IsTrue(RecoilCalculator.TryCompute(gun.recoilProfile, 0, 0f, out float pitch, out float yaw));
                Assert.Greater(Mathf.Abs(yaw), 0f, "sideways kick");
                Assert.AreEqual(1f, gun.recoilProfile.recoveryFraction);
                Assert.AreEqual(3f, gun.recoilProfile.recoverySpeed);
                Assert.AreEqual(0, prefs.Values.Count, "only the chosen file");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gun);
            }
        }

        [UnityTest]
        public IEnumerator EquipmentScreen_InPlayMode_SavesNoScreenshot()
        {
            string root = Path.Combine(Application.temporaryCachePath, DevCaptureStore.RootName, "equip");
            int before = Directory.Exists(root) ? Directory.GetFiles(root).Length : 0;
            yield return null;
            Assert.IsFalse(EquipmentScreenDiagnostics.Enabled);
            Assert.IsFalse(EquipmentScreenDiagnostics.CaptureRequested(Environment.GetEnvironmentVariable), "not asked for at launch");
            Assert.AreEqual(before, Directory.Exists(root) ? Directory.GetFiles(root).Length : 0);
        }
    }
}
