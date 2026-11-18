using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Rarity of an item (D-055: five grades). Only a label: there is no performance multiplier per rarity, every weapon
    /// differs by its own values. <see cref="Unassigned"/>: not decided yet for that weapon.
    /// </summary>
    public enum WeaponRarity
    {
        Unassigned,
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>Kind of weapon in the catalog: the gun categories (<see cref="WeaponCategory"/>) and the knife.</summary>
    public enum CatalogCategory
    {
        Rifle,
        Smg,
        Shotgun,
        Lmg,
        Handgun,
        Knife,
    }

    /// <summary>
    /// One weapon of the catalog (Stage 33). Its id is what the gacha, the owned items and the exchange shop (Stage 34
    /// and later) and the online sync (Stage 37) refer to; it never changes once given. The values themselves stay in
    /// the definition referred to (<see cref="gun"/> / <see cref="knife"/>).
    /// </summary>
    [Serializable]
    public sealed class WeaponCatalogEntry
    {
        [Tooltip("Stable id, the same as the definition's. Never renamed (saved data and other features refer to it).")]
        public string weaponId;
        [Tooltip("Name for screens. Undecided names are marked 仮称 (Q-030).")]
        public string displayName;
        public CatalogCategory category;
        [Tooltip("Unassigned: not decided for this weapon. Only a label (no performance multiplier).")]
        public WeaponRarity rarity;
        [Tooltip("Performance definition of a gun (null for the knife).")]
        public WeaponDefinition gun;
        [Tooltip("Definition of the knife (null for a gun).")]
        public MeleeDefinition knife;
        [Tooltip("Its name or some of its values are provisional, not approved (Q-030).")]
        public bool provisional;
        [Tooltip("Confirmed values (DECISIONS), for screens and checks.")]
        public string confirmedNote;
        [Tooltip("Provisional values (adjustable, not approved).")]
        public string provisionalNote;
        [Tooltip("Display art the weapon needs, by AssetPipeline asset id (empty: registered before asset ids were " +
                 "used). Whether each is approved is read from what is registered (WeaponArtStatus), never stored here.")]
        public string sideArt, hipArt, adsArt;

        public bool IsKnife => category == CatalogCategory.Knife;

        /// <summary>The only loadout slot the weapon may go in (D-052).</summary>
        public LoadoutSlot Slot => IsKnife ? LoadoutSlot.Knife : WeaponSlotRules.SlotFor(gun != null ? gun.category : WeaponCategory.Rifle);
    }

    /// <summary>
    /// Every weapon of the game with its id, name, category, slot, rarity, definition and art (Stage 33). Built by the
    /// editor setup (WeaponCatalogSetup) and kept in Resources so a scene built before it can still find it.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon Catalog", fileName = "WeaponCatalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public const string ResourceName = "WeaponCatalog";

        [SerializeField] List<WeaponCatalogEntry> entries = new List<WeaponCatalogEntry>();

        public IReadOnlyList<WeaponCatalogEntry> Entries => entries;

        public static WeaponCatalog Load() => Resources.Load<WeaponCatalog>(ResourceName);

        public WeaponCatalogEntry Find(string weaponId)
        {
            foreach (var e in entries)
                if (e != null && e.weaponId == weaponId)
                    return e;
            return null;
        }

        public WeaponCatalogEntry Find(WeaponDefinition gun) => gun == null ? null : Find(gun.weaponId);

        /// <summary>The guns that may go in a slot, in catalog order.</summary>
        public List<WeaponDefinition> GunsFor(LoadoutSlot slot)
        {
            var list = new List<WeaponDefinition>();
            foreach (var e in entries)
                if (e != null && e.gun != null && WeaponSlotRules.CanEquip(e.gun, slot))
                    list.Add(e.gun);
            return list;
        }

        public void SetEntries(IEnumerable<WeaponCatalogEntry> list) => entries = new List<WeaponCatalogEntry>(list);

        public static CatalogCategory CategoryOf(WeaponCategory category) => category switch
        {
            WeaponCategory.Smg => CatalogCategory.Smg,
            WeaponCategory.Shotgun => CatalogCategory.Shotgun,
            WeaponCategory.Lmg => CatalogCategory.Lmg,
            WeaponCategory.Handgun => CatalogCategory.Handgun,
            _ => CatalogCategory.Rifle,
        };

        /// <summary>Problems of the catalog (empty: none): ids unique and equal to the definitions', categories agreeing.</summary>
        public List<string> Check()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.weaponId))
                {
                    problems.Add("entry without an id");
                    continue;
                }
                if (!ids.Add(e.weaponId))
                    problems.Add($"{e.weaponId}: id used twice");
                if (e.IsKnife)
                {
                    if (e.knife == null || e.gun != null)
                        problems.Add($"{e.weaponId}: a knife needs a knife definition only");
                    else if (e.knife.weaponId != e.weaponId)
                        problems.Add($"{e.weaponId}: definition id {e.knife.weaponId}");
                    continue;
                }
                if (e.gun == null || e.knife != null)
                    problems.Add($"{e.weaponId}: a gun needs a gun definition only");
                else if (e.gun.weaponId != e.weaponId)
                    problems.Add($"{e.weaponId}: definition id {e.gun.weaponId}");
                else if (CategoryOf(e.gun.category) != e.category)
                    problems.Add($"{e.weaponId}: category {e.category}, definition {e.gun.category}");
            }
            return problems;
        }
    }

    /// <summary>
    /// The EPIC SMG (Stage 33, D-053 / D-054 / D-057). Confirmed: EPIC, an SMG for Slot 1 only, 950 RPM, 28 rounds,
    /// 56 in reserve at the start, 84 at most, 28 from an ammo box, the whole magazine reloaded at once, obtainable from
    /// the gacha and the exchange shop later. Provisional (Q-030, adjustable, not approved): damage 18, reload 1.9 s,
    /// recoil, spread, damage falloff, and the name. A weapon of its own: never the id of the normal SMG.
    /// </summary>
    public static class EpicSmgSpec
    {
        public const string WeaponId = "proto_epic_smg";

        // Confirmed.
        public const WeaponRarity Rarity = WeaponRarity.Epic;
        public const WeaponCategory Category = WeaponCategory.Smg;
        public const LoadoutSlot Slot = LoadoutSlot.Primary;
        public const float RoundsPerMinute = 950f;
        public const int MagazineSize = 28;
        public const int StartReserve = 56;
        public const int ReserveMax = 84;
        public const int AmmoBoxRounds = 28;
        public static float FireInterval => 60f / RoundsPerMinute;

        // Provisional starting values (not approved).
        public const float ProvisionalDamage = 18f;
        public const float ProvisionalReloadTime = 1.9f;

        /// <summary>The definition holds every confirmed value.</summary>
        public static bool HasConfirmedValues(WeaponDefinition d) =>
            d != null && d.weaponId == WeaponId && d.category == Category && d.automatic
            && Mathf.Abs(d.fireInterval - FireInterval) < 1e-5f && d.magazineSize == MagazineSize
            && d.startReserve == StartReserve && d.reserveMax == ReserveMax
            && AmmoRules.AmmoBoxRounds(d.magazineSize) == AmmoBoxRounds;
    }
}
