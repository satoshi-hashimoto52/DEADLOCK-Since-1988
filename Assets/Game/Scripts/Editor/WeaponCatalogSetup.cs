using System;
using Deadlock.Combat;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// The weapon definitions of Stage 31 (DECISIONS D-052 to D-054):
    /// <list type="bullet">
    /// <item>The rifle: category and the reserve rule (two / three magazines) are written; every other value (damage,
    /// fire rate, magazine, recoil, ADS, ...) is left as it is.</item>
    /// <item>The SMG: <see cref="SmgCandidateSetup.PreparePerformance"/> (confirmed values).</item>
    /// <item>Shotgun, handgun, LMG and the EPIC SMG: definitions with <b>provisional</b> values (not approved, Q-030),
    /// created only when missing so values tuned later are never reset. Not in any scene, menu or art registry yet
    /// (art and selection are Stages 32 / 33).</item>
    /// <item>Stage 33: the EPIC SMG's confirmed values (<see cref="EpicSmgSpec"/>) written again on every run (its
    /// provisional values stay as tuned), and the weapon catalog (<see cref="WeaponCatalog"/>, in Resources) rebuilt
    /// from <see cref="CatalogEntries"/>.</item>
    /// </list>
    /// Batch: -batchmode -quit -executeMethod Deadlock.Editor.WeaponCatalogSetup.PrepareFromBatch
    /// </summary>
    public static class WeaponCatalogSetup
    {
        public const string RiflePath = "Assets/Game/Data/Weapon_ProtoRifle.asset";
        public const string ShotgunPath = "Assets/Game/Data/Weapon_ProtoShotgun_Provisional.asset";
        public const string HandgunPath = "Assets/Game/Data/Weapon_ProtoHandgun_Provisional.asset";
        public const string LmgPath = "Assets/Game/Data/Weapon_ProtoLMG_Provisional.asset";
        public const string EpicSmgPath = "Assets/Game/Data/Weapon_ProtoEpicSMG_Provisional.asset";
        public const string KnifePath = "Assets/Game/Data/Melee_ProtoKnife.asset";
        public const string CatalogPath = "Assets/Game/Data/Resources/WeaponCatalog.asset";

        /// <summary>One provisional weapon: where it lives and how its first values are written (on top of a rifle copy).</summary>
        public readonly struct Provisional
        {
            public readonly string Path;
            public readonly Action<WeaponDefinition> Write;

            public Provisional(string path, Action<WeaponDefinition> write)
            {
                Path = path;
                Write = write;
            }
        }

        // Provisional values (not approved; Stage 30 final spec §1.2 marks them 仮値). Confirmed: shotgun 8 projectiles per
        // shot and a whole-magazine reload, handgun Slot 2 only, EPIC SMG 950 RPM and 28 rounds.
        public static readonly Provisional[] ProvisionalWeapons =
        {
            new Provisional(ShotgunPath, d =>
            {
                Identity(d, "proto_shotgun", "SHOTGUN (PROVISIONAL)", WeaponCategory.Shotgun, AmmoType.Shotgun);
                d.damage = 12f; // per projectile
                d.projectilesPerShot = 8;
                d.fireInterval = 0.9f;
                d.automatic = false;
                d.magazineSize = 6;
                d.reloadTime = 2.6f;
                d.maxRange = 40f;
                d.hipSpreadDegrees = 6f;
                d.adsSpreadDegrees = 4.5f;
            }),
            new Provisional(HandgunPath, d =>
            {
                Identity(d, "proto_handgun", "HANDGUN (PROVISIONAL)", WeaponCategory.Handgun, AmmoType.Pistol);
                d.damage = 30f;
                d.fireInterval = 60f / 400f; // semi-automatic, at most 400 RPM
                d.automatic = false;
                d.magazineSize = 12;
                d.reloadTime = 1.4f;
            }),
            new Provisional(LmgPath, d =>
            {
                Identity(d, "proto_lmg", "LMG (PROVISIONAL)", WeaponCategory.Lmg, AmmoType.Heavy);
                d.damage = 24f;
                d.fireInterval = 60f / 700f;
                d.automatic = true;
                d.magazineSize = 75;
                d.reloadTime = 4f;
                d.adsTime = 0.32f;
                d.adsMoveSpeedMultiplier = 0.45f;
            }),
            new Provisional(EpicSmgPath, d =>
            {
                Identity(d, EpicSmgSpec.WeaponId, "EPIC SMG (PROVISIONAL)", EpicSmgSpec.Category, AmmoType.Pistol);
                d.damage = EpicSmgSpec.ProvisionalDamage;
                d.reloadTime = EpicSmgSpec.ProvisionalReloadTime;
                ApplyEpicSmgConfirmed(d);
            }),
        };

        /// <summary>
        /// The EPIC SMG's confirmed values (D-053 / D-054): SMG, 950 RPM (automatic), 28 rounds, reserve 56 / 84.
        /// Written on every run of the setup so a rebuild never loses them; nothing else (damage, reload, recoil, spread,
        /// falloff, name: provisional) is touched.
        /// </summary>
        public static void ApplyEpicSmgConfirmed(WeaponDefinition d)
        {
            d.weaponId = EpicSmgSpec.WeaponId;
            d.category = EpicSmgSpec.Category;
            d.fireInterval = EpicSmgSpec.FireInterval;
            d.automatic = true;
            d.magazineSize = EpicSmgSpec.MagazineSize;
            AmmoRules.Apply(d);
        }

        /// <summary>One weapon of the catalog, as the setup writes it (values from the definition at <see cref="Path"/>).</summary>
        public readonly struct CatalogItem
        {
            public readonly string Path, WeaponId, DisplayName, ConfirmedNote, ProvisionalNote;
            public readonly CatalogCategory Category;
            public readonly WeaponRarity Rarity;
            public readonly bool Provisional;

            public CatalogItem(string path, string weaponId, string displayName, CatalogCategory category, WeaponRarity rarity,
                bool provisional, string confirmedNote, string provisionalNote)
            {
                Path = path;
                WeaponId = weaponId;
                DisplayName = displayName;
                Category = category;
                Rarity = rarity;
                Provisional = provisional;
                ConfirmedNote = confirmedNote;
                ProvisionalNote = provisionalNote;
            }
        }

        // Stage 33. Ids are the existing ones (never renamed). Rarity: only the EPIC SMG's is decided (the others
        // Unassigned). Names marked 仮称 are not approved (Q-030).
        public static readonly CatalogItem[] CatalogEntries =
        {
            new CatalogItem(RiflePath, "proto_rifle", "ライフル", CatalogCategory.Rifle, WeaponRarity.Unassigned, false,
                "ダメージ25・600RPM・30発（現行値を基準として維持）・Slot 1・予備 開始60／上限90・弾薬箱30", "正式名称・レアリティは未定"),
            new CatalogItem(SmgCandidateSetup.WeaponPath, "proto_smg", "SMG", CatalogCategory.Smg, WeaponRarity.Unassigned, true,
                "ダメージ17・900RPM・35発・Slot 1・予備 開始70／上限105・弾薬箱35", "反動・集弾・射程減衰などほかの値、正式名称・レアリティは未定"),
            new CatalogItem(ShotgunPath, "proto_shotgun", "ショットガン（仮称）", CatalogCategory.Shotgun, WeaponRarity.Unassigned, true,
                "1射8ペレット・フルマガジン一括リロード・Slot 1・予備は開始2／上限3マガジン分",
                "ダメージ12／ペレット・0.9秒／射・6発・リロード2.6秒などの数値（予備弾の数も連動して仮）、正式名称・レアリティは未定"),
            new CatalogItem(HandgunPath, "proto_handgun", "ハンドガン（仮称）", CatalogCategory.Handgun, WeaponRarity.Unassigned, true,
                "Slot 2 専用・予備は開始2／上限3マガジン分",
                "ダメージ30・半自動 最大400RPM・12発・リロード1.4秒などの数値（予備弾の数も連動して仮）、正式名称・レアリティは未定"),
            new CatalogItem(LmgPath, "proto_lmg", "LMG（仮称）", CatalogCategory.Lmg, WeaponRarity.Unassigned, true,
                "Slot 1・予備は開始2／上限3マガジン分",
                "ダメージ24・700RPM・75発・リロード4秒などの数値（予備弾の数も連動して仮）、正式名称・レアリティは未定"),
            new CatalogItem(EpicSmgPath, EpicSmgSpec.WeaponId, "EPIC SMG（仮称）", CatalogCategory.Smg, EpicSmgSpec.Rarity, true,
                "EPIC・SMG・Slot 1 のみ・950RPM・28発・予備 開始56／上限84・弾薬箱28・一括リロード・入手はガチャと交換ショップ（Stage 34 以降）",
                "ダメージ18・リロード1.9秒・反動・集弾・射程減衰、正式名称"),
            new CatalogItem(KnifePath, "proto_knife", "ナイフ", CatalogCategory.Knife, WeaponRarity.Unassigned, true,
                "Slot 3", "威力・射程などは試作用の暫定、レアリティは未定"),
        };

        /// <summary>
        /// Where one new gun's images go once they are approved (Stage 32; asset IDs and paths of
        /// docs/WEAPON_ASSET_REQUIREMENTS_STAGE30.md §3-1, settled by Stage 38: AssetPipeline's weapons_integration
        /// target registers them at exactly these paths, and <see cref="WeaponArtSetup"/> picks up hip / ADS). The three images
        /// are separate: the side view is picked up by <see cref="SkinSetup"/> when its file is there; hip and ADS go
        /// through a <see cref="WeaponArtDefinition"/> listed in the <see cref="WeaponArtRegistry"/> (with their placement
        /// files), as the rifle's and the SMG's did. Nothing here makes or registers an image: every file arrives through
        /// AssetPipeline (checks, the user's approval, registration) first. Until then the game shows stand-ins
        /// (<see cref="Deadlock.View.WeaponArtStatus"/>).
        /// </summary>
        public readonly struct PlannedArt
        {
            public readonly string DefinitionPath;
            public readonly string WeaponId;
            public readonly string Label;
            public readonly string SideAssetId, HipAssetId, AdsAssetId;
            public readonly string SidePath, HipPath, AdsPath;

            public PlannedArt(string definitionPath, string weaponId, string label, string artName, string folder)
            {
                DefinitionPath = definitionPath;
                WeaponId = weaponId;
                Label = label;
                SideAssetId = $"weapon.{artName}.side";
                HipAssetId = $"weapon.{artName}.hip";
                AdsAssetId = $"weapon.{artName}.ads";
                SidePath = $"Assets/Game/Art/Weapons/{folder}/Side/{artName}_side_design.png";
                HipPath = $"Assets/Game/Art/Weapons/{folder}/{artName}_hip.png";
                AdsPath = $"Assets/Game/Art/Weapons/{folder}/{artName}_ads.png";
            }
        }

        public static readonly PlannedArt[] PlannedArts =
        {
            new PlannedArt(ShotgunPath, "proto_shotgun", "ショットガン", "shotgun", "Shotgun"),
            new PlannedArt(HandgunPath, "proto_handgun", "ハンドガン", "handgun", "Handgun"),
            new PlannedArt(LmgPath, "proto_lmg", "LMG", "lmg", "LMG"),
            // Asset ids weapon.epic_smg.* (Stage 38, D-074; the Stage 30 proposal smg_x88 / SMG_X88 is replaced). The
            // weapon id stays proto_epic_smg (D-066); saves and the catalog keep only weapon ids, never art ids.
            new PlannedArt(EpicSmgPath, "proto_epic_smg", "EPIC SMG", "epic_smg", "EpicSMG"),
        };

        [MenuItem("DEADLOCK/Dev/Prepare Weapon Catalog (Stage 31-33)")]
        public static void PrepareFromMenu() => PrepareFromBatch();

        public static void PrepareFromBatch()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RiflePath);
            if (rifle == null)
                throw new InvalidOperationException($"[DEADLOCK] weapon catalog needs {RiflePath}");
            ApplyRifleRules(rifle);
            SmgCandidateSetup.PreparePerformance();
            PrepareProvisional(rifle);
            var epic = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(EpicSmgPath);
            ApplyEpicSmgConfirmed(epic);
            EditorUtility.SetDirty(epic);
            PrepareCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("[DEADLOCK] weapon catalog prepared (rifle reserve rule, SMG and EPIC SMG confirmed values, provisional weapons, catalog)");
        }

        /// <summary>Writes the catalog asset (created when missing) from <see cref="CatalogEntries"/>.</summary>
        public static WeaponCatalog PrepareCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetEntries(BuildEntries());
            var problems = catalog.Check();
            if (problems.Count > 0)
                throw new InvalidOperationException("[DEADLOCK] weapon catalog: " + string.Join("; ", problems));
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        /// <summary>The catalog's entries, read from the weapon assets (art ids: the approved SMG side view, the plans).</summary>
        public static System.Collections.Generic.List<WeaponCatalogEntry> BuildEntries()
        {
            var list = new System.Collections.Generic.List<WeaponCatalogEntry>();
            foreach (var item in CatalogEntries)
            {
                var e = new WeaponCatalogEntry
                {
                    weaponId = item.WeaponId,
                    displayName = item.DisplayName,
                    category = item.Category,
                    rarity = item.Rarity,
                    provisional = item.Provisional,
                    confirmedNote = item.ConfirmedNote,
                    provisionalNote = item.ProvisionalNote,
                    sideArt = "",
                    hipArt = "",
                    adsArt = "",
                };
                if (item.Category == CatalogCategory.Knife)
                    e.knife = AssetDatabase.LoadAssetAtPath<MeleeDefinition>(item.Path);
                else
                    e.gun = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(item.Path);
                if (e.gun == null && e.knife == null)
                    throw new InvalidOperationException($"[DEADLOCK] weapon catalog needs {item.Path}");
                if (item.WeaponId == "proto_smg")
                    e.sideArt = "weapon.smg.side";
                foreach (var plan in PlannedArts)
                    if (plan.WeaponId == item.WeaponId)
                    {
                        e.sideArt = plan.SideAssetId;
                        e.hipArt = plan.HipAssetId;
                        e.adsArt = plan.AdsAssetId;
                    }
                list.Add(e);
            }
            return list;
        }

        /// <summary>The rifle's category, ammo type and reserve rule. Nothing else of the rifle changes.</summary>
        public static void ApplyRifleRules(WeaponDefinition rifle)
        {
            rifle.category = WeaponCategory.Rifle;
            rifle.ammoType = AmmoType.Rifle;
            AmmoRules.Apply(rifle);
            EditorUtility.SetDirty(rifle);
        }

        /// <summary>Creates the provisional weapons that are missing (a copy of the rifle, then their own values).</summary>
        public static void PrepareProvisional(WeaponDefinition rifle)
        {
            foreach (var p in ProvisionalWeapons)
            {
                if (AssetDatabase.LoadAssetAtPath<WeaponDefinition>(p.Path) != null)
                    continue; // keep tuned values
                var d = ScriptableObject.CreateInstance<WeaponDefinition>();
                Build(rifle, p, d);
                AssetDatabase.CreateAsset(d, p.Path);
            }
        }

        /// <summary>Writes a provisional weapon's first values into <paramref name="target"/> (used by the setup and tests).</summary>
        public static void Build(WeaponDefinition rifle, Provisional p, WeaponDefinition target)
        {
            EditorUtility.CopySerialized(rifle, target);
            target.name = System.IO.Path.GetFileNameWithoutExtension(p.Path);
            target.viewProfile = null; // no art yet (Stage 32)
            target.recoilProfile = new RecoilProfile();
            target.damageFalloff = new DamageFalloffProfile();
            target.projectilesPerShot = 1;
            p.Write(target);
            AmmoRules.Apply(target);
        }

        static void Identity(WeaponDefinition d, string id, string name, WeaponCategory category, AmmoType ammoType)
        {
            d.weaponId = id;
            d.displayName = name;
            d.category = category;
            d.ammoType = ammoType;
        }
    }
}
