using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Editor;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 33 without a scene: the EPIC SMG as a weapon of its own (id, EPIC, 950 RPM, 28 rounds, 56 / 84 reserve, 28
    /// from an ammo box, Slot 1 only), its confirmed values surviving a rebuild while the provisional ones stay as
    /// tuned, and the weapon catalog (seven weapons, stable ids, categories, slots, rarity, definitions, art).
    /// </summary>
    public class WeaponCatalogTests
    {
        static WeaponDefinition Load(string path)
        {
            var d = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            Assert.IsNotNull(d, path);
            return d;
        }

        static WeaponDefinition Rifle => Load(WeaponCatalogSetup.RiflePath);
        static WeaponDefinition Smg => Load(SmgCandidateSetup.WeaponPath);
        static WeaponDefinition Handgun => Load(WeaponCatalogSetup.HandgunPath);
        static WeaponDefinition EpicSmg => Load(WeaponCatalogSetup.EpicSmgPath);
        static MeleeDefinition Knife => AssetDatabase.LoadAssetAtPath<MeleeDefinition>(WeaponCatalogSetup.KnifePath);

        static WeaponCatalog Catalog
        {
            get
            {
                var c = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(WeaponCatalogSetup.CatalogPath);
                Assert.IsNotNull(c, WeaponCatalogSetup.CatalogPath);
                return c;
            }
        }

        // ------------------------------------------------------------------ 1 to 7: the EPIC SMG

        [Test]
        public void EpicSmg_OwnId_Epic_950Rpm_28Rounds_Reserve56And84()
        {
            var epic = EpicSmg;
            Assert.AreEqual(EpicSmgSpec.WeaponId, epic.weaponId);
            Assert.AreNotEqual(Smg.weaponId, epic.weaponId, "never the normal SMG's id");
            Assert.AreNotSame(Smg, epic, "a definition of its own");
            Assert.AreEqual(WeaponRarity.Epic, Catalog.Find(epic).rarity);
            Assert.AreEqual(WeaponRarity.Unassigned, Catalog.Find(Smg).rarity, "the normal SMG's rarity is not decided");
            Assert.AreEqual(CatalogCategory.Smg, Catalog.Find(epic).category);
            Assert.AreEqual(WeaponCategory.Smg, epic.category);
            Assert.AreEqual(950f, 60f / epic.fireInterval, 0.01f, "950 RPM");
            Assert.IsTrue(epic.automatic);
            Assert.AreEqual(28, epic.magazineSize);
            Assert.AreEqual(56, epic.startReserve);
            Assert.AreEqual(84, epic.reserveMax);
            Assert.AreEqual(28, AmmoRules.AmmoBoxRounds(epic.magazineSize));
            Assert.IsTrue(EpicSmgSpec.HasConfirmedValues(epic));
            // Provisional starting values, kept apart from the confirmed ones.
            Assert.AreEqual(EpicSmgSpec.ProvisionalDamage, epic.damage);
            Assert.AreEqual(EpicSmgSpec.ProvisionalReloadTime, epic.reloadTime);
            Assert.AreEqual(1, epic.projectilesPerShot);
            StringAssert.Contains("PROVISIONAL", epic.displayName, "the name is not decided");
        }

        [Test]
        public void EpicSmg_AmmoBox_Adds28_CappedAt84()
        {
            var inv = new GunInventory();
            Assert.IsTrue(inv.TryEquip(LoadoutSlot.Primary, EpicSmg));
            Assert.AreEqual(28, inv.Ammo(LoadoutSlot.Primary).Loaded);
            Assert.AreEqual(56, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(28, inv.ApplyAmmoBox());
            Assert.AreEqual(84, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(0, inv.ApplyAmmoBox(), "at the maximum");
            inv.Ammo(LoadoutSlot.Primary).Set(28, 70);
            Assert.AreEqual(14, inv.ApplyAmmoBox(), "capped");
            Assert.AreEqual(84, inv.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(28, inv.Ammo(LoadoutSlot.Primary).Loaded, "the magazine is never filled by a box");
        }

        [Test]
        public void EpicSmg_Slot1Only_HandgunSlot2Only()
        {
            Assert.IsTrue(WeaponSlotRules.CanEquip(EpicSmg, LoadoutSlot.Primary));
            Assert.IsFalse(WeaponSlotRules.CanEquip(EpicSmg, LoadoutSlot.Secondary));
            Assert.IsFalse(WeaponSlotRules.CanEquip(EpicSmg, LoadoutSlot.Knife));
            Assert.IsTrue(WeaponSlotRules.CanEquip(Handgun, LoadoutSlot.Secondary));
            Assert.IsFalse(WeaponSlotRules.CanEquip(Handgun, LoadoutSlot.Primary));
            Assert.IsTrue(Loadout.TryCreate(EpicSmg, Handgun, Knife, out var ok, out _));
            Assert.AreSame(EpicSmg, ok.GunIn(LoadoutSlot.Primary));
            Assert.IsFalse(Loadout.TryCreate(Rifle, EpicSmg, Knife, out _, out string error), "EPIC SMG in Slot 2");
            StringAssert.Contains("Slot 2", error);
            Assert.IsFalse(Loadout.TryCreate(Handgun, null, Knife, out _, out _), "handgun in Slot 1");
            var inv = new GunInventory();
            Assert.IsFalse(inv.TryEquip(LoadoutSlot.Secondary, EpicSmg));
            Assert.IsFalse(inv.Has(LoadoutSlot.Secondary));
            Assert.AreEqual(LoadoutSlot.Primary, Catalog.Find(EpicSmg).Slot);
            Assert.AreEqual(LoadoutSlot.Secondary, Catalog.Find(Handgun).Slot);
        }

        // ------------------------------------------------------------------ 11: a rebuild keeps the confirmed values

        [Test]
        public void EpicSmgRebuild_WritesTheConfirmedValues_KeepsTunedProvisionalOnes()
        {
            var epic = Object.Instantiate(EpicSmg);
            try
            {
                // Broken confirmed values (as a stray edit or an old copy would leave them), tuned provisional ones.
                epic.fireInterval = 0.1f;
                epic.magazineSize = 30;
                epic.startReserve = 90;
                epic.reserveMax = 180;
                epic.category = WeaponCategory.Rifle;
                epic.automatic = false;
                epic.damage = 19.5f;
                epic.reloadTime = 2.1f;
                epic.hipSpreadDegrees = 2.2f;
                Assert.IsFalse(EpicSmgSpec.HasConfirmedValues(epic));
                WeaponCatalogSetup.ApplyEpicSmgConfirmed(epic);
                Assert.IsTrue(EpicSmgSpec.HasConfirmedValues(epic));
                Assert.AreEqual(950f, 60f / epic.fireInterval, 0.01f);
                Assert.AreEqual(28, epic.magazineSize);
                Assert.AreEqual(56, epic.startReserve);
                Assert.AreEqual(84, epic.reserveMax);
                Assert.AreEqual(19.5f, epic.damage, "provisional values stay as tuned");
                Assert.AreEqual(2.1f, epic.reloadTime);
                Assert.AreEqual(2.2f, epic.hipSpreadDegrees);
                WeaponCatalogSetup.ApplyEpicSmgConfirmed(epic);
                Assert.AreEqual(19.5f, epic.damage, "a second build changes nothing");

                // A definition built from scratch (the setup's first build) has them too.
                var p = WeaponCatalogSetup.ProvisionalWeapons.Single(w => w.Path == WeaponCatalogSetup.EpicSmgPath);
                var fresh = ScriptableObject.CreateInstance<WeaponDefinition>();
                try
                {
                    WeaponCatalogSetup.Build(Rifle, p, fresh);
                    Assert.IsTrue(EpicSmgSpec.HasConfirmedValues(fresh));
                    Assert.AreEqual(18f, fresh.damage);
                    Assert.AreEqual(1.9f, fresh.reloadTime);
                }
                finally
                {
                    Object.DestroyImmediate(fresh);
                }
            }
            finally
            {
                Object.DestroyImmediate(epic);
            }
        }

        [Test]
        public void CatalogRebuild_SameEntriesAsTheAsset_RarityAndIdsKept()
        {
            var built = WeaponCatalogSetup.BuildEntries();
            var asset = Catalog.Entries;
            Assert.AreEqual(built.Count, asset.Count);
            for (int i = 0; i < built.Count; i++)
            {
                Assert.AreEqual(built[i].weaponId, asset[i].weaponId);
                Assert.AreEqual(built[i].displayName, asset[i].displayName);
                Assert.AreEqual(built[i].category, asset[i].category);
                Assert.AreEqual(built[i].rarity, asset[i].rarity);
                Assert.AreSame(built[i].gun, asset[i].gun);
                Assert.AreSame(built[i].knife, asset[i].knife);
                Assert.AreEqual(built[i].confirmedNote, asset[i].confirmedNote);
                Assert.AreEqual(built[i].provisionalNote, asset[i].provisionalNote);
                Assert.AreEqual(built[i].sideArt, asset[i].sideArt);
            }
            Assert.AreEqual(WeaponRarity.Epic, built.Single(e => e.weaponId == EpicSmgSpec.WeaponId).rarity);
        }

        // ------------------------------------------------------------------ catalog

        [Test]
        public void Catalog_SevenWeapons_IdsCategoriesSlotsDefinitions()
        {
            var c = Catalog;
            Assert.AreSame(c, WeaponCatalog.Load(), "found through Resources (scenes built before it)");
            CollectionAssert.IsEmpty(c.Check());
            var expected = new (string id, CatalogCategory cat, LoadoutSlot slot)[]
            {
                ("proto_rifle", CatalogCategory.Rifle, LoadoutSlot.Primary),
                ("proto_smg", CatalogCategory.Smg, LoadoutSlot.Primary),
                ("proto_shotgun", CatalogCategory.Shotgun, LoadoutSlot.Primary),
                ("proto_handgun", CatalogCategory.Handgun, LoadoutSlot.Secondary),
                ("proto_lmg", CatalogCategory.Lmg, LoadoutSlot.Primary),
                (EpicSmgSpec.WeaponId, CatalogCategory.Smg, LoadoutSlot.Primary),
                ("proto_knife", CatalogCategory.Knife, LoadoutSlot.Knife),
            };
            Assert.AreEqual(expected.Length, c.Entries.Count);
            foreach (var (id, cat, slot) in expected)
            {
                var e = c.Find(id);
                Assert.IsNotNull(e, id);
                Assert.AreEqual(cat, e.category, id);
                Assert.AreEqual(slot, e.Slot, id);
                Assert.IsFalse(string.IsNullOrEmpty(e.displayName), id);
                if (e.IsKnife)
                {
                    Assert.AreSame(Knife, e.knife);
                    Assert.IsNull(e.gun);
                }
                else
                {
                    Assert.AreEqual(id, e.gun.weaponId, "the definition's own id");
                    Assert.IsNull(e.knife);
                }
            }
            Assert.AreEqual(c.Entries.Count, c.Entries.Select(e => e.weaponId).Distinct().Count(), "ids unique");
            // Existing ids unchanged.
            Assert.AreEqual("proto_rifle", Rifle.weaponId);
            Assert.AreEqual("proto_smg", Smg.weaponId);
            CollectionAssert.AreEqual(new[] { "proto_rifle", "proto_smg", "proto_shotgun", "proto_lmg", EpicSmgSpec.WeaponId },
                c.GunsFor(LoadoutSlot.Primary).Select(g => g.weaponId).ToArray());
            CollectionAssert.AreEqual(new[] { "proto_handgun" }, c.GunsFor(LoadoutSlot.Secondary).Select(g => g.weaponId).ToArray());
            CollectionAssert.IsEmpty(c.GunsFor(LoadoutSlot.Knife));
            Assert.IsNull(c.Find("proto_smg_x88"), "the unapproved id proposal is not used");
        }

        [Test]
        public void Catalog_Check_FindsDuplicateIdsAndWrongDefinitions()
        {
            var c = ScriptableObject.CreateInstance<WeaponCatalog>();
            try
            {
                c.SetEntries(new[]
                {
                    new WeaponCatalogEntry { weaponId = "proto_smg", category = CatalogCategory.Smg, gun = Smg },
                    new WeaponCatalogEntry { weaponId = "proto_smg", category = CatalogCategory.Smg, gun = EpicSmg },
                    new WeaponCatalogEntry { weaponId = "proto_handgun", category = CatalogCategory.Smg, gun = Handgun },
                    new WeaponCatalogEntry { weaponId = "proto_knife", category = CatalogCategory.Knife, gun = Rifle },
                });
                var problems = c.Check();
                Assert.IsTrue(problems.Any(p => p.Contains("used twice")));
                Assert.IsTrue(problems.Any(p => p.Contains("definition id")));
                Assert.IsTrue(problems.Any(p => p.Contains("category")));
                Assert.IsTrue(problems.Any(p => p.Contains("knife")));
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void Catalog_ConfirmedAndProvisionalToldApart_NoRarityMultiplier()
        {
            var epic = Catalog.Find(EpicSmgSpec.WeaponId);
            foreach (var word in new[] { "EPIC", "950RPM", "28発", "56", "84", "弾薬箱28", "一括リロード", "Slot 1" })
                StringAssert.Contains(word, epic.confirmedNote);
            foreach (var word in new[] { "ダメージ18", "リロード1.9秒", "正式名称" })
                StringAssert.Contains(word, epic.provisionalNote);
            StringAssert.DoesNotContain("ダメージ", epic.confirmedNote, "damage is provisional");
            StringAssert.Contains("仮称", epic.displayName);
            Assert.IsTrue(epic.provisional);
            Assert.IsFalse(Catalog.Find("proto_rifle").provisional);
            // Rarity is only a label of the catalog: nothing on the performance definition scales with it.
            Assert.IsNull(typeof(WeaponDefinition).GetField("rarity"), "no rarity on the performance definition");
        }

        [Test]
        public void Catalog_Art_ApprovalReadFromWhatIsRegistered()
        {
            var skins = AssetDatabase.LoadAssetAtPath<SkinCatalog>(SkinSetup.CatalogPath);
            foreach (var e in Catalog.Entries)
            {
                foreach (WeaponArtView view in System.Enum.GetValues(typeof(WeaponArtView)))
                {
                    var state = WeaponArtStatus.Of(e, skins, view);
                    if (!e.IsKnife)
                        Assert.AreEqual(WeaponArtStatus.Of(e.gun, skins, view), state, $"{e.weaponId} {view}");
                    if (e.weaponId == "proto_rifle" || e.weaponId == "proto_smg")
                        Assert.AreEqual(WeaponArtState.Approved, state, $"{e.weaponId} {view}");
                }
                if (e.IsKnife)
                    Assert.AreEqual(WeaponArtState.Approved, WeaponArtStatus.Of(e, skins, WeaponArtView.Hip), "the knife's first-person image");
            }
            foreach (var plan in WeaponCatalogSetup.PlannedArts)
            {
                var e = Catalog.Find(plan.WeaponId);
                Assert.AreEqual(plan.SideAssetId, e.sideArt);
                Assert.AreEqual(plan.HipAssetId, e.hipArt);
                Assert.AreEqual(plan.AdsAssetId, e.adsArt);
                // Stage 38: approved exactly when AssetPipeline registered that image at the planned path (a view not
                // delivered stays a stand-in, never another gun's image).
                var registered = new Dictionary<WeaponArtView, string>
                {
                    [WeaponArtView.Side] = plan.SidePath, [WeaponArtView.Hip] = plan.HipPath, [WeaponArtView.Ads] = plan.AdsPath,
                };
                foreach (var kv in registered)
                    Assert.AreEqual(System.IO.File.Exists(kv.Value) ? WeaponArtState.Approved : WeaponArtState.Placeholder,
                        WeaponArtStatus.Of(e, skins, kv.Key), $"{e.weaponId} {kv.Key} ({kv.Value})");
            }
            // The EPIC SMG's art ids (Stage 38, D-074): weapon.epic_smg.*; its weapon id is unchanged.
            var epicArt = Catalog.Find(EpicSmgSpec.WeaponId);
            Assert.AreEqual("proto_epic_smg", EpicSmgSpec.WeaponId);
            Assert.AreEqual("weapon.epic_smg.side", epicArt.sideArt);
            Assert.AreEqual("weapon.epic_smg.hip", epicArt.hipArt);
            Assert.AreEqual("weapon.epic_smg.ads", epicArt.adsArt);
            foreach (var e in Catalog.Entries)
                foreach (var id in new[] { e.sideArt, e.hipArt, e.adsArt })
                    StringAssert.DoesNotContain("smg_x88", id ?? "", $"{e.weaponId}: the replaced proposal is not used");
            foreach (var plan in WeaponCatalogSetup.PlannedArts)
                StringAssert.DoesNotContain("SMG_X88", plan.SidePath + plan.HipPath + plan.AdsPath);
        }
    }
}
