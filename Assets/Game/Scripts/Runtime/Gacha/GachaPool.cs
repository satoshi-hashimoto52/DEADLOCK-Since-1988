using System;
using System.Collections.Generic;
using Deadlock.Combat;

namespace Deadlock.Gacha
{
    /// <summary>Source of random whole numbers for the draw (seeded in tests; the server's later).</summary>
    public interface IGachaRandom
    {
        /// <summary>0 ≤ result &lt; maxExclusive, every value equally likely.</summary>
        int Next(int maxExclusive);
    }

    public sealed class SystemGachaRandom : IGachaRandom
    {
        readonly Random random;

        public SystemGachaRandom(int? seed = null) => random = seed.HasValue ? new Random(seed.Value) : new Random(Guid.NewGuid().GetHashCode());

        public int Next(int maxExclusive) => random.Next(maxExclusive);
    }

    /// <summary>One result of a draw.</summary>
    public readonly struct DrawnItem
    {
        public readonly ItemDefinition Item;
        public readonly bool Guaranteed;

        public DrawnItem(ItemDefinition item, bool guaranteed)
        {
            Item = item;
            Guaranteed = guaranteed;
        }
    }

    /// <summary>
    /// What one gacha can draw right now: the items by rarity and kind (gacha items only; LEGENDARY only those named by
    /// the active event). Draws in the order rarity → kind → item (D-055): the rarity by its weight, the kind by its weight
    /// among the kinds that have items in that rarity, the item evenly. A rarity that can come out and has no item makes
    /// the whole configuration invalid: nothing is drawn and no point is spent (<see cref="Problems"/>).
    /// </summary>
    public sealed class GachaPool
    {
        readonly Dictionary<(WeaponRarity, ItemCategory), List<ItemDefinition>> items =
            new Dictionary<(WeaponRarity, ItemCategory), List<ItemDefinition>>();

        public GachaEvent Event { get; }
        public bool LegendaryEvent => Event != null;
        public List<string> Problems { get; } = new List<string>();
        public bool IsOpen => Problems.Count == 0;

        public GachaPool(IEnumerable<ItemDefinition> catalogItems, GachaEvent activeEvent)
        {
            Event = activeEvent;
            foreach (var r in GachaRates.Rarities)
                foreach (var c in GachaRates.Categories)
                    items[(r, c)] = new List<ItemDefinition>();
            foreach (var item in catalogItems)
            {
                if (item == null || !item.gacha || item.rarity == WeaponRarity.Unassigned)
                    continue;
                if (item.EventLimited && (activeEvent == null || !activeEvent.targetItemIds.Contains(item.itemId)))
                    continue; // never outside its event, never a LEGENDARY the event does not name
                items[(item.rarity, item.category)].Add(item);
            }
            foreach (var r in GachaRates.Rarities)
            {
                bool canComeOut = GachaRates.RarityWeight(r, LegendaryEvent) > 0 || GachaRates.GuaranteedWeight(r, LegendaryEvent) > 0;
                if (canComeOut && CategoriesWithItems(r).Count == 0)
                    Problems.Add($"{ItemCatalog.RarityLabel(r)} に排出できるアイテムがありません");
            }
        }

        public IReadOnlyList<ItemDefinition> ItemsOf(WeaponRarity r, ItemCategory c) => items[(r, c)];

        public List<ItemCategory> CategoriesWithItems(WeaponRarity r)
        {
            var list = new List<ItemCategory>();
            foreach (var c in GachaRates.Categories)
                if (items[(r, c)].Count > 0)
                    list.Add(c);
            return list;
        }

        /// <summary>Exact chance of one item in a normal slot or the guaranteed slot.</summary>
        public Fraction ItemChance(ItemDefinition item, bool guaranteed)
        {
            var list = items[(item.rarity, item.category)];
            if (!list.Exists(i => i.itemId == item.itemId))
                return new Fraction(0, 1);
            return GachaRates.RarityChance(item.rarity, guaranteed, LegendaryEvent)
                   * GachaRates.CategoryChance(item.category, CategoriesWithItems(item.rarity))
                   * new Fraction(1, list.Count);
        }

        /// <summary>Draws one slot. Throws if the pool is not open (callers check <see cref="IsOpen"/> first).</summary>
        public DrawnItem Draw(IGachaRandom random, bool guaranteed)
        {
            if (!IsOpen)
                throw new InvalidOperationException("gacha configuration invalid: " + string.Join("; ", Problems));
            WeaponRarity rarity = Pick(GachaRates.Rarities, r => GachaRates.Weight(r, guaranteed, LegendaryEvent), random);
            var kinds = CategoriesWithItems(rarity);
            ItemCategory kind = Pick(kinds, GachaRates.CategoryWeight, random);
            var list = items[(rarity, kind)];
            return new DrawnItem(list[random.Next(list.Count)], guaranteed);
        }

        static T Pick<T>(IReadOnlyList<T> options, Func<T, int> weight, IGachaRandom random)
        {
            int total = 0;
            foreach (var o in options)
                total += weight(o);
            int roll = random.Next(total);
            foreach (var o in options)
            {
                int w = weight(o);
                if (roll < w)
                    return o;
                roll -= w;
            }
            throw new InvalidOperationException("weights");
        }
    }
}
