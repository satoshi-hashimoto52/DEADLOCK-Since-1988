using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.Home;
using Deadlock.UI.Layout;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Settings storage of the recovery tests (never the real PlayerPrefs); counts the writes.</summary>
    public sealed class MemoryRecoveryPrefs : IRecoveryPrefs
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public readonly List<string> Writes = new List<string>();
        public int Saves;

        public bool HasKey(string key) => Values.ContainsKey(key);
        public string GetString(string key) => Values.TryGetValue(key, out var v) && v is string s ? s : "";
        public int GetInt(string key) => Values.TryGetValue(key, out var v) && v is int i ? i : 0;

        public void SetString(string key, string value)
        {
            Values[key] = value;
            Writes.Add(key);
        }

        public void SetInt(string key, int value)
        {
            Values[key] = value;
            Writes.Add(key);
        }

        public void Save() => Saves++;
    }

    /// <summary>
    /// Recovery of the 2026-10-28 incident, in isolation: a dictionary instead of PlayerPrefs and a temporary folder
    /// instead of the device's Documents. Uses the generated development-only data (Assets/Game/Verification/Recovery);
    /// without it (a clean checkout) the tests that need it are ignored. The real PlayerPrefs are only read, to show
    /// they did not change.
    /// </summary>
    public class DeviceDataRecoveryTests
    {
        // The Mac backup next to the project (outside the repository; DEADLOCK_DEVICE_BACKUP overrides). Absent elsewhere.
        static readonly string Backup = Environment.GetEnvironmentVariable("DEADLOCK_DEVICE_BACKUP") is { Length: > 0 } dir
            ? dir : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "_device_backup"));
        static readonly string BackupLog = Path.Combine(Backup, "2026-10-28_incident_records", "evidence_logs", "final50_cap_log.txt");
        static readonly string BackupRifle = Path.Combine(Backup, "2026-10-28_iphone_screenshots", "run1", "WeaponBalanceLab", "proto_rifle.json");
        static readonly string BackupSmg = Path.Combine(Backup, "2026-10-28_iphone_screenshots", "run1", "WeaponBalanceLab", "proto_smg.json");

        string dir;
        string weapons;
        MemoryRecoveryPrefs prefs;
        Dictionary<string, string> realBefore;

        public static string BundleJson()
        {
            var type = Type.GetType("Deadlock.Verification.IosIncident20261028Bundle, Assembly-CSharp");
            return type?.GetField("Json", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
        }

        static RecoveryBundle Bundle()
        {
            string json = BundleJson();
            if (json == null)
                Assert.Ignore("generated recovery data not present (Assets/Game/Verification/Recovery)");
            return JsonUtility.FromJson<RecoveryBundle>(json);
        }

        static string[] RealKeys() => IosIncident20261028.Prefs().Select(p => p.key)
            .Concat(new[] { "deadlock.touchLayout.v1", "deadlock.hud.minimap.visible" }).ToArray();

        static Dictionary<string, string> ReadReal() => RealKeys().ToDictionary(k => k,
            k => PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k, "") + "|" + PlayerPrefs.GetInt(k, int.MinValue) : "<none>");

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "DeadlockRecoveryTest_" + Guid.NewGuid().ToString("N"));
            weapons = Path.Combine(dir, "Documents", "WeaponBalanceLab");
            prefs = new MemoryRecoveryPrefs();
            realBefore = ReadReal();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true); // this test's own temporary folder only
            CollectionAssert.AreEqual(realBefore, ReadReal(), "the real PlayerPrefs are never written by these tests");
        }

        RecoveryReport Apply(RecoveryBundle bundle, params string[] items) =>
            DeviceDataRecovery.Run(bundle, RecoveryMode.Apply, items.Length == 0 ? new[] { "all" } : items, prefs, weapons);

        [Test]
        public void Bundle_IsValid_LayoutIsTheRecordedOne_WeaponJsonAreTheBackup()
        {
            var bundle = Bundle();
            Assert.IsTrue(DeviceDataRecovery.Validate(bundle, out string error), error);
            Assert.AreEqual(8, bundle.prefs.Count, "layout + 6 transparencies + compare");
            Assert.AreEqual(2, bundle.files.Count);

            string layout = bundle.prefs.Single(p => p.key == "deadlock.uiLayout.home").stringValue;
            Assert.AreEqual(IosIncident20261028.HomeLayoutSha256, DeviceDataRecovery.Sha256(layout));
            if (File.Exists(BackupLog))
            {
                string recorded = File.ReadAllLines(BackupLog).First(l => l.StartsWith("saved layout at start: ")).Substring("saved layout at start: ".Length);
                Assert.AreEqual(recorded, layout, "the exact JSON recorded on the device on 10-27");
            }
            var data = JsonUtility.FromJson<UiLayoutData>(layout);
            Assert.AreEqual(HomeMenu.LayoutVersion, data.layoutVersion);
            Assert.AreEqual(1.1f, data.Get("logo").scale, 1e-5f, "logo scale 1.1 comes from the layout itself");

            foreach (var (path, wid) in new[] { (BackupRifle, "proto_rifle"), (BackupSmg, "proto_smg") })
            {
                byte[] bytes = Convert.FromBase64String(bundle.files.Single(f => f.weaponId == wid).contentBase64);
                if (File.Exists(path))
                    CollectionAssert.AreEqual(File.ReadAllBytes(path), bytes, wid + ": byte for byte the backup");
            }
        }

        [Test]
        public void Apply_ToEmptyStorage_RestoresEverything_AsRecorded()
        {
            var bundle = Bundle();
            var report = Apply(bundle);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(10, report.Count(DeviceDataRecovery.Written), "8 settings + 2 files");

            string layout = bundle.prefs.Single(p => p.key == UiLayoutStore.Key(HomeMenu.LayoutId)).stringValue;
            Assert.AreEqual(layout, prefs.GetString("deadlock.uiLayout.home"), "the layout string is stored unchanged");
            var shown = UiLayoutMath.Resolve(JsonUtility.FromJson<UiLayoutData>(prefs.GetString("deadlock.uiLayout.home")),
                new UiLayoutData { layoutVersion = HomeMenu.LayoutVersion, items = IosIncident20261028.HomeLayoutIds.Select(id => new UiLayoutItem { id = id, scale = 1f }).ToList() },
                HomeMenu.LayoutVersion, out bool usedDefaults);
            Assert.IsFalse(usedDefaults, "the home uses the restored layout, not its defaults");
            Assert.AreEqual(1.1f, shown.Get("logo").scale, 1e-5f);

            foreach (var t in HomePanelLook.Targets)
                Assert.AreEqual(50, prefs.GetInt(HomePanelLook.Key(t)), t + " transparency");
            Assert.AreEqual(6, HomePanelLook.Targets.Length);
            Assert.AreEqual(0, prefs.GetInt(HomePanelTuner.CompareKey), "legacy comparison OFF");
            Assert.AreEqual(1, prefs.Saves, "saved once, through the settings storage");

            var store = new WeaponBalanceStore(weapons);
            Assert.AreEqual(IosIncident20261028.RifleSha256, DeviceDataRecovery.Sha256(File.ReadAllBytes(store.PathOf("proto_rifle"))));
            Assert.AreEqual(IosIncident20261028.SmgSha256, DeviceDataRecovery.Sha256(File.ReadAllBytes(store.PathOf("proto_smg"))));
            Assert.IsTrue(store.TryLoad("proto_rifle", out var rifle, out string e1), e1);
            Assert.IsTrue(store.TryLoad("proto_smg", out var smg, out string e2), e2);
            Assert.AreEqual(RecoilAxis.Diagonal, rifle.recoil.axis);
            Assert.AreEqual(RecoilVertical.Up, rifle.recoil.vertical);
            Assert.AreEqual(RecoilHorizontal.Left, rifle.recoil.horizontal);
            Assert.AreEqual(RecoilStrength.Medium, rifle.recoil.strength);
            Assert.AreEqual(3f, rifle.recoil.yawDegrees);
            Assert.AreEqual(1f, rifle.recoil.recoveryFraction);
            Assert.AreEqual(3f, rifle.recoil.recoverySpeed);
            Assert.AreEqual("2026-10-27T19:04:08+09:00", rifle.savedAt, "the saved time is kept (not re-saved)");
            Assert.IsFalse(smg.recoil.enabled);
            Assert.AreEqual(28f, smg.damage);
            Assert.IsFalse(Directory.GetFiles(weapons).Any(f => f.EndsWith(DeviceDataRecovery.TempSuffix)), "no temporary file left");
        }

        [Test]
        public void DryRun_ShowsThePlan_WritesNothing()
        {
            prefs.Values[HomePanelLook.Key(HomePanelTarget.Crate)] = 50;
            var report = DeviceDataRecovery.Run(Bundle(), RecoveryMode.DryRun, new[] { "all" }, prefs, weapons);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual("dry-run", report.mode);
            Assert.AreEqual(9, report.Count(DeviceDataRecovery.Write));
            Assert.AreEqual(1, report.Count(DeviceDataRecovery.AlreadyRestored));
            Assert.IsTrue(report.entries.Any(e => e.target == "File:WeaponBalanceLab/proto_rifle.json" && e.detail.Contains("871 bytes")));
            Assert.IsTrue(report.entries.Any(e => e.target == "PlayerPrefs:deadlock.uiLayout.home" && e.detail.Contains(IosIncident20261028.HomeLayoutSha256)));
            Assert.AreEqual(0, prefs.Writes.Count);
            Assert.AreEqual(0, prefs.Saves);
            Assert.IsFalse(Directory.Exists(weapons), "no folder or file made");
        }

        /// <summary>Dry-run on empty storage (a fresh install); DEADLOCK_RECOVERY_TEST_REPORT=folder keeps its report.</summary>
        [Test]
        public void DryRun_OnAFreshInstall_PlansAllTen()
        {
            var report = DeviceDataRecovery.Run(Bundle(), RecoveryMode.DryRun, new[] { "all" }, prefs, weapons);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(10, report.Count(DeviceDataRecovery.Write));
            string keep = Environment.GetEnvironmentVariable("DEADLOCK_RECOVERY_TEST_REPORT");
            if (!string.IsNullOrEmpty(keep))
                File.WriteAllText(Path.Combine(keep, "dry_run_fresh_install.json"), JsonUtility.ToJson(report, true));
        }

        [Test]
        public void ExistingValues_AreKept_NotOverwritten()
        {
            var bundle = Bundle();
            prefs.Values["deadlock.uiLayout.home"] = "{\"layoutVersion\":2,\"items\":[]}";
            prefs.Values[HomePanelLook.Key(HomePanelTarget.MainButton)] = 20;
            prefs.Values[HomePanelTuner.CompareKey] = 1;
            Directory.CreateDirectory(weapons);
            File.WriteAllText(Path.Combine(weapons, "proto_rifle.json"), "{\"weaponId\":\"proto_rifle\",\"mine\":true}");

            var report = Apply(bundle);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(4, report.Count(DeviceDataRecovery.KeptExisting));
            Assert.AreEqual("{\"layoutVersion\":2,\"items\":[]}", prefs.GetString("deadlock.uiLayout.home"));
            Assert.AreEqual(20, prefs.GetInt(HomePanelLook.Key(HomePanelTarget.MainButton)));
            Assert.AreEqual(1, prefs.GetInt(HomePanelTuner.CompareKey));
            Assert.AreEqual("{\"weaponId\":\"proto_rifle\",\"mine\":true}", File.ReadAllText(Path.Combine(weapons, "proto_rifle.json")));
            Assert.AreEqual(50, prefs.GetInt(HomePanelLook.Key(HomePanelTarget.Row)), "the missing ones are still restored");
            Assert.IsTrue(File.Exists(Path.Combine(weapons, "proto_smg.json")));
            CollectionAssert.DoesNotContain(prefs.Writes, "deadlock.uiLayout.home");
        }

        [Test]
        public void OtherSettingsAndFiles_AreNotTouched()
        {
            prefs.Values["deadlock.touchLayout.v1"] = "{\"mine\":1}";
            prefs.Values["deadlock.hud.minimap.visible"] = 0;
            prefs.Values["deadlock.controls.touchAds"] = 2;
            prefs.Values["deadlock.net.lastHost"] = "192.168.0.9";
            Directory.CreateDirectory(weapons);
            File.WriteAllText(Path.Combine(weapons, "proto_shotgun.json"), "other");
            File.WriteAllText(Path.Combine(dir, "Documents", "notes.txt"), "keep");

            var report = Apply(Bundle());
            Assert.IsTrue(report.ok, report.error);
            var allowed = IosIncident20261028.Prefs().Select(p => p.key).ToList();
            Assert.IsTrue(prefs.Writes.All(allowed.Contains), "only whitelisted keys are written: " + string.Join(",", prefs.Writes));
            Assert.AreEqual("{\"mine\":1}", prefs.GetString("deadlock.touchLayout.v1"), "the estimated touch layout is not restored");
            Assert.AreEqual(0, prefs.GetInt("deadlock.hud.minimap.visible"));
            Assert.AreEqual(2, prefs.GetInt("deadlock.controls.touchAds"));
            Assert.AreEqual("192.168.0.9", prefs.GetString("deadlock.net.lastHost"));
            Assert.AreEqual("other", File.ReadAllText(Path.Combine(weapons, "proto_shotgun.json")));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(dir, "Documents", "notes.txt")));
            CollectionAssert.AreEquivalent(new[] { "notes.txt", "WeaponBalanceLab" },
                Directory.GetFileSystemEntries(Path.Combine(dir, "Documents")).Select(Path.GetFileName));
        }

        [Test]
        public void RunningTwice_ChangesNothingTheSecondTime()
        {
            var bundle = Bundle();
            Assert.IsTrue(Apply(bundle).ok);
            var store = new WeaponBalanceStore(weapons);
            var time = File.GetLastWriteTimeUtc(store.PathOf("proto_rifle"));
            var values = new Dictionary<string, object>(prefs.Values);
            int writes = prefs.Writes.Count, saves = prefs.Saves;

            var second = Apply(bundle);
            Assert.IsTrue(second.ok, second.error);
            Assert.AreEqual(10, second.Count(DeviceDataRecovery.AlreadyRestored));
            Assert.AreEqual(writes, prefs.Writes.Count, "no setting written again");
            Assert.AreEqual(saves, prefs.Saves);
            CollectionAssert.AreEquivalent(values, prefs.Values);
            Assert.AreEqual(time, File.GetLastWriteTimeUtc(store.PathOf("proto_rifle")));
            Assert.AreEqual(IosIncident20261028.RifleSha256, DeviceDataRecovery.Sha256(File.ReadAllBytes(store.PathOf("proto_rifle"))));
        }

        [Test]
        public void OnlyTheChosenItems_AreRestored()
        {
            var report = Apply(Bundle(), IosIncident20261028.SmgItem, IosIncident20261028.PanelCompareItem);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(2, report.Count(DeviceDataRecovery.Written));
            Assert.AreEqual(8, report.Count(DeviceDataRecovery.NotSelected));
            CollectionAssert.AreEqual(new[] { HomePanelTuner.CompareKey }, prefs.Writes);
            CollectionAssert.AreEqual(new[] { "proto_smg.json" }, Directory.GetFiles(weapons).Select(Path.GetFileName));

            var unknown = Apply(Bundle(), "touch-layout");
            Assert.IsFalse(unknown.ok);
            StringAssert.Contains("不明な項目", unknown.error);
            var none = DeviceDataRecovery.Run(Bundle(), RecoveryMode.Apply, new string[0], prefs, weapons);
            Assert.IsFalse(none.ok);
        }

        static IEnumerable<TestCaseData> BrokenBundles()
        {
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.prefs[0].stringValue = b.prefs[0].stringValue.Replace("0.1967005729675293", "0.1967005729675294"))).SetName("Refused_LayoutChanged_HashMismatch");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.prefs[0].sha256 = new string('0', 64))).SetName("Refused_LayoutHashFieldWrong");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.files[0].contentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }))).SetName("Refused_WeaponFileChanged_HashMismatch");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.files[1].contentBase64 = "!!not base64!!")).SetName("Refused_WeaponFileNotBase64");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.prefs[1].intValue = 40)).SetName("Refused_TransparencyNotTheConfirmedValue");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.prefs.Add(new RecoveryPref { item = "home-layout", key = "deadlock.touchLayout.v1", kind = "string", stringValue = "{}" }))).SetName("Refused_SettingNotInWhitelist");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.files[0].weaponId = "proto_shotgun")).SetName("Refused_FileNotInWhitelist");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.bundleId = "other")).SetName("Refused_OtherBundle");
            yield return new TestCaseData((Action<RecoveryBundle>)(b => b.formatVersion = 2)).SetName("Refused_OtherFormatVersion");
        }

        [TestCaseSource(nameof(BrokenBundles))]
        public void BrokenData_StopsBeforeWritingAnything(Action<RecoveryBundle> breakIt)
        {
            var bundle = Bundle();
            breakIt(bundle);
            var report = Apply(bundle);
            Assert.IsFalse(report.ok);
            Assert.IsNotEmpty(report.error);
            Assert.AreEqual(0, report.entries.Count, "stopped before planning");
            Assert.AreEqual(0, prefs.Writes.Count);
            Assert.IsFalse(Directory.Exists(weapons));
        }

        [Test]
        public void BrokenJson_StopsBeforeWritingAnything()
        {
            string reports = Path.Combine(dir, "reports");
            var env = new Dictionary<string, string> { ["DEADLOCK_RECOVERY"] = "apply", ["DEADLOCK_RECOVERY_BUNDLE"] = IosIncident20261028.BundleId, ["DEADLOCK_RECOVERY_ITEMS"] = "all" };
            foreach (string json in new[] { "{not json", "{}", "[1,2]" })
            {
                var report = DeviceDataRecoveryBoot.RunFromEnvironment(k => env.TryGetValue(k, out var v) ? v : null, json, prefs, weapons, reports);
                Assert.IsFalse(report.ok, json);
                Assert.AreEqual(0, prefs.Writes.Count);
                Assert.IsFalse(Directory.Exists(weapons));
            }
            if (BundleJson() != null)
            {
                string broken = BundleJson().Replace("\"intValue\":50", "\"intValue\":51");
                Assert.IsFalse(DeviceDataRecoveryBoot.RunFromEnvironment(k => env.TryGetValue(k, out var v) ? v : null, broken, prefs, weapons, reports).ok);
                Assert.AreEqual(0, prefs.Writes.Count);
            }
            Assert.AreEqual(4 - (BundleJson() == null ? 1 : 0), Directory.GetFiles(reports).Length, "every refused run is reported");
        }

        [Test]
        public void Launch_WithoutTheRequest_DoesNothing_AndApplyNeedsBundleIdAndItems()
        {
            string json = BundleJson() ?? "{}";
            string reports = Path.Combine(dir, "reports");
            Assert.IsNull(DeviceDataRecoveryBoot.RunFromEnvironment(_ => null, json, prefs, weapons, reports), "normal launch");
            Assert.IsFalse(Directory.Exists(reports), "nothing written, not even a report");

            var env = new Dictionary<string, string> { ["DEADLOCK_RECOVERY"] = "apply", ["DEADLOCK_RECOVERY_BUNDLE"] = IosIncident20261028.BundleId };
            Func<string, string> read = k => env.TryGetValue(k, out var v) ? v : null;
            StringAssert.Contains("DEADLOCK_RECOVERY_ITEMS", DeviceDataRecoveryBoot.RunFromEnvironment(read, json, prefs, weapons, reports).error);
            env["DEADLOCK_RECOVERY_ITEMS"] = "all";
            env["DEADLOCK_RECOVERY_BUNDLE"] = "ios-incident-20261029";
            StringAssert.Contains("DEADLOCK_RECOVERY_BUNDLE", DeviceDataRecoveryBoot.RunFromEnvironment(read, json, prefs, weapons, reports).error);
            env["DEADLOCK_RECOVERY"] = "force";
            Assert.IsFalse(DeviceDataRecoveryBoot.RunFromEnvironment(read, json, prefs, weapons, reports).ok);
            env["DEADLOCK_RECOVERY"] = "apply";
            env["DEADLOCK_RECOVERY_BUNDLE"] = IosIncident20261028.BundleId;
            Assert.IsFalse(DeviceDataRecoveryBoot.RunFromEnvironment(read, null, prefs, weapons, reports).ok, "a build without the data");
            Assert.AreEqual(0, prefs.Writes.Count);
            Assert.IsFalse(Directory.Exists(weapons));

            if (BundleJson() == null)
                return;
            env.Remove("DEADLOCK_RECOVERY_ITEMS");
            env["DEADLOCK_RECOVERY"] = "dry-run";
            var dry = DeviceDataRecoveryBoot.RunFromEnvironment(read, json, prefs, weapons, reports);
            Assert.IsTrue(dry.ok, dry.error);
            Assert.AreEqual(10, dry.Count(DeviceDataRecovery.Write), "dry-run covers all items when none are named");
            Assert.AreEqual(0, prefs.Writes.Count);
            string saved = Directory.GetFiles(reports, "recovery_dry-run_*.json").Single();
            var back = JsonUtility.FromJson<RecoveryReport>(File.ReadAllText(saved));
            Assert.AreEqual(10, back.entries.Count, "per key / per file report");
        }
    }
}
