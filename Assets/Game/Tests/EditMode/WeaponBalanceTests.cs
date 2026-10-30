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

        [Test]
        public void Directions_PickTheAxis_BothNoneRefused()
        {
            Assert.AreEqual(RecoilAxis.Vertical, RecoilProfile.AxisFor(RecoilVertical.Up, RecoilHorizontal.None));
            Assert.AreEqual(RecoilAxis.Horizontal, RecoilProfile.AxisFor(RecoilVertical.None, RecoilHorizontal.Left));
            Assert.AreEqual(RecoilAxis.Diagonal, RecoilProfile.AxisFor(RecoilVertical.Down, RecoilHorizontal.Left));
            Assert.AreEqual(RecoilAxis.Diagonal, RecoilProfile.AxisFor(RecoilVertical.Up, RecoilHorizontal.Right));
            Assert.AreEqual(RecoilAxis.Mixed, RecoilProfile.AxisFor(RecoilVertical.Up, RecoilHorizontal.Alternating));
            Assert.AreEqual(RecoilAxis.Horizontal, RecoilProfile.AxisFor(RecoilVertical.None, RecoilHorizontal.Alternating));
            Assert.IsNull(RecoilProfile.AxisFor(RecoilVertical.None, RecoilHorizontal.None));
            var p = Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None);
            foreach (RecoilVertical v in System.Enum.GetValues(typeof(RecoilVertical)))
                foreach (RecoilHorizontal h in System.Enum.GetValues(typeof(RecoilHorizontal)))
                {
                    bool ok = p.SetDirections(v, h);
                    Assert.AreEqual(!(v == RecoilVertical.None && h == RecoilHorizontal.None), ok, $"{v} {h}");
                    Assert.IsTrue(p.IsValid(out string e), $"{v} {h}: {e}");
                }
        }

        [Test]
        public void Recovery_AmountAndSpeed_Validated_Separately()
        {
            var p = Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None);
            Assert.AreEqual(0.5f, p.recoveryFraction, "default: the earlier half");
            Assert.AreEqual(9f, p.recoverySpeed, "default: the scenes' 9 °/s");
            Assert.IsTrue(p.IsValid(out _));
            foreach (var bad in new[] { -0.1f, 1.1f, float.NaN })
            {
                p.recoveryFraction = bad;
                Assert.IsFalse(p.IsValid(out _), "amount " + bad);
            }
            p.recoveryFraction = 0.3f;
            foreach (var bad in new[] { 0f, 0.4f, 61f, float.PositiveInfinity })
            {
                p.recoverySpeed = bad;
                Assert.IsFalse(p.IsValid(out _), "speed " + bad);
            }
            p.recoverySpeed = 60f;
            Assert.IsTrue(p.IsValid(out _));
        }

        [Test]
        public void ViewPreview_WithRecovery_AmountSetsWhatStays_SpeedSetsHowFast()
        {
            RecoilProfile R(float fraction, float speed)
            {
                var p = Recoil(RecoilAxis.Vertical, RecoilVertical.Up, RecoilHorizontal.None);
                p.pitchDegrees = 1f;
                p.recoveryFraction = fraction;
                p.recoverySpeed = speed;
                return p;
            }
            // 8 shots of 1° at 0.1 s; the last point is 1 s after the last shot.
            var none = WeaponBalanceLab.PreviewView(R(0f, 9f), 0.1f, 9f);
            Assert.AreEqual(8f, none[none.Count - 1].y, 1e-3f, "amount 0: everything stays");
            var half = WeaponBalanceLab.PreviewView(R(0.5f, 60f), 0.1f, 9f);
            Assert.AreEqual(4f, half[half.Count - 1].y, 1e-3f, "amount 0.5, fast: half stays");
            var slow = WeaponBalanceLab.PreviewView(R(0.5f, 1f), 0.1f, 9f);
            Assert.Greater(slow[8].y, half[8].y, "slow speed: more piled up at the 8th shot");
            // 1 °/s for 0.7 s of firing + 1 s after: about 1.7° of the 4° share has returned.
            Assert.AreEqual(8f - 1.7f, slow[slow.Count - 1].y, 0.05f, "… and still on the way back after 1 s");
            var legacy = WeaponBalanceLab.PreviewView(new RecoilProfile { enabled = false, pitchDegrees = 0.7f }, 0.1f, 9f);
            Assert.AreEqual(0.7f * 8f * 0.5f, legacy[legacy.Count - 1].y, 1e-3f, "the earlier recoil: half stays");
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
            Assert.AreEqual(20, WeaponBalanceProfile.Columns.Length, "18 + recovery amount and speed");
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

        // ------------------------------------------------------------------ A / B editor (sliders, numbers, comparison, HUD spot)

        [Test]
        public void RecoilParams_CoarseAndFineWindows_TypedValuesOutsideTheRangeRefused()
        {
            // Coarse: the whole range at 0.05° (angles) / 0.5 °/s (recovery speed).
            Assert.AreEqual((0f, 200), RecoilParams.Window(RecoilParam.Pitch, false, 0.7f));
            Assert.AreEqual((0.5f, 119), RecoilParams.Window(RecoilParam.RecoverySpeed, false, 9f));
            Assert.AreEqual(0.7f, RecoilParams.ValueAt(RecoilParam.Pitch, false, 0f, 14), 1e-5f);
            // Fine: ±0.5° around the value at 0.01°; the window stays while the value is inside it.
            var (lo, steps) = RecoilParams.Window(RecoilParam.Pitch, true, 0.7f);
            Assert.AreEqual(0.2f, lo, 1e-5f);
            Assert.AreEqual(100, steps);
            Assert.AreEqual(0.71f, RecoilParams.ValueAt(RecoilParam.Pitch, true, lo, 51), 1e-5f);
            Assert.AreEqual(0.2f, RecoilParams.Window(RecoilParam.Pitch, true, 1.1f, lo).lo, 1e-5f, "kept while inside");
            Assert.AreEqual(0.75f, RecoilParams.Window(RecoilParam.Pitch, true, 1.25f, lo).lo, 1e-5f, "re-centred outside");
            Assert.AreEqual(0f, RecoilParams.Window(RecoilParam.Pitch, true, 0.1f).lo, "clamped at the range's start");
            Assert.AreEqual(9f, RecoilParams.Window(RecoilParam.Pitch, true, 9.9f).lo, 1e-5f, "and end");
            Assert.AreEqual((0.45f, 20), RecoilParams.Window(RecoilParam.Ads, true, 0.55f));
            Assert.AreEqual(0.01f, RecoilParams.Step(RecoilParam.RecoveryAmount, true));
            Assert.AreEqual(0.1f, RecoilParams.Step(RecoilParam.RecoverySpeed, true), 1e-6f);
            // Ranges of typed numbers.
            Assert.IsTrue(RecoilParams.InRange(RecoilParam.Pitch, 10f));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.Pitch, 10.5f));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.Yaw, -0.1f));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.Ads, 1.2f));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.RecoverySpeed, 0.2f));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.RecoveryAmount, float.NaN));
        }

        [Test]
        public void RecoilParams_ShownWithOneUnitEach_RecoveryAmountInPercent()
        {
            Assert.AreEqual("0.70°", RecoilParams.Shown(RecoilParam.Pitch, 0.7f));
            Assert.AreEqual("0.25°", RecoilParams.Shown(RecoilParam.Yaw, 0.25f));
            Assert.AreEqual("0.55倍", RecoilParams.Shown(RecoilParam.Ads, 0.55f));
            Assert.AreEqual("50%", RecoilParams.Shown(RecoilParam.RecoveryAmount, 0.5f));
            Assert.AreEqual("9.0°/秒", RecoilParams.Shown(RecoilParam.RecoverySpeed, 9f));
            Assert.AreEqual("50", RecoilParams.Text(RecoilParam.RecoveryAmount, 0.5f), "the field shows the number only");
            Assert.IsTrue(RecoilParams.TryParse(RecoilParam.RecoveryAmount, "30", out float v));
            Assert.AreEqual(0.3f, v, 1e-6f, "typed in percent, stored as a fraction");
            Assert.IsTrue(RecoilParams.TryParse(RecoilParam.Pitch, "1.25", out v));
            Assert.AreEqual(1.25f, v, 1e-6f);
            Assert.IsFalse(RecoilParams.TryParse(RecoilParam.Pitch, "abc", out _));
            Assert.IsFalse(RecoilParams.InRange(RecoilParam.RecoveryAmount, 150f / RecoilParams.Scale(RecoilParam.RecoveryAmount)), "150 % refused");
        }

        [Test]
        public void AB_Comparison_ItemsAndRows_DifferExactlyWhereTheValuesDo()
        {
            var a = new RecoilProfile { enabled = false, pitchDegrees = 0.7f, yawDegrees = 0.25f, adsMultiplier = 0.55f };
            var b = a.Clone();
            Assert.AreEqual(0, RecoilParams.DiffCount(a, b, 9f));
            var items = RecoilParams.Compare(a, b, 9f);
            CollectionAssert.AreEqual(new[] { "反動方向", "反動強度", "縦の基準角", "横の基準角", "ADS反動倍率", "回復量", "回復速度" }, items.ConvertAll(i => i.item));
            Assert.AreEqual("50%", items[5].a, "the earlier recoil: half recovers");
            Assert.AreEqual("0.55倍", items[4].a);
            Assert.AreEqual("9.0°/秒", items[6].a, "at the scene's speed");
            b = Recoil(RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Right, RecoilStrength.Strong);
            b.pitchDegrees = 0.7f;
            b.yawDegrees = 0.25f;
            b.adsMultiplier = 0.55f;
            b.recoveryFraction = 0.5f;
            b.recoverySpeed = 9f;
            Assert.AreEqual(2, RecoilParams.DiffCount(a, b, 9f), "direction and strength only");
            b.recoverySpeed = 4f;
            Assert.AreEqual(3, RecoilParams.DiffCount(a, b, 9f));
            Assert.AreEqual("上 0.70°", RecoilParams.RowText(a, RecoilRow.Vertical, 9f));
            Assert.AreEqual("右 0.25°", RecoilParams.RowText(b, RecoilRow.Horizontal, 9f));
            Assert.AreEqual("ランダム ±0.25°", RecoilParams.RowText(a, RecoilRow.Horizontal, 9f));
            // A direction of "なし" shows none, whatever angle is kept.
            b.SetDirections(RecoilVertical.Up, RecoilHorizontal.None);
            Assert.AreEqual("なし", RecoilParams.RowText(b, RecoilRow.Horizontal, 9f));
            Assert.AreEqual(0.25f, b.yawDegrees, "the angle is kept");
        }

        /// <summary>Default touch layout circles and the top buttons, in lab canvas units (941 high) from the safe area's top left.</summary>
        static List<Rect> DefaultHud(Vector2 area)
        {
            float k = 941f / 1080f;
            var list = new List<Rect>();
            foreach (var item in Deadlock.Input.TouchLayout.Defaults().items)
            {
                if (item.id == Deadlock.Input.TouchLayout.MoveZone)
                    continue;
                float d = Deadlock.Input.TouchLayout.Diameter(item) * k;
                var c = new Vector2(item.center.x * area.x, (1f - item.center.y) * area.y);
                list.Add(new Rect(c.x - d / 2f, c.y - d / 2f, d, d));
            }
            var menu = Deadlock.Input.TouchLayout.MenuButtonSize * k;
            list.Add(new Rect(area.x / 2f - menu.x / 2f, Deadlock.Input.TouchLayout.MenuButtonTopOffset * k, menu.x, menu.y));
            return list;
        }

        static float Covered(Rect r, IEnumerable<Rect> others)
        {
            float sum = 0f;
            foreach (var o in others)
            {
                float w = Mathf.Min(r.xMax, o.xMax) - Mathf.Max(r.xMin, o.xMin), h = Mathf.Min(r.yMax, o.yMax) - Mathf.Max(r.yMin, o.yMin);
                if (w > 0f && h > 0f)
                    sum += w * h;
            }
            return sum;
        }

        [TestCase(1755f, 892f, TestName = "QuickHud_Spot_iPhone16Pro_Landscape")]
        [TestCase(1254.7f, 941f, TestName = "QuickHud_Spot_iPad_4x3")]
        [TestCase(1672f, 941f, TestName = "QuickHud_Spot_16x9")]
        public void QuickHud_FirstSpot_InsideTheSafeArea_ClearOfCrosshairAndControls(float w, float h)
        {
            var area = new Vector2(w, h);
            var hud = DefaultHud(area);
            var c = area / 2f;
            float k = WeaponBalanceQuickPanel.CrosshairClearance;
            var crosshair = new Rect(c.x - k, c.y - k, 2f * k, 2f * k);
            void Check(string what, Vector2 pos, Vector2 size)
            {
                var r = new Rect(pos.x, -pos.y, size.x, size.y);
                TestContext.WriteLine($"{w}x{h} {what}: spot ({r.x}, {r.y}) size {size.x}x{size.y}, covers HUD {Covered(r, hud):0} units², crosshair {Covered(r, new[] { crosshair }):0}");
                Assert.GreaterOrEqual(r.xMin, 0f);
                Assert.GreaterOrEqual(r.yMin, 0f);
                Assert.LessOrEqual(r.xMax, area.x + 0.01f);
                Assert.LessOrEqual(r.yMax, area.y + 0.01f);
                Assert.IsFalse(r.Overlaps(crosshair), "the crosshair stays visible");
                Assert.AreEqual(0f, Covered(r, hud), "no HUD control covered");
            }
            var (cpos, cw) = WeaponBalanceQuickPanel.ChooseCompact(area, WeaponBalanceQuickPanel.PreferredSpot, hud, crosshair);
            Check("compact", cpos, new Vector2(cw, WeaponBalanceQuickPanel.CompactHeight));
            var (pos, height, width) = WeaponBalanceQuickPanel.ChooseExpanded(area, area.y, WeaponBalanceQuickPanel.MinExpandedHeight,
                WeaponBalanceQuickPanel.PreferredSpot, hud, crosshair);
            Check("expanded", pos, new Vector2(width, height));
            TestContext.WriteLine($"{w}x{h} recoil rows in view at the top: {WeaponBalanceQuickPanel.RecoilRowsInView(height)} of 6");
            Assert.GreaterOrEqual(height, WeaponBalanceQuickPanel.MinExpandedHeight, "at least one cell row of the body");
            // Earlier panel: 700 wide (the tallest clean height it could get on the same HUD).
            var (_, before) = WeaponBalanceQuickPanel.ChooseSpot(area, 700f, area.y, 344f, WeaponBalanceQuickPanel.PreferredSpot, hud, crosshair);
            TestContext.WriteLine($"{w}x{h} expanded height {height} (700 wide: {before})");
            // 2026-10-29: no wrapping comes first (never narrower than 800), so the panel may be shorter than the earlier
            // 700-wide one; it scrolls, and the first two recoil rows stay in view.
            Assert.GreaterOrEqual(width, WeaponBalanceQuickPanel.NarrowestWidth, "wide enough for one-line texts");
            Assert.GreaterOrEqual(WeaponBalanceQuickPanel.RecoilRowsInView(height), 2, "the first rows in view (the rest by scrolling)");
        }

        /// <summary>The user's HUD measured on an iPhone 16 Pro (safe-area units incl. the 6-unit margin).</summary>
        static List<Rect> MeasuredIPhoneHud() => new List<Rect>
        {
            new Rect(-5.22f, 61.19f, 136.60f, 136.60f), // weapon button (top left)
            new Rect(775.96f, 2.71f, 203.69f, 67.76f),  // menu button
            new Rect(62.66f, 517.27f, 255.96f, 255.96f), // move stick
            new Rect(1554.43f, 11.95f, 207.17f, 207.17f), // ADS
            new Rect(1231.96f, 496.75f, 133.98f, 133.98f), // reload
            new Rect(1456.84f, 526.09f, 304.76f, 304.76f), // fire
            new Rect(1050.01f, 607.31f, 147.92f, 147.92f), // posture
            new Rect(1217.27f, 751.66f, 146.18f, 146.18f), // jump
            new Rect(714.97f, 844.02f, 325.67f, 32.91f), // health bar
        };

        static readonly Rect MeasuredCrosshair = new Rect(877.8f - 45f, 445.9f - 45f, 90f, 90f);

        [Test]
        public void QuickHud_ExpandedSpot_FindsAGapNarrowerThanTheGrid()
        {
            // A 700-wide panel: the only columns clear of both the weapon button and the crosshair are x 131.4–132,
            // narrower than the 8-unit grid.
            var area = new Vector2(1755.6f, 891.9f);
            var hud = MeasuredIPhoneHud();
            var (pos, height) = WeaponBalanceQuickPanel.ChooseSpot(area, 700f, area.y, 344f, WeaponBalanceQuickPanel.PreferredSpot, hud, MeasuredCrosshair);
            var r = new Rect(pos.x, -pos.y, 700f, height);
            TestContext.WriteLine($"spot ({r.x}, {r.y}) height {height}");
            Assert.AreEqual(0f, Covered(r, hud), "no control covered");
            Assert.IsFalse(r.Overlaps(MeasuredCrosshair));
            Assert.GreaterOrEqual(height, 430f, "the room between the menu button and the move stick is used");
            Assert.Less(r.xMax, MeasuredCrosshair.xMin + 0.01f, "left of the crosshair");
        }

        [Test]
        public void QuickHud_MeasuredIPhoneHud_WidePanel_ShowsMostRecoilRowsAtOnce()
        {
            var area = new Vector2(1755.6f, 891.9f);
            var hud = MeasuredIPhoneHud();
            var (pos, height, width) = WeaponBalanceQuickPanel.ChooseExpanded(area, area.y, WeaponBalanceQuickPanel.MinExpandedHeight,
                WeaponBalanceQuickPanel.PreferredSpot, hud, MeasuredCrosshair);
            var r = new Rect(pos.x, -pos.y, width, height);
            int rows = WeaponBalanceQuickPanel.RecoilRowsInView(height);
            TestContext.WriteLine($"spot ({r.x}, {r.y}) size {width}x{height}, recoil rows in view at the top {rows} of 6");
            Assert.GreaterOrEqual(width, WeaponBalanceQuickPanel.NarrowestWidth, "one-line labels, roomy columns");
            Assert.Greater(width, 600f, "wider than before (600 on this HUD)");
            Assert.AreEqual(0f, Covered(r, hud), "no control covered");
            Assert.IsFalse(r.Overlaps(MeasuredCrosshair));
            Assert.GreaterOrEqual(rows, 2, "the earlier touch layout showed 1 at the top (no wrapping first: shorter than the 640-wide panel's 5)");
        }

        [Test]
        public void QuickHud_HudTexts_ShortenThePanelByAtMostOneRow()
        {
            var area = new Vector2(1755.6f, 891.9f);
            var hud = MeasuredIPhoneHud();
            var (_, free, _) = WeaponBalanceQuickPanel.ChooseExpanded(area, area.y, WeaponBalanceQuickPanel.MinExpandedHeight,
                WeaponBalanceQuickPanel.PreferredSpot, hud, MeasuredCrosshair);
            // A text right under the panel's spot (as the device's HP or status text could be).
            var texts = new List<Rect> { new Rect(300f, 430f, 300f, 40f) };
            var (pos, height, width) = WeaponBalanceQuickPanel.ChooseExpanded(area, area.y, WeaponBalanceQuickPanel.MinExpandedHeight,
                WeaponBalanceQuickPanel.PreferredSpot, hud, MeasuredCrosshair, texts);
            var r = new Rect(pos.x, -pos.y, width, height);
            TestContext.WriteLine($"spot ({r.x}, {r.y}) size {width}x{height} (no texts: {free})");
            Assert.AreEqual(0f, Covered(r, hud), "no control covered");
            Assert.IsFalse(r.Overlaps(MeasuredCrosshair));
            Assert.GreaterOrEqual(height, free - WeaponBalanceQuickPanel.TextYield, "texts cost at most one row");
        }

        [Test]
        public void QuickHud_RowsAreTheLabs_WiderThanBefore()
        {
            Assert.AreEqual(50f, WeaponBalanceQuickPanel.Row, "the full lab's row height (each slider's touch area)");
            Assert.Greater(WeaponBalanceQuickPanel.Font, 19, "a step larger than the full lab's font, for reading in play");
            Assert.Greater(WeaponBalanceQuickPanel.WideWidth, 680f, "wider than the earlier 680 / 600 / 540");
            Assert.LessOrEqual(WeaponBalanceQuickPanel.NarrowWidth, WeaponBalanceQuickPanel.WideWidth);
            Assert.Greater(WeaponBalanceQuickPanel.NarrowestWidth, 540f, "wider than the earlier narrowest panel");
            var style = WeaponBalanceQuickPanel.RowStyle;
            Assert.IsTrue(style.OneLineLabels && style.ThinSliders);
            Assert.GreaterOrEqual(style.LabelWidth, 4.4f * WeaponBalanceQuickPanel.Font, "room for 回復速度 on one line");
            Assert.Less(WeaponRecoilEditor.ThinTrack, WeaponBalanceQuickPanel.Row / 4f, "a thin track");
            Assert.AreEqual(6, WeaponBalanceQuickPanel.RecoilRowsInView(WeaponBalanceQuickPanel.FullHeight), "all rows without scrolling at full height");
        }
    }
}
