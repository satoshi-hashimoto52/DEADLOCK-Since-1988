using System.IO;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 31 weapon foundation (DECISIONS D-052 to D-054): the three loadout slots and which guns may go where, every
    /// gun's own ammunition (two magazines at the start, three at most, one per gun from an ammo box), the rifle's values
    /// kept, the SMG's confirmed values surviving a rebuild, the provisional weapons, and the weapon lab's saved data still
    /// readable.
    /// </summary>
    public class WeaponLoadoutTests
    {
        const string KnifePath = "Assets/Game/Data/Melee_ProtoKnife.asset";

        static WeaponDefinition Load(string path)
        {
            var d = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            Assert.IsNotNull(d, path);
            return d;
        }

        static WeaponDefinition Rifle => Load(WeaponCatalogSetup.RiflePath);
        static WeaponDefinition Smg => Load(SmgCandidateSetup.WeaponPath);
        static WeaponDefinition Shotgun => Load(WeaponCatalogSetup.ShotgunPath);
        static WeaponDefinition Handgun => Load(WeaponCatalogSetup.HandgunPath);
        static WeaponDefinition Lmg => Load(WeaponCatalogSetup.LmgPath);
        static WeaponDefinition EpicSmg => Load(WeaponCatalogSetup.EpicSmgPath);
        static MeleeDefinition Knife => AssetDatabase.LoadAssetAtPath<MeleeDefinition>(KnifePath);

        // ------------------------------------------------------------------ 1, 2: slots

        [Test]
        public void Loadout_ThreeSlots_MainGunHandgunKnife()
        {
            foreach (var main in new[] { Rifle, Smg, Shotgun, Lmg, EpicSmg })
            {
                Assert.IsTrue(Loadout.TryCreate(main, Handgun, Knife, out var loadout, out string error), main.weaponId + ": " + error);
                Assert.AreSame(main, loadout.GunIn(LoadoutSlot.Primary));
                Assert.AreSame(Handgun, loadout.GunIn(LoadoutSlot.Secondary));
                Assert.AreSame(Knife, loadout.Knife);
                Assert.IsTrue(Loadout.TryCreate(main, null, Knife, out _, out _), "Slot 2 may stay empty");
            }
        }

        [Test]
        public void Loadout_Slot2_RefusesEveryGunButTheHandgun()
        {
            foreach (var gun in new[] { Rifle, Smg, Shotgun, Lmg, EpicSmg })
            {
                Assert.IsFalse(WeaponSlotRules.CanEquip(gun, LoadoutSlot.Secondary), gun.weaponId);
                Assert.IsFalse(Loadout.TryCreate(Rifle, gun, Knife, out var loadout, out string error), gun.weaponId);
                Assert.IsNull(loadout);
                StringAssert.Contains("Slot 2", error);
            }
            Assert.IsTrue(WeaponSlotRules.CanEquip(Handgun, LoadoutSlot.Secondary));
        }

        [Test]
        public void Loadout_Slot1_RefusesTheHandgun_GunsNeverInTheKnifeSlot_RequiredSlots()
        {
            Assert.IsFalse(WeaponSlotRules.CanEquip(Handgun, LoadoutSlot.Primary));
            Assert.IsFalse(Loadout.TryCreate(Handgun, null, Knife, out _, out string error));
            StringAssert.Contains("Slot 1", error);
            foreach (var gun in new[] { Rifle, Handgun })
                Assert.IsFalse(WeaponSlotRules.CanEquip(gun, LoadoutSlot.Knife));
            Assert.IsFalse(Loadout.TryCreate(null, Handgun, Knife, out _, out _), "a main gun is required");
            Assert.IsFalse(Loadout.TryCreate(Rifle, Handgun, null, out _, out _), "a knife is required");
            Assert.IsFalse(WeaponSlotRules.CanEquip(null, LoadoutSlot.Primary));
        }

        [Test]
        public void Inventory_RefusesAGunInTheWrongSlot()
        {
            var inv = new GunInventory();
            Assert.IsFalse(inv.TryEquip(LoadoutSlot.Secondary, Rifle));
            Assert.IsFalse(inv.TryEquip(LoadoutSlot.Primary, Handgun));
            Assert.AreEqual(0, inv.Count);
            Assert.IsTrue(inv.TryEquip(LoadoutSlot.Primary, Rifle));
            Assert.IsTrue(inv.TryEquip(LoadoutSlot.Secondary, Handgun));
            Assert.AreEqual(2, inv.Count);
        }

        [Test]
        public void Categories_DecideTheSlots()
        {
            Assert.AreEqual(WeaponCategory.Rifle, Rifle.category);
            Assert.AreEqual(WeaponCategory.Smg, Smg.category);
            Assert.AreEqual(WeaponCategory.Shotgun, Shotgun.category);
            Assert.AreEqual(WeaponCategory.Handgun, Handgun.category);
            Assert.AreEqual(WeaponCategory.Lmg, Lmg.category);
            Assert.AreEqual(WeaponCategory.Smg, EpicSmg.category);
            Assert.AreEqual(LoadoutSlot.Secondary, WeaponSlotRules.SlotFor(WeaponCategory.Handgun));
            foreach (var c in new[] { WeaponCategory.Rifle, WeaponCategory.Smg, WeaponCategory.Shotgun, WeaponCategory.Lmg })
                Assert.AreEqual(LoadoutSlot.Primary, WeaponSlotRules.SlotFor(c));
        }

        // ------------------------------------------------------------------ 3, 4: values

        [Test]
        public void Rifle_KeepsItsValues_OnlyTheReserveFollowsTheRule()
        {
            var r = Rifle;
            Assert.AreEqual("proto_rifle", r.weaponId);
            Assert.AreEqual(25f, r.damage);
            Assert.AreEqual(0.1f, r.fireInterval, 1e-6f, "600 RPM");
            Assert.IsTrue(r.automatic);
            Assert.AreEqual(1, r.projectilesPerShot);
            Assert.AreEqual(30, r.magazineSize);
            Assert.AreEqual(1.8f, r.reloadTime, 1e-6f);
            Assert.AreEqual(250f, r.projectileSpeed);
            Assert.AreEqual(200f, r.maxRange);
            Assert.AreEqual(1.6f, r.hipSpreadDegrees, 1e-6f);
            Assert.AreEqual(0.15f, r.adsSpreadDegrees, 1e-6f);
            Assert.AreEqual(0.7f, r.recoilPitchDegrees, 1e-6f);
            Assert.AreEqual(0.25f, r.recoilYawDegrees, 1e-6f);
            Assert.AreEqual(0.55f, r.adsRecoilMultiplier, 1e-6f);
            Assert.AreEqual(75f, r.hipFov);
            Assert.AreEqual(55f, r.adsFov);
            Assert.AreEqual(0.18f, r.adsTime, 1e-6f);
            Assert.AreEqual(0.6f, r.adsMoveSpeedMultiplier, 1e-6f);
            Assert.AreEqual(0.65f, r.adsLookSensitivityMultiplier, 1e-6f);
            Assert.AreEqual(1f, r.moveSpeedMultiplier, "no movement change");
            Assert.AreEqual(1f, r.movingSpreadMultiplier, "no moving spread");
            Assert.AreEqual(0f, r.switchTime, "the switcher's own time");
            Assert.AreEqual(60, r.startReserve, "D-054: was 90");
            Assert.AreEqual(90, r.reserveMax, "D-054: was 180");
        }

        [Test]
        public void Smg_ConfirmedValues()
        {
            var s = Smg;
            Assert.AreEqual("proto_smg", s.weaponId);
            Assert.AreEqual(17f, s.damage);
            Assert.AreEqual(900f, 60f / s.fireInterval, 0.01f);
            Assert.IsTrue(s.automatic);
            Assert.AreEqual(1, s.projectilesPerShot);
            Assert.AreEqual(35, s.magazineSize);
            Assert.AreEqual(70, s.startReserve);
            Assert.AreEqual(105, s.reserveMax);
        }

        [Test]
        public void ProvisionalWeapons_ConfirmedPartsAndTheReserveRule()
        {
            Assert.AreEqual(8, Shotgun.projectilesPerShot, "D-053: 8 projectiles per shot");
            Assert.AreEqual(950f, 60f / EpicSmg.fireInterval, 0.01f, "D-053: 950 RPM");
            Assert.AreEqual(28, EpicSmg.magazineSize, "D-053: 28 rounds");
            Assert.IsFalse(Handgun.automatic, "semi-automatic");
            foreach (var d in new[] { Shotgun, Handgun, Lmg, EpicSmg })
            {
                Assert.IsTrue(AmmoRules.Follows(d), d.weaponId);
                // Stage 38: first-person art only from registered images (none: no profile at all).
                var plan = System.Array.Find(Deadlock.Editor.WeaponCatalogSetup.PlannedArts, p => p.WeaponId == d.weaponId);
                Assert.AreEqual(System.IO.File.Exists(plan.HipPath) || System.IO.File.Exists(plan.AdsPath), d.viewProfile != null, d.weaponId + ": art only when registered");
                StringAssert.Contains("PROVISIONAL", d.displayName, "named as provisional");
            }
            foreach (var d in new[] { Rifle, Smg })
                Assert.IsTrue(AmmoRules.Follows(d), d.weaponId);
        }

        // ------------------------------------------------------------------ 5 to 9: ammunition

        [Test]
        public void Inventory_EveryGunKeepsItsOwnRounds()
        {
            var inv = new GunInventory();
            inv.TryEquip(LoadoutSlot.Primary, Rifle);
            inv.TryEquip(LoadoutSlot.Secondary, Handgun);
            var rifle = inv.Ammo(LoadoutSlot.Primary);
            var handgun = inv.Ammo(LoadoutSlot.Secondary);
            Assert.AreNotSame(rifle, handgun);
            for (int i = 0; i < 5; i++)
                rifle.TryConsume();
            rifle.Reload();
            Assert.AreEqual(30, rifle.Loaded);
            Assert.AreEqual(55, rifle.Reserve);
            Assert.AreEqual(12, handgun.Loaded, "the handgun is untouched");
            Assert.AreEqual(24, handgun.Reserve);
            inv.ResetAll();
            Assert.AreEqual(60, inv.Ammo(LoadoutSlot.Primary).Reserve, "a reset gives every gun its starting reserve");
        }

        [Test]
        public void Ammo_StartsWithTwoMagazines_HoldsThree()
        {
            foreach (var (gun, start, max) in new[] { (Rifle, 60, 90), (Smg, 70, 105), (EpicSmg, 56, 84) })
            {
                var ammo = GunInventory.Fresh(gun);
                Assert.AreEqual(gun.magazineSize, ammo.Loaded, gun.weaponId + ": full magazine");
                Assert.AreEqual(start, ammo.Reserve, gun.weaponId);
                Assert.AreEqual(AmmoRules.StartReserve(gun.magazineSize), ammo.Reserve);
                Assert.AreEqual(max, ammo.ReserveMax, gun.weaponId);
                Assert.AreEqual(max - start, ammo.AddReserve(1000), "capped at three magazines");
                Assert.AreEqual(max, ammo.Reserve);
            }
        }

        [Test]
        public void AmmoBox_OneMagazineToEveryGun_IntoReserveOnly()
        {
            var inv = new GunInventory();
            inv.TryEquip(LoadoutSlot.Primary, Rifle);
            inv.TryEquip(LoadoutSlot.Secondary, Handgun);
            inv.Ammo(LoadoutSlot.Primary).Set(10, 20);
            inv.Ammo(LoadoutSlot.Secondary).Set(3, 0);
            Assert.AreEqual(30 + 12, inv.ApplyAmmoBox());
            Assert.AreEqual(50, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(12, inv.Ammo(LoadoutSlot.Secondary).Reserve);
            Assert.AreEqual(10, inv.Ammo(LoadoutSlot.Primary).Loaded, "the magazine is not filled");
            Assert.AreEqual(3, inv.Ammo(LoadoutSlot.Secondary).Loaded);
        }

        [Test]
        public void AmmoBox_CappedPerGun_NothingWhenEveryReserveIsFull()
        {
            var inv = new GunInventory();
            inv.TryEquip(LoadoutSlot.Primary, Rifle);
            inv.TryEquip(LoadoutSlot.Secondary, Handgun);
            inv.Ammo(LoadoutSlot.Primary).Set(30, 80);
            Assert.AreEqual(10 + 12, inv.ApplyAmmoBox(), "rifle up to its cap (90), handgun one magazine");
            Assert.AreEqual(90, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(36, inv.Ammo(LoadoutSlot.Secondary).Reserve);
            Assert.AreEqual(0, inv.ApplyAmmoBox(), "both full: nothing added (the box stays)");
            Assert.AreEqual(90, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(36, inv.Ammo(LoadoutSlot.Secondary).Reserve);
        }

        // ------------------------------------------------------------------ 12: rebuilding keeps the values

        [Test]
        public void SmgRebuild_WritesTheConfirmedValues_KeepsTunedOnes_NeverTheRifles()
        {
            var smg = Object.Instantiate(Smg);
            try
            {
                // As the old setup left it (a rifle copy), with a tuned value that is not confirmed.
                EditorUtility.CopySerialized(Rifle, smg);
                smg.hipSpreadDegrees = 2.4f;
                smg.reloadTime = 1.6f;
                SmgCandidateSetup.ApplyConfirmedValues(smg);
                Assert.AreEqual(17f, smg.damage);
                Assert.AreEqual(900f, 60f / smg.fireInterval, 0.01f);
                Assert.AreEqual(35, smg.magazineSize);
                Assert.AreEqual(70, smg.startReserve);
                Assert.AreEqual(105, smg.reserveMax);
                Assert.AreEqual("proto_smg", smg.weaponId);
                Assert.AreEqual(2.4f, smg.hipSpreadDegrees, "values not confirmed stay as tuned");
                Assert.AreEqual(1.6f, smg.reloadTime);
                SmgCandidateSetup.ApplyConfirmedValues(smg);
                Assert.AreEqual(17f, smg.damage, "a second build changes nothing");
                Assert.AreEqual(2.4f, smg.hipSpreadDegrees);
                Assert.AreEqual(25f, Rifle.damage, "the rifle is only read");
            }
            finally
            {
                Object.DestroyImmediate(smg);
            }
        }

        [Test]
        public void RifleRules_TouchOnlyTheReserveAndCategory()
        {
            var rifle = Object.Instantiate(Rifle);
            try
            {
                rifle.startReserve = 90;
                rifle.reserveMax = 180;
                rifle.damage = 26f; // a hand-tuned value is left alone
                WeaponCatalogSetup.ApplyRifleRules(rifle);
                Assert.AreEqual(60, rifle.startReserve);
                Assert.AreEqual(90, rifle.reserveMax);
                Assert.AreEqual(26f, rifle.damage);
                Assert.AreEqual(WeaponCategory.Rifle, rifle.category);
            }
            finally
            {
                Object.DestroyImmediate(rifle);
            }
        }

        [Test]
        public void ProvisionalBuild_FromTheRifle_OwnValues()
        {
            var shotgun = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                WeaponCatalogSetup.Build(Rifle, WeaponCatalogSetup.ProvisionalWeapons[0], shotgun);
                Assert.AreEqual("proto_shotgun", shotgun.weaponId);
                Assert.AreEqual(8, shotgun.projectilesPerShot);
                Assert.AreEqual(6, shotgun.magazineSize);
                Assert.AreEqual(12, shotgun.startReserve);
                Assert.AreEqual(18, shotgun.reserveMax);
                Assert.IsNull(shotgun.viewProfile);
                Assert.AreEqual(1, Rifle.projectilesPerShot, "the rifle is only read");
            }
            finally
            {
                Object.DestroyImmediate(shotgun);
            }
        }

        // ------------------------------------------------------------------ 13: the weapon lab's saved data

        [Test]
        public void WeaponLabSavedData_FromBeforeStage31_StillLoadsAndApplies()
        {
            Assert.AreEqual(1, WeaponBalanceProfile.CurrentVersion, "the saved data's version is unchanged");
            string dir = Path.Combine(Path.GetTempPath(), "DeadlockStage31Lab_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new WeaponBalanceStore(dir);
                // A file as the lab saved it before Stage 31 (old reserve values, no new fields).
                var old = WeaponBalanceProfile.FromDefinition(Rifle);
                old.startReserve = 90;
                old.reserveMax = 180;
                old.damage = 27f;
                store.Save(old, "42374ab");
                Assert.IsTrue(store.TryLoad("proto_rifle", out var loaded, out string error), error);
                Assert.AreEqual(90, loaded.startReserve);
                Assert.AreEqual(27f, loaded.damage);
                Assert.IsTrue(loaded.CanApply);

                var clone = Object.Instantiate(Rifle); // the lab applies to a runtime clone, never the asset
                try
                {
                    loaded.ApplyTo(clone);
                    Assert.AreEqual(27f, clone.damage);
                    Assert.AreEqual(180, clone.reserveMax);
                    Assert.AreEqual(WeaponCategory.Rifle, clone.category, "new fields keep the definition's values");
                    Assert.AreEqual(1, clone.projectilesPerShot);
                    Assert.AreEqual(60, Rifle.startReserve, "the asset is untouched");
                }
                finally
                {
                    Object.DestroyImmediate(clone);
                }

                // A profile of the current SMG round-trips with its confirmed values.
                var smg = WeaponBalanceProfile.FromDefinition(Smg);
                store.Save(smg, "stage31");
                Assert.IsTrue(store.TryLoad("proto_smg", out var smgLoaded, out _));
                Assert.AreEqual(17f, smgLoaded.damage);
                Assert.AreEqual(900f, smgLoaded.rpm, 0.01f);
                Assert.AreEqual(35, smgLoaded.magazineSize);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}
