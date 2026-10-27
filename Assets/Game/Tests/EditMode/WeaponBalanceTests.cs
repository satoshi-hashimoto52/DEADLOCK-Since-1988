using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using Deadlock.Dev;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Weapon balance lab, phase 1: recoil direction / strength, damage falloff, trial profiles and their files.</summary>
    public class WeaponBalanceTests
    {
        const string RiflePath = "Assets/Game/Data/Weapon_ProtoRifle.asset";
        const string SmgPath = "Assets/Game/Data/Weapon_ProtoSMG_Candidate.asset";

        static RecoilProfile Recoil(RecoilAxis axis, RecoilVertical v, RecoilHorizontal h, RecoilStrength s = RecoilStrength.Medium) =>
            new RecoilProfile { enabled = true, axis = axis, vertical = v, horizontal = h, strength = s, pitchDegrees = 1f, yawDegrees = 0.5f, adsMultiplier = 0.5f };

        static (float pitch, float yaw) Kick(RecoilProfile p, int shot = 0, float ads = 0f)
        {
            Assert.IsTrue(RecoilCalculator.TryCompute(p, shot, ads, out float pitch, out float yaw));
            return (pitch, yaw);
        }

        // ------------------------------------------------------------------ recoil

        [Test]
        public void Recoil_UpDown_LeftRight_Diagonal()
        {
            Assert.AreEqual((1f, 0f), Kick(Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None)));
            Assert.AreEqual((-1f, 0f), Kick(Recoil(RecoilAxis.Vertical, RecoilVertical.Down, RecoilHorizontal.None)));
            Assert.AreEqual((0f, -0.5f), Kick(Recoil(RecoilAxis.Horizontal, RecoilVertical.None, RecoilHorizontal.Left)));
            Assert.AreEqual((0f, 0.5f), Kick(Recoil(RecoilAxis.Horizontal, RecoilVertical.None, RecoilHorizontal.Right)));
            Assert.AreEqual((1f, -0.5f), Kick(Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Left)), "左上");
            Assert.AreEqual((1f, 0.5f), Kick(Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Right)), "右上");
            Assert.AreEqual((-1f, -0.5f), Kick(Recoil(RecoilAxis.Diagonal, RecoilVertical.Down, RecoilHorizontal.Left)), "左下");
            Assert.AreEqual((-1f, 0.5f), Kick(Recoil(RecoilAxis.Diagonal, RecoilVertical.Down, RecoilHorizontal.Right)), "右下");
        }

        [Test]
        public void Recoil_Alternating_ByShotNumber_Repeatable()
        {
            var mixed = Recoil(RecoilAxis.Mixed, RecoilVertical.Up, RecoilHorizontal.Alternating);
            Assert.AreEqual((1f, 0.5f), Kick(mixed, 0), "even shots right");
            Assert.AreEqual((1f, -0.5f), Kick(mixed, 1), "odd shots left");
            Assert.AreEqual((1f, 0.5f), Kick(mixed, 2));
            Assert.AreEqual(Kick(mixed, 7), Kick(mixed, 7), "same input, same kick (no randomness)");
            var sides = Recoil(RecoilAxis.Horizontal, RecoilVertical.None, RecoilHorizontal.Alternating);
            Assert.AreEqual((0f, 0.5f), Kick(sides, 0));
            Assert.AreEqual((0f, -0.5f), Kick(sides, 1));
            Assert.AreEqual("上＋左右交互", RecoilCalculator.DirectionName(mixed));
            Assert.AreEqual("左上", RecoilCalculator.DirectionName(Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Left)));
        }

        [Test]
        public void Recoil_Strength_ThreeSteps_And_AdsFactor()
        {
            Assert.AreEqual(0.7f, Kick(Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None, RecoilStrength.Weak)).pitch, 1e-5f);
            Assert.AreEqual(1f, Kick(Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None, RecoilStrength.Medium)).pitch, 1e-5f);
            Assert.AreEqual(1.4f, Kick(Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None, RecoilStrength.Strong)).pitch, 1e-5f);
            var p = Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Right, RecoilStrength.Strong);
            var ads = Kick(p, 0, 1f);
            Assert.AreEqual(1.4f * 0.5f, ads.pitch, 1e-5f, "fully aimed: × ADS factor");
            Assert.AreEqual(0.5f * 1.4f * 0.5f, ads.yaw, 1e-5f);
            Assert.AreEqual(1.4f * 0.75f, Kick(p, 0, 0.5f).pitch, 1e-5f, "half aimed: halfway");
        }

        [Test]
        public void Recoil_ContradictoryOrBadValues_AreRejected_NoKick()
        {
            var bad = new[]
            {
                Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.Left),
                Recoil(RecoilAxis.Vertical, RecoilVertical.None, RecoilHorizontal.None),
                Recoil(RecoilAxis.Horizontal, RecoilVertical.Up, RecoilHorizontal.Right),
                Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Alternating),
                Recoil(RecoilAxis.Mixed, RecoilVertical.Up, RecoilHorizontal.Left),
                Recoil(RecoilAxis.Mixed, RecoilVertical.None, RecoilHorizontal.Alternating),
            };
            foreach (var p in bad)
            {
                Assert.IsFalse(p.IsValid(out string error), $"{p.axis} {p.vertical} {p.horizontal}");
                Assert.IsNotEmpty(error);
                Assert.IsFalse(RecoilCalculator.TryCompute(p, 0, 0f, out float pitch, out float yaw));
                Assert.AreEqual(0f, pitch);
                Assert.AreEqual(0f, yaw);
            }
            var nan = Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None);
            nan.pitchDegrees = float.NaN;
            Assert.IsFalse(nan.IsValid(out _));
            Assert.IsFalse(RecoilCalculator.TryCompute(null, 0, 0f, out _, out _));
        }

        [Test]
        public void Recoil_ExistingWeapons_HaveNoProfile_KeepTheEarlierRecoil()
        {
            foreach (var path in new[] { RiflePath, SmgPath })
            {
                var d = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                Assert.IsNotNull(d, path);
                Assert.IsTrue(d.recoilProfile == null || !d.recoilProfile.enabled, path + ": no data-driven recoil");
                Assert.IsTrue(d.damageFalloff == null || d.damageFalloff.IsEmpty, path + ": no falloff");
            }
        }

        [Test]
        public void Online_HostUsesTheWeaponAsset_NotALabProfile()
        {
            // The online avatar's host-side damage comes from its own serialized weapon asset (the rifle). The lab only
            // writes runtime clones and its own files, so test values of either player never reach an online match.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Net_Avatar.prefab");
            Assert.IsNotNull(prefab);
            WeaponDefinition used = null;
            foreach (var mb in prefab.GetComponents<MonoBehaviour>())
            {
                var prop = new SerializedObject(mb).FindProperty("gunDefinition");
                if (prop != null)
                    used = prop.objectReferenceValue as WeaponDefinition;
            }
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RiflePath), used, "the rifle asset itself");
            Assert.IsFalse(used.recoilProfile.enabled);
            Assert.IsTrue(used.damageFalloff.IsEmpty, "full damage online, as before");
        }

        // ------------------------------------------------------------------ falloff

        static DamageFalloffProfile Example() => WeaponBalanceProfile.ExampleFalloff();

        [Test]
        public void Falloff_OnPoints_Between_Outside()
        {
            var f = Example();
            Assert.IsTrue(f.IsValid(out _));
            Assert.AreEqual(1f, f.Evaluate(0f));
            Assert.AreEqual(1f, f.Evaluate(25f));
            Assert.AreEqual(0.8f, f.Evaluate(40f), 1e-5f);
            Assert.AreEqual(0.4f, f.Evaluate(80f), 1e-5f);
            Assert.AreEqual(0.9f, f.Evaluate(32.5f), 1e-5f, "linear between 25 m (1.0) and 40 m (0.8)");
            Assert.AreEqual(0.7f, f.Evaluate(50f), 1e-5f);
            Assert.AreEqual(0.4f, f.Evaluate(150f), 1e-5f, "past the last point: the last multiplier");
            var late = new DamageFalloffProfile { points = new List<FalloffPoint> { new FalloffPoint(20f, 0.9f), new FalloffPoint(40f, 0.5f) } };
            Assert.AreEqual(0.9f, late.Evaluate(5f), 1e-5f, "before the first point: the first multiplier");
        }

        [Test]
        public void Falloff_None_IsFullDamage()
        {
            Assert.AreEqual(1f, new DamageFalloffProfile().Evaluate(70f));
            Assert.AreEqual(1f, DamageFalloffProfile.Evaluate(null, 70f));
            Assert.IsTrue(new DamageFalloffProfile().IsValid(out _));
        }

        [Test]
        public void Falloff_InvalidProfiles_AreNotApplied()
        {
            DamageFalloffProfile Of(params (float d, float m)[] pts)
            {
                var f = new DamageFalloffProfile();
                foreach (var (d, m) in pts)
                    f.points.Add(new FalloffPoint(d, m));
                return f;
            }
            var bad = new[]
            {
                Of((-1f, 1f), (10f, 0.5f)),
                Of((0f, 1f), (10f, 0.5f), (10f, 0.4f)),
                Of((0f, 1f), (30f, 0.5f), (20f, 0.4f)),
                Of((0f, 1.2f)),
                Of((0f, -0.1f)),
                Of((float.NaN, 1f)),
                Of((0f, float.PositiveInfinity)),
                Of((float.PositiveInfinity, 0.5f)),
            };
            foreach (var f in bad)
            {
                Assert.IsFalse(f.IsValid(out string error));
                Assert.IsNotEmpty(error);
                Assert.AreEqual(1f, f.Evaluate(50f), "an invalid profile changes nothing");
            }
        }

        // ------------------------------------------------------------------ profiles and files

        [Test]
        public void Profile_FromDefinition_Columns_ApplyToClone_AssetUntouched()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RiflePath);
            float damage = rifle.damage, interval = rifle.fireInterval;
            var p = WeaponBalanceProfile.FromDefinition(rifle);
            Assert.AreEqual("proto_rifle", p.weaponId);
            Assert.AreEqual("ライフル", p.weaponType);
            Assert.AreEqual(60f / interval, p.rpm, 1e-3f);
            Assert.IsFalse(p.recoil.enabled);
            Assert.AreEqual(18, WeaponBalanceProfile.Columns.Length);
            Assert.AreEqual("従来（上・左右ランダム）", WeaponBalanceProfile.RecoilDirectionText(p));
            Assert.AreEqual("なし（1.0）", WeaponBalanceProfile.FalloffText(p));
            Assert.IsEmpty(WeaponBalanceProfile.Diff(p, p.Clone()));

            var e = p.Clone();
            e.damage = 30f;
            e.rpm = 900f;
            e.recoil = Recoil(RecoilAxis.Mixed, RecoilVertical.Up, RecoilHorizontal.Alternating, RecoilStrength.Strong);
            e.falloff = Example();
            Assert.IsTrue(e.CanApply);
            var diff = WeaponBalanceProfile.Diff(p, e);
            CollectionAssert.IsSubsetOf(new[] { "基本威力", "RPM", "反動方向", "反動強度", "距離減衰" }, diff.ConvertAll(d => d.column));

            var clone = Object.Instantiate(rifle);
            try
            {
                e.ApplyTo(clone);
                Assert.AreEqual(30f, clone.damage);
                Assert.AreEqual(60f / 900f, clone.fireInterval, 1e-6f);
                Assert.IsTrue(clone.recoilProfile.enabled);
                Assert.AreEqual(6, clone.damageFalloff.points.Count);
                Assert.AreEqual(damage, rifle.damage, "the weapon asset is not written");
                Assert.AreEqual(interval, rifle.fireInterval);
                Assert.IsFalse(rifle.recoilProfile.enabled);
                Assert.IsTrue(rifle.damageFalloff.IsEmpty);
                Assert.AreNotSame(e.falloff, clone.damageFalloff, "copied, not shared");
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void Profile_Problems_KeepBadValuesFromBeingApplied()
        {
            var p = WeaponBalanceProfile.FromDefinition(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(SmgPath));
            Assert.AreEqual("SMG", p.weaponType);
            p.rpm = 0f;
            p.startReserve = p.reserveMax + 1;
            p.recoil = Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.Left);
            p.falloff.points.Add(new FalloffPoint(-5f, 1f));
            var problems = p.Problems();
            Assert.AreEqual(4, problems.Count, string.Join(" / ", problems));
            Assert.IsFalse(p.CanApply);
        }

        [Test]
        public void Store_SaveLoadDelete_InItsOwnFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DeadlockWeaponLabTest_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new WeaponBalanceStore(dir);
                var p = WeaponBalanceProfile.FromDefinition(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(SmgPath));
                Assert.IsFalse(store.TryLoad("proto_smg", out _, out string none));
                Assert.IsNotEmpty(none);
                p.damage = 21f;
                p.recoil = Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Left, RecoilStrength.Weak);
                p.falloff = Example();
                store.Save(p, "abc1234");
                Assert.IsTrue(File.Exists(store.PathOf("proto_smg")));
                Assert.IsTrue(store.TryLoad("proto_smg", out var loaded, out _));
                Assert.AreEqual("proto_smg", loaded.weaponId);
                Assert.AreEqual(WeaponBalanceProfile.CurrentVersion, loaded.profileVersion);
                Assert.AreEqual("abc1234", loaded.baseCommit);
                Assert.IsNotEmpty(loaded.savedAt);
                Assert.AreEqual(21f, loaded.damage);
                Assert.AreEqual(RecoilAxis.Diagonal, loaded.recoil.axis);
                Assert.AreEqual(RecoilStrength.Weak, loaded.recoil.strength);
                Assert.AreEqual(6, loaded.falloff.points.Count);
                Assert.AreEqual(0.8f, loaded.falloff.Evaluate(40f), 1e-5f);
                Assert.IsTrue(store.Delete("proto_smg"));
                Assert.IsFalse(store.TryLoad("proto_smg", out _, out _));
                // A file of another weapon id / version is not taken.
                File.WriteAllText(store.PathOf("proto_rifle"), "{\"weaponId\":\"proto_smg\",\"profileVersion\":1}");
                Assert.IsFalse(store.TryLoad("proto_rifle", out _, out _));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
