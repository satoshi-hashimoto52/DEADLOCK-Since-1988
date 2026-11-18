using System;
using System.Collections.Generic;
using Deadlock.Combat;

namespace Deadlock.Gacha
{
    /// <summary>The three kinds of item (D-008): weapons, skins, skills.</summary>
    public enum ItemCategory
    {
        Weapon,
        Skin,
        Skill,
    }

    /// <summary>
    /// One item that can be owned (Stage 34). Its id is stable: the owned items, the history, the exchange shop and later
    /// the server refer to it. <see cref="refId"/> points into the definition of the item (a weapon: its id in the
    /// <see cref="WeaponCatalog"/>). Rarity uses <see cref="WeaponRarity"/> (Unassigned: not decided, never offered).
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        public string itemId;
        public ItemCategory category;
        public WeaponRarity rarity;
        public string refId;
        public string displayName;
        /// <summary>Can come out of the gacha.</summary>
        public bool gacha = true;
        /// <summary>Can be bought in the exchange shop (false: "gacha only", D-057).</summary>
        public bool exchangeable = true;
        /// <summary>A development test item: never an official item, only in a development build's test pool.</summary>
        public bool devOnly;

        /// <summary>LEGENDARY items appear only during an event that names them (D-056).</summary>
        public bool EventLimited => rarity == WeaponRarity.Legendary;

        public static string WeaponItemId(string weaponId) => "weapon:" + weaponId;
        public static string SkinItemId(string weaponId, string skinId) => $"skin:{weaponId}/{skinId}";
        public static string SkillItemId(string skillId) => "skill:" + skillId;
    }

    /// <summary>
    /// Every item that exists (official ones, plus the development test items in a development build). Weapons come
    /// from the weapon catalog: only those with a decided rarity are items (today the EPIC SMG). There are no skin or
    /// skill items yet; their ids and fields are ready (<see cref="ItemDefinition.SkinItemId"/>, ...).
    /// </summary>
    public sealed class ItemCatalog
    {
        readonly List<ItemDefinition> items = new List<ItemDefinition>();
        readonly Dictionary<string, ItemDefinition> byId = new Dictionary<string, ItemDefinition>();

        public IReadOnlyList<ItemDefinition> Items => items;

        public ItemCatalog(IEnumerable<ItemDefinition> list)
        {
            foreach (var item in list)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId))
                    throw new ArgumentException("item without an id");
                if (byId.ContainsKey(item.itemId))
                    throw new ArgumentException($"item id used twice: {item.itemId}");
                byId[item.itemId] = item;
                items.Add(item);
            }
        }

        public ItemDefinition Find(string itemId) => itemId != null && byId.TryGetValue(itemId, out var i) ? i : null;

        /// <summary>The official items: the weapons of the catalog whose rarity is decided.</summary>
        public static List<ItemDefinition> OfficialItems(WeaponCatalog weapons)
        {
            var list = new List<ItemDefinition>();
            if (weapons == null)
                return list;
            foreach (var e in weapons.Entries)
            {
                if (e == null || e.rarity == WeaponRarity.Unassigned)
                    continue;
                list.Add(new ItemDefinition
                {
                    itemId = ItemDefinition.WeaponItemId(e.weaponId),
                    category = ItemCategory.Weapon,
                    rarity = e.rarity,
                    refId = e.weaponId,
                    displayName = e.displayName,
                    gacha = true,
                    exchangeable = true, // the EPIC SMG: gacha and shop (D-057)
                });
            }
            return list;
        }

        public const string DevItemPrefix = "devtest:";
        public const string DevLegendarySkin = DevItemPrefix + "skin.legendary.1";
        public const string DevLegendaryWeapon = DevItemPrefix + "weapon.legendary.1";
        public const string DevGachaOnlyItem = DevItemPrefix + "skill.rare.gachaonly";

        /// <summary>
        /// Development test items (not official, not approved, no art): one per rarity and kind so every rarity can be
        /// drawn in a development build, two LEGENDARY ones for the simulated events, one gacha-only item. Weapons are
        /// left out for COMMON and UNCOMMON so the redistribution between kinds is exercised.
        /// </summary>
        public static List<ItemDefinition> DevTestItems()
        {
            var list = new List<ItemDefinition>();
            void Add(ItemCategory c, WeaponRarity r, string name, bool exchangeable = true)
            {
                string kind = c.ToString().ToLowerInvariant();
                string id = $"{DevItemPrefix}{kind}.{r.ToString().ToLowerInvariant()}.{name}";
                list.Add(new ItemDefinition
                {
                    itemId = id,
                    category = c,
                    rarity = r,
                    refId = "",
                    displayName = $"テスト用 {CategoryLabel(c)} {RarityLabel(r)} {name}",
                    exchangeable = exchangeable,
                    devOnly = true,
                });
            }
            foreach (var r in new[] { WeaponRarity.Common, WeaponRarity.Uncommon, WeaponRarity.Rare, WeaponRarity.Epic })
            {
                Add(ItemCategory.Skin, r, "1");
                Add(ItemCategory.Skin, r, "2");
                Add(ItemCategory.Skill, r, "1");
                if (r == WeaponRarity.Rare || r == WeaponRarity.Epic)
                    Add(ItemCategory.Weapon, r, "1");
            }
            Add(ItemCategory.Skill, WeaponRarity.Rare, "gachaonly", exchangeable: false);
            Add(ItemCategory.Skin, WeaponRarity.Legendary, "1");
            Add(ItemCategory.Weapon, WeaponRarity.Legendary, "1");
            return list;
        }

        public static string CategoryLabel(ItemCategory c) => c switch
        {
            ItemCategory.Weapon => "武器",
            ItemCategory.Skin => "スキン",
            _ => "スキル",
        };

        public static string RarityLabel(WeaponRarity r) => r == WeaponRarity.Unassigned ? "未定" : r.ToString().ToUpperInvariant();
    }
}
