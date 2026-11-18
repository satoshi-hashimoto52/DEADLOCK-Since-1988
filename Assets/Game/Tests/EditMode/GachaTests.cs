using System;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Gacha;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 34 without a scene: the draw weights (exact fractions, and many seeded draws), the RARE-or-better slot,
    /// kinds and items, empty kinds and rarities, LEGENDARY events, owned items and duplicates, medals, the exchange
    /// shop, points, double requests, failed saves and interruptions, the history limit, version migration and imports.
    /// </summary>
    public class GachaTests
    {
        /// <summary>Returns queued numbers first (each below the asked maximum), then seeded random ones.</summary>
        sealed class QueueRandom : IGachaRandom
        {
            readonly Queue<int> queue = new Queue<int>();
            readonly System.Random fallback = new System.Random(7);

            public QueueRandom(params int[] values)
            {
                foreach (var v in values)
                    queue.Enqueue(v);
            }

            public void Push(params int[] values)
            {
                foreach (var v in values)
                    queue.Enqueue(v);
            }

            public int Next(int max)
            {
                if (queue.Count == 0)
                    return fallback.Next(max);
                int v = queue.Dequeue();
                Assert.Less(v, max, "scripted roll inside the range");
                return v;
            }
        }

        // Rolls of a normal slot without an event (weights 120 / 50 / 24 / 5) and kinds (20 / 50 / 30 among those with items).
        const int RollCommon = 0, RollRare = 170, RollEpic = 194;

        static ItemCatalog Catalog() => new ItemCatalog(ItemCatalog.OfficialItems(WeaponCatalog.Load()).Concat(ItemCatalog.DevTestItems()));

        static GachaService Service(MemoryGachaStore store, IGachaRandom random = null, GachaEvent ev = null, bool testItems = true) =>
            new GachaService(store, Catalog(), new EventSchedule(null), new FixedClock(DateTime.UtcNow), random ?? new SystemGachaRandom(1),
                () => testItems, () => ev);

        static GachaService WithPoints(MemoryGachaStore store, int points, IGachaRandom random = null, GachaEvent ev = null)
        {
            var s = Service(store, random, ev);
            Assert.IsTrue(s.GrantDevPoints(points).Ok);
            return s;
        }

        [TearDown]
        public void TearDown() => GachaDev.Reset();

        // ------------------------------------------------------------------ 1, 2, 4: exact weights

        [Test]
        public void RarityWeights_EventAndNormal_ExactFractions_DisplayTwoDecimals()
        {
            var ev = new[] { (WeaponRarity.Common, 3, 5), (WeaponRarity.Uncommon, 1, 4), (WeaponRarity.Rare, 3, 25), (WeaponRarity.Epic, 1, 40), (WeaponRarity.Legendary, 1, 200) };
            foreach (var (r, n, d) in ev)
                Assert.AreEqual(new Fraction(n, d), GachaRates.RarityChance(r, false, true), $"event {r}");
            // Outside an event: LEGENDARY out, the rest over 99.5 (= 199 / 2).
            var normal = new[] { (WeaponRarity.Common, 120), (WeaponRarity.Uncommon, 50), (WeaponRarity.Rare, 24), (WeaponRarity.Epic, 5), (WeaponRarity.Legendary, 0) };
            foreach (var (r, n) in normal)
                Assert.AreEqual(new Fraction(n, 199), GachaRates.RarityChance(r, false, false), $"normal {r}");
            Assert.AreEqual(60.0 / 99.5, GachaRates.RarityChance(WeaponRarity.Common, false, false).Value, 1e-12);
            Assert.AreEqual(2.5 / 99.5, GachaRates.RarityChance(WeaponRarity.Epic, false, false).Value, 1e-12);
            CollectionAssert.AreEqual(new[] { "60.30%", "25.13%", "12.06%", "2.51%", "0.00%" },
                GachaRates.Rarities.Select(r => GachaRates.RarityChance(r, false, false).Percent).ToArray());
            CollectionAssert.AreEqual(new[] { "60.00%", "25.00%", "12.00%", "2.50%", "0.50%" },
                GachaRates.Rarities.Select(r => GachaRates.RarityChance(r, false, true).Percent).ToArray());
        }

        [Test]
        public void GuaranteedSlot_24To5To1_Exact()
        {
            Assert.AreEqual(new Fraction(4, 5), GachaRates.RarityChance(WeaponRarity.Rare, true, true));
            Assert.AreEqual(new Fraction(1, 6), GachaRates.RarityChance(WeaponRarity.Epic, true, true));
            Assert.AreEqual(new Fraction(1, 30), GachaRates.RarityChance(WeaponRarity.Legendary, true, true));
            Assert.AreEqual(new Fraction(24, 29), GachaRates.RarityChance(WeaponRarity.Rare, true, false));
            Assert.AreEqual(new Fraction(5, 29), GachaRates.RarityChance(WeaponRarity.Epic, true, false));
            Assert.AreEqual(new Fraction(0, 1), GachaRates.RarityChance(WeaponRarity.Legendary, true, false));
            foreach (var low in new[] { WeaponRarity.Common, WeaponRarity.Uncommon })
                foreach (bool e in new[] { false, true })
                    Assert.AreEqual(0, GachaRates.RarityChance(low, true, e).Numerator);
            CollectionAssert.AreEqual(new[] { "80.00%", "16.67%", "3.33%" },
                new[] { WeaponRarity.Rare, WeaponRarity.Epic, WeaponRarity.Legendary }.Select(r => GachaRates.RarityChance(r, true, true).Percent).ToArray());
            CollectionAssert.AreEqual(new[] { "82.76%", "17.24%", "0.00%" },
                new[] { WeaponRarity.Rare, WeaponRarity.Epic, WeaponRarity.Legendary }.Select(r => GachaRates.RarityChance(r, true, false).Percent).ToArray());
        }

        [Test]
        public void Categories_20_50_30_EmptyKindSharedOut_ItemsEven()
        {
            var all = new List<ItemCategory> { ItemCategory.Weapon, ItemCategory.Skin, ItemCategory.Skill };
            Assert.AreEqual(new Fraction(1, 5), GachaRates.CategoryChance(ItemCategory.Weapon, all));
            Assert.AreEqual(new Fraction(1, 2), GachaRates.CategoryChance(ItemCategory.Skin, all));
            Assert.AreEqual(new Fraction(3, 10), GachaRates.CategoryChance(ItemCategory.Skill, all));

            // COMMON test items: no weapon → skin 50/80, skill 30/80; the rarity stays COMMON.
            var pool = new GachaPool(Catalog().Items, null);
            Assert.IsTrue(pool.IsOpen, string.Join(";", pool.Problems));
            CollectionAssert.AreEqual(new[] { ItemCategory.Skin, ItemCategory.Skill }, pool.CategoriesWithItems(WeaponRarity.Common));
            var skins = pool.ItemsOf(WeaponRarity.Common, ItemCategory.Skin);
            Assert.AreEqual(2, skins.Count);
            foreach (var skin in skins)
                Assert.AreEqual(new Fraction(120, 199) * new Fraction(5, 8) * new Fraction(1, 2), pool.ItemChance(skin, false), "even within rarity and kind");
            var skill = pool.ItemsOf(WeaponRarity.Common, ItemCategory.Skill)[0];
            Assert.AreEqual(new Fraction(120, 199) * new Fraction(3, 8), pool.ItemChance(skill, false));
            // Every item's chances add up to one, in both slot kinds, with and without an event.
            foreach (var ev in new[] { null, GachaDev.TestEvent(false) })
            {
                var p = new GachaPool(Catalog().Items, ev);
                foreach (bool g in new[] { false, true })
                {
                    double sum = Catalog().Items.Sum(i => p.ItemChance(i, g).Value);
                    Assert.AreEqual(1.0, sum, 1e-9, $"event {ev != null} guaranteed {g}");
                }
            }
        }

        [Test]
        public void ManyDraws_FollowTheExactChances()
        {
            const int n = 200000;
            var rng = new SystemGachaRandom(12345);
            foreach (var ev in new[] { null, GachaDev.TestEvent(false) })
            {
                var pool = new GachaPool(Catalog().Items, ev);
                var byRarity = new Dictionary<WeaponRarity, int>();
                var byItem = new Dictionary<string, int>();
                for (int i = 0; i < n; i++)
                {
                    var d = pool.Draw(rng, false);
                    byRarity[d.Item.rarity] = byRarity.TryGetValue(d.Item.rarity, out int c) ? c + 1 : 1;
                    byItem[d.Item.itemId] = byItem.TryGetValue(d.Item.itemId, out int k) ? k + 1 : 1;
                }
                foreach (var r in GachaRates.Rarities)
                {
                    double p = GachaRates.RarityChance(r, false, ev != null).Value;
                    double sigma = Math.Sqrt(n * p * (1 - p));
                    byRarity.TryGetValue(r, out int got);
                    Assert.AreEqual(n * p, got, 5 * sigma + 1, $"event {ev != null} {r}");
                }
                if (ev == null)
                    Assert.IsFalse(byRarity.ContainsKey(WeaponRarity.Legendary), "no LEGENDARY outside an event");
                foreach (var item in Catalog().Items)
                {
                    double p = pool.ItemChance(item, false).Value;
                    byItem.TryGetValue(item.itemId, out int got);
                    Assert.AreEqual(n * p, got, 5 * Math.Sqrt(n * p * (1 - p)) + 1, item.itemId);
                }
            }
            // The guaranteed slot.
            var gp = new GachaPool(Catalog().Items, GachaDev.TestEvent(false));
            var counts = new Dictionary<WeaponRarity, int>();
            for (int i = 0; i < n; i++)
            {
                var r = gp.Draw(rng, true).Item.rarity;
                counts[r] = counts.TryGetValue(r, out int c) ? c + 1 : 1;
            }
            Assert.AreEqual(n * 0.8, counts[WeaponRarity.Rare], 5 * Math.Sqrt(n * 0.16));
            Assert.AreEqual(n / 30.0, counts[WeaponRarity.Legendary], 5 * Math.Sqrt(n / 30.0));
            Assert.IsFalse(counts.ContainsKey(WeaponRarity.Common) || counts.ContainsKey(WeaponRarity.Uncommon));
        }

        // ------------------------------------------------------------------ 3: the 11-draw

        [Test]
        public void TenDraw_TenNormalSlotsAndOneRareOrBetter_1000Points()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 30000, new SystemGachaRandom(3));
            for (int k = 0; k < 30; k++)
            {
                var res = s.Draw(GachaKind.Ten, "ten-" + k);
                Assert.AreEqual(GachaOutcome.Done, res.Outcome, res.Message);
                Assert.AreEqual(11, res.Record.results.Count);
                Assert.AreEqual(1000, res.Record.cost);
                for (int i = 0; i < 10; i++)
                    Assert.IsFalse(res.Record.results[i].guaranteed);
                var last = res.Record.results[10];
                Assert.IsTrue(last.guaranteed);
                Assert.GreaterOrEqual((int)last.Rarity, (int)WeaponRarity.Rare, "RARE or better");
            }
            Assert.AreEqual(0, s.State.points);
            // Even when every normal slot is COMMON.
            var rng = new QueueRandom();
            for (int i = 0; i < 10; i++)
                rng.Push(RollCommon, 0, 0);
            var s2 = WithPoints(new MemoryGachaStore(), 1000, rng);
            var r2 = s2.Draw(GachaKind.Ten, "all-common");
            Assert.IsTrue(r2.Record.results.Take(10).All(x => x.Rarity == WeaponRarity.Common));
            Assert.GreaterOrEqual((int)r2.Record.results[10].Rarity, (int)WeaponRarity.Rare);
        }

        // ------------------------------------------------------------------ 6, 7: empty kinds and rarities

        [Test]
        public void RarityWithoutItems_ConfigurationInvalid_NothingSpent()
        {
            // Official items only (today the EPIC SMG): COMMON to RARE have nothing.
            var official = new GachaPool(ItemCatalog.OfficialItems(WeaponCatalog.Load()), null);
            Assert.IsFalse(official.IsOpen);
            StringAssert.Contains("COMMON", string.Join(";", official.Problems));
            Assert.Throws<InvalidOperationException>(() => official.Draw(new SystemGachaRandom(1), false));

            var store = new MemoryGachaStore();
            var s = Service(store, testItems: true);
            Assert.IsTrue(s.GrantDevPoints(1000).Ok);
            var closed = new GachaService(store, Catalog(), null, new FixedClock(DateTime.UtcNow), new SystemGachaRandom(1), () => false, () => null);
            int writes = store.Writes;
            var r = closed.Draw(GachaKind.Single, "x");
            Assert.AreEqual(GachaOutcome.InvalidConfiguration, r.Outcome);
            Assert.AreEqual(1000, closed.State.points, "no point spent");
            Assert.AreEqual(writes, store.Writes, "nothing written");
            Assert.IsEmpty(closed.State.history);

            // An event without LEGENDARY items: the event gacha does not open.
            var empty = new GachaEvent { eventId = "e", startUtc = "2000-01-01T00:00:00Z", endUtc = "2100-01-01T00:00:00Z" };
            Assert.IsFalse(new GachaPool(Catalog().Items, empty).IsOpen);
        }

        // ------------------------------------------------------------------ 8, 9: LEGENDARY events

        [Test]
        public void Legendary_OnlyInItsEvent_RerunOnlyItsItems_TrustedTimeOnly()
        {
            var cat = Catalog();
            var none = new GachaPool(cat.Items, null);
            Assert.AreEqual(0, GachaRates.Rarities.Contains(WeaponRarity.Legendary) ? GachaRates.RarityWeight(WeaponRarity.Legendary, false) : -1);
            Assert.IsEmpty(none.CategoriesWithItems(WeaponRarity.Legendary), "no LEGENDARY outside an event");

            var first = new GachaPool(cat.Items, GachaDev.TestEvent(false));
            Assert.AreEqual(2, first.ItemsOf(WeaponRarity.Legendary, ItemCategory.Skin).Count + first.ItemsOf(WeaponRarity.Legendary, ItemCategory.Weapon).Count);
            var rerun = new GachaPool(cat.Items, GachaDev.TestEvent(true));
            Assert.AreEqual(1, rerun.ItemsOf(WeaponRarity.Legendary, ItemCategory.Skin).Count);
            Assert.IsEmpty(rerun.ItemsOf(WeaponRarity.Legendary, ItemCategory.Weapon), "a rerun brings back only its items");
            Assert.AreEqual(0, rerun.ItemChance(cat.Find(ItemCatalog.DevLegendaryWeapon), true).Numerator);

            // The shop likewise.
            var store = new MemoryGachaStore();
            Assert.IsNull(Service(store).ShopLineup().Find(i => i.rarity == WeaponRarity.Legendary));
            var rerunShop = Service(store, ev: GachaDev.TestEvent(true)).ShopLineup();
            CollectionAssert.AreEqual(new[] { ItemCatalog.DevLegendarySkin },
                rerunShop.Where(i => i.rarity == WeaponRarity.Legendary).Select(i => i.itemId).ToArray());

            // The schedule: by a trusted clock only, inside the period only.
            var e = new GachaEvent { eventId = "ev1", startUtc = "2026-11-01T00:00:00Z", endUtc = "2026-11-08T00:00:00Z", targetItemIds = { ItemCatalog.DevLegendarySkin } };
            var schedule = new EventSchedule(new[] { e });
            Assert.AreSame(e, schedule.Active(new FixedClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc))));
            Assert.IsNull(schedule.Active(new FixedClock(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc))), "ended");
            Assert.IsNull(schedule.Active(new FixedClock(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc))), "not started");
            Assert.IsNull(schedule.Active(new FixedClock(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), trusted: false)), "never by an untrusted clock");
            Assert.IsNull(schedule.Active(new DeviceClock()), "the device clock is not trusted");
            Assert.AreEqual(0, GachaAccount.OfficialEvents.Count, "no official event yet");
        }

        [Test]
        public void AfterTheEvent_LegendaryAndMedalsKept()
        {
            var store = new MemoryGachaStore();
            var ev = GachaDev.TestEvent(false);
            GachaEvent current = ev;
            var rng = new QueueRandom();
            var s = new GachaService(store, Catalog(), null, new FixedClock(DateTime.UtcNow), rng, () => true, () => current);
            Assert.IsTrue(s.GrantDevPoints(200).Ok);
            rng.Push(199, 0, 0, 199, 0, 0); // LEGENDARY (weight 1 of 200) twice, the weapon (kinds weapon 20 / skin 50: roll 0)
            Assert.IsTrue(s.Draw(GachaKind.Single, "a").Ok);
            Assert.IsTrue(s.Draw(GachaKind.Single, "b").Ok);
            Assert.AreEqual(ItemCatalog.DevLegendaryWeapon, s.State.history[0].results[0].itemId);
            Assert.AreEqual(100, s.State.medals, "the duplicate LEGENDARY");
            current = null; // the event ends
            Assert.IsTrue(s.State.Owns(ItemCatalog.DevLegendaryWeapon));
            Assert.AreEqual(100, s.State.medals);
            Assert.IsNull(s.ShopLineup().Find(i => i.itemId == ItemCatalog.DevLegendaryWeapon));
        }

        // ------------------------------------------------------------------ 10, 11, 17: owned items and medals

        [Test]
        public void FirstTimeOwned_DuplicatesToMedals_PerRarity_SameItemTwiceInOneTenDraw()
        {
            Assert.AreEqual(new[] { 1, 3, 10, 30, 100 }, GachaRates.Rarities.Select(GachaRates.DuplicateMedals).ToArray());
            var rng = new QueueRandom();
            var s = WithPoints(new MemoryGachaStore(), 1000, rng);
            // Ten normal slots: the same COMMON skin three times, the same RARE skin twice, others; guaranteed: an EPIC skill.
            rng.Push(RollCommon, 20, 0); // COMMON → skin (kinds skin 50 / skill 30: 20 < 50) → item 0
            rng.Push(RollCommon, 20, 0);
            rng.Push(RollRare, 30, 0); // RARE → kinds weapon 20 / skin 50 / skill 30: 30 → skin → item 0
            rng.Push(RollCommon, 20, 0);
            rng.Push(RollRare, 30, 0);
            rng.Push(RollEpic, 80, 0); // EPIC skill
            rng.Push(RollEpic, 80, 0);
            rng.Push(120, 20, 0); // UNCOMMON skin
            rng.Push(120, 20, 0);
            rng.Push(RollCommon, 60, 0); // COMMON skill
            rng.Push(24, 0, 0); // guaranteed (24 / 5): EPIC → weapon → the EPIC SMG (official, first)
            var r = s.Draw(GachaKind.Ten, "ten");
            Assert.AreEqual(GachaOutcome.Done, r.Outcome, r.Message);
            var res = r.Record.results;
            CollectionAssert.AreEqual(new[] { true, false, true, false, false, true, false, true, false, true, true }, res.Select(x => x.firstTime).ToArray(),
                "in slot order: the first time owned, later times duplicates");
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 10, 0, 30, 0, 3, 0, 0 }, res.Select(x => x.medals).ToArray());
            Assert.AreEqual(45, s.State.medals);
            Assert.AreEqual(6, s.State.owned.Count, "each item owned once");
            Assert.AreEqual(2, s.State.FindOwned(res[0].itemId).duplicates);
            Assert.AreEqual("weapon:proto_epic_smg", res[10].itemId);
            Assert.AreEqual(45, r.Record.MedalsGained);
        }

        // ------------------------------------------------------------------ 12, 13: the exchange shop, the EPIC SMG

        [Test]
        public void Shop_PricesAndRules_EpicSmgFromGachaAndShop()
        {
            Assert.AreEqual(new[] { 30, 100, 350, 1000, 3500 }, GachaRates.Rarities.Select(GachaRates.ExchangePrice).ToArray());
            var epic = Catalog().Find("weapon:proto_epic_smg");
            Assert.IsNotNull(epic, "the EPIC SMG is an official item");
            Assert.AreEqual(WeaponRarity.Epic, epic.rarity);
            Assert.AreEqual(ItemCategory.Weapon, epic.category);
            Assert.AreEqual(EpicSmgSpec.WeaponId, epic.refId);
            Assert.IsTrue(epic.gacha && epic.exchangeable && !epic.devOnly);
            // Other catalog weapons have no decided rarity: not items (nor the knife).
            Assert.AreEqual(1, ItemCatalog.OfficialItems(WeaponCatalog.Load()).Count);

            var store = new MemoryGachaStore();
            var s = Service(store);
            Assert.IsNull(s.ShopLineup().Find(i => i.itemId == ItemCatalog.DevGachaOnlyItem), "gacha-only items are not shown");
            Assert.AreEqual(GachaOutcome.NotOffered, s.Exchange(ItemCatalog.DevGachaOnlyItem, "g").Outcome);
            Assert.AreEqual(GachaOutcome.NotEnoughMedals, s.Exchange(epic.itemId, "e0").Outcome);
            SetMedals(s, store, 1200);
            var ok = s.Exchange(epic.itemId, "e1");
            Assert.AreEqual(GachaOutcome.Done, ok.Outcome, ok.Message);
            Assert.AreEqual(200, s.State.medals);
            Assert.AreEqual("exchange", s.State.FindOwned(epic.itemId).source);
            Assert.AreEqual(GachaOutcome.AlreadyApplied, s.Exchange(epic.itemId, "e1").Outcome, "the same request once");
            Assert.AreEqual(200, s.State.medals);
            SetMedals(s, store, 5000);
            Assert.AreEqual(GachaOutcome.AlreadyOwned, s.Exchange(epic.itemId, "e2").Outcome);
            Assert.AreEqual(5000, s.State.medals);
            // A failed save: no medal lost, nothing owned.
            var common = Catalog().Items.First(i => i.devOnly && i.rarity == WeaponRarity.Common);
            store.FailNextWrites = 1;
            Assert.AreEqual(GachaOutcome.SaveFailed, s.Exchange(common.itemId, "e3").Outcome);
            Assert.AreEqual(5000, s.State.medals);
            Assert.IsFalse(s.State.Owns(common.itemId));
            Assert.AreEqual(GachaOutcome.Done, s.Exchange(common.itemId, "e4").Outcome);
            Assert.AreEqual(4970, s.State.medals);

            // From the gacha: EPIC → weapon → the EPIC SMG.
            var s2 = WithPoints(new MemoryGachaStore(), 100, new QueueRandom(RollEpic, 0, 0));
            var drawn = s2.Draw(GachaKind.Single, "d");
            Assert.AreEqual(epic.itemId, drawn.Record.results[0].itemId);
            Assert.IsTrue(s2.State.Owns(epic.itemId));
        }

        static void SetMedals(GachaService s, MemoryGachaStore store, int medals)
        {
            var data = s.State.Clone();
            data.medals = medals;
            store.Write(GachaSave.Serialize(data));
            s.Load();
        }

        // ------------------------------------------------------------------ 14, 15, 16: points, double requests, failures

        [Test]
        public void NotEnoughPoints_Refused_NothingChanges()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 900);
            int writes = store.Writes;
            var r = s.Draw(GachaKind.Ten, "t");
            Assert.AreEqual(GachaOutcome.NotEnoughPoints, r.Outcome);
            Assert.AreEqual(900, s.State.points);
            Assert.AreEqual(writes, store.Writes);
            Assert.IsEmpty(s.State.history);
            Assert.IsTrue(s.Draw(GachaKind.Single, "s").Ok);
            Assert.AreEqual(800, s.State.points);
        }

        [Test]
        public void SameRequestTwice_AppliedOnce()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 2000);
            var a = s.Draw(GachaKind.Ten, "tap");
            var b = s.Draw(GachaKind.Ten, "tap");
            Assert.AreEqual(GachaOutcome.Done, a.Outcome);
            Assert.AreEqual(GachaOutcome.AlreadyApplied, b.Outcome);
            Assert.AreSame(a.Record, b.Record, "the same result back");
            Assert.AreEqual(1000, s.State.points, "charged once");
            Assert.AreEqual(1, s.State.history.Count);
            // Also after a restart (the request is in the saved data).
            var again = Service(store);
            Assert.AreEqual(GachaOutcome.AlreadyApplied, again.Draw(GachaKind.Ten, "tap").Outcome);
            Assert.AreEqual(1000, again.State.points);
        }

        [Test]
        public void FailedSave_NothingLost_Interruption_RecoveredAndShownAgain()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 1000);
            string before = store.Main;
            store.FailNextWrites = 1;
            var r = s.Draw(GachaKind.Ten, "fail");
            Assert.AreEqual(GachaOutcome.SaveFailed, r.Outcome);
            Assert.AreEqual(1000, s.State.points, "no point lost");
            Assert.IsEmpty(s.State.history);
            Assert.IsEmpty(s.State.owned);
            Assert.AreEqual(before, store.Main, "the saved data unchanged");

            // Saved, then the app stops before the opening: a restart has the result, marked not shown.
            Assert.IsTrue(s.Draw(GachaKind.Ten, "ok").Ok);
            var restarted = Service(store);
            Assert.AreEqual(0, restarted.State.points);
            Assert.AreEqual(11, restarted.State.history[0].results.Count);
            Assert.AreEqual("ok", restarted.Unrevealed()?.requestId, "shown again");
            restarted.MarkRevealed("ok");
            Assert.IsNull(Service(store).Unrevealed());

            // A damaged last save: the one before is used.
            string good = store.Main;
            store.Write(good.Replace("\"points\": 0", "\"points\": 99999"));
            var recovered = Service(store);
            Assert.IsTrue(recovered.RecoveredFromPrevious);
            Assert.IsNull(recovered.LoadError);
            Assert.AreEqual(0, recovered.State.points);
            // Both damaged: nothing is done and nothing is written over them.
            store.Main = "{broken";
            store.Previous = "{broken";
            var broken = Service(store);
            Assert.IsNotNull(broken.LoadError);
            int writes = store.Writes;
            Assert.AreEqual(GachaOutcome.DataUnavailable, broken.Draw(GachaKind.Single, "z").Outcome);
            Assert.AreEqual(GachaOutcome.DataUnavailable, broken.GrantDevPoints(100).Outcome);
            Assert.AreEqual(writes, store.Writes);
            Assert.AreEqual("{broken", store.Main);
        }

        [Test]
        public void FileStore_WritesWhole_KeepsThePrevious()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deadlock_gacha_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileGachaStore(dir, "state.json");
                Assert.IsNull(store.Read());
                store.Write("one");
                store.Write("two");
                Assert.AreEqual("two", store.Read());
                Assert.AreEqual("one", store.ReadPrevious());
                Assert.IsFalse(System.IO.File.Exists(store.MainPath + ".tmp"), "no temporary file left");
                string backup = store.Backup("two", "test");
                Assert.IsTrue(System.IO.File.Exists(backup));
                var s = new GachaService(store, Catalog(), null, new FixedClock(DateTime.UtcNow), new SystemGachaRandom(1), () => true, () => null);
                Assert.IsNotNull(s.LoadError, "\"two\" is not gacha data");
                System.IO.File.Delete(store.MainPath);
                System.IO.File.Delete(store.PreviousPath);
                s = new GachaService(store, Catalog(), null, new FixedClock(DateTime.UtcNow), new SystemGachaRandom(1), () => true, () => null);
                Assert.IsTrue(s.GrantDevPoints(100).Ok);
                Assert.IsTrue(s.Draw(GachaKind.Single, "f").Ok);
                var reread = new GachaService(store, Catalog(), null, new FixedClock(DateTime.UtcNow), new SystemGachaRandom(1), () => true, () => null);
                Assert.AreEqual(0, reread.State.points);
                Assert.AreEqual(1, reread.State.owned.Count);
            }
            finally
            {
                if (System.IO.Directory.Exists(dir))
                    System.IO.Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------------ 18: history

        [Test]
        public void History_Last100_TrimmingKeepsAssets()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 105 * 100);
            for (int i = 0; i < 105; i++)
                Assert.IsTrue(s.Draw(GachaKind.Single, "h" + i).Ok);
            Assert.AreEqual(100, s.State.history.Count);
            Assert.AreEqual("h5", s.State.history[0].requestId, "the oldest went");
            Assert.AreEqual("h104", s.State.history[99].requestId);
            Assert.AreEqual(0, s.State.points);
            int ownedTotal = s.State.owned.Count + s.State.owned.Sum(o => o.duplicates);
            Assert.AreEqual(105, ownedTotal, "all 105 results still counted in owned items and duplicates");
            int medalsFromDuplicates = 0;
            foreach (var o in s.State.owned)
                medalsFromDuplicates += o.duplicates * GachaRates.DuplicateMedals(Catalog().Find(o.itemId).rarity);
            Assert.AreEqual(medalsFromDuplicates, s.State.medals, "medals of trimmed draws kept");
            Assert.IsEmpty(GachaSave.Validate(s.State, Catalog()));
            // An 11-draw is one record with 11 results.
            s.GrantDevPoints(1000);
            Assert.IsTrue(s.Draw(GachaKind.Ten, "t").Ok);
            Assert.AreEqual(100, s.State.history.Count);
            Assert.AreEqual(11, s.State.history[99].results.Count);
        }

        // ------------------------------------------------------------------ 19, 20: migration and imports

        const string V1Json = "{\"format\":\"deadlock.gacha\",\"version\":1,\"points\":300,\"medals\":13,\"owned\":[\"weapon:proto_epic_smg\",\"devtest:skin.common.1\"]," +
                              "\"history\":[{\"timeUtc\":\"2026-10-30T10:00:00.0000000Z\",\"kind\":\"single\",\"results\":[{\"itemId\":\"devtest:skin.common.1\",\"firstTime\":false,\"medals\":1}]}]}";

        [Test]
        public void Version1_MigratedStepByStep_SavedAsCurrent()
        {
            var data = GachaSave.Parse(V1Json, Catalog(), out string error, out int version);
            Assert.IsNotNull(data, error);
            Assert.AreEqual(1, version);
            Assert.AreEqual(300, data.points);
            Assert.AreEqual(13, data.medals);
            Assert.AreEqual(2, data.owned.Count);
            Assert.AreEqual("migrated", data.owned[0].source);
            var rec = data.history[0];
            Assert.AreEqual("Common", rec.results[0].rarity);
            Assert.AreEqual("Skin", rec.results[0].category);
            Assert.AreEqual(100, rec.cost);
            Assert.IsTrue(rec.revealed);
            Assert.IsNotNull(data.FindRequest(rec.requestId));

            var store = new MemoryGachaStore { Main = V1Json };
            var s = Service(store);
            Assert.IsNull(s.LoadError);
            StringAssert.Contains("\"version\": 2", store.Main, "rewritten in the current version");
            Assert.AreEqual(300, Service(store).State.points);
        }

        [Test]
        public void BadImports_Refused_CurrentDataUntouched_GoodImport_BackedUpReplacedChecked()
        {
            var store = new MemoryGachaStore();
            var s = WithPoints(store, 500);
            Assert.IsTrue(s.Draw(GachaKind.Single, "x").Ok);
            string current = store.Main;
            var good = new GachaStateData { points = 7000, medals = 40 };
            good.owned.Add(new OwnedItem { itemId = "weapon:proto_epic_smg", source = "exchange" });
            string goodJson = GachaSave.Serialize(good);

            var bad = new List<(string why, string json)>
            {
                ("not JSON", "hello"),
                ("other format", "{\"format\":\"other\",\"version\":2}"),
                ("future version", goodJson.Replace("\"version\": 2", "\"version\": 3")),
                ("unknown old version", goodJson.Replace("\"version\": 2", "\"version\": 0")),
                ("checksum", goodJson.Replace("\"points\": 7000", "\"points\": 9000")),
            };
            var negative = good.Clone();
            negative.points = -5;
            bad.Add(("negative points", GachaSave.Serialize(negative)));
            var unknown = good.Clone();
            unknown.owned.Add(new OwnedItem { itemId = "weapon:no_such_gun" });
            bad.Add(("unknown item", GachaSave.Serialize(unknown)));
            var twice = good.Clone();
            twice.owned.Add(new OwnedItem { itemId = "weapon:proto_epic_smg" });
            bad.Add(("owned twice", GachaSave.Serialize(twice)));
            var tooMany = good.Clone();
            for (int i = 0; i < 101; i++)
                tooMany.history.Add(new HistoryRecord { requestId = "r" + i, kind = "single", results = { new DrawResult { itemId = "weapon:proto_epic_smg", firstTime = true } } });
            bad.Add(("101 records", GachaSave.Serialize(tooMany)));
            foreach (var (why, json) in bad)
            {
                var check = s.CheckImport(json);
                Assert.IsFalse(check.Ok, why);
                Assert.IsFalse(string.IsNullOrEmpty(check.Error), why);
                Assert.AreEqual(GachaOutcome.NotOffered, s.ApplyImport(check).Outcome, why);
                Assert.AreEqual(current, store.Main, why + ": current data untouched");
            }
            Assert.AreEqual(400, s.State.points);

            // A write failure during the import: the original stays.
            var okCheck = s.CheckImport(goodJson);
            Assert.IsTrue(okCheck.Ok, okCheck.Error);
            store.FailNextWrites = 1;
            Assert.AreEqual(GachaOutcome.SaveFailed, s.ApplyImport(okCheck).Outcome);
            Assert.AreEqual(current, store.Main);
            Assert.AreEqual(400, s.State.points);

            // A good one: backed up first, then the whole gacha data replaced.
            int backups = store.Backups.Count;
            var done = s.ApplyImport(okCheck);
            Assert.AreEqual(GachaOutcome.Done, done.Outcome, done.Message);
            Assert.AreEqual(backups + 1, store.Backups.Count);
            Assert.AreEqual(current, store.Backups.Last(), "the data before");
            Assert.AreEqual(7000, s.State.points);
            Assert.AreEqual(40, s.State.medals);
            Assert.IsEmpty(s.State.history, "replaced as a whole, nothing merged");
            StringAssert.Contains("local-imported-unverified", store.Main, "never trusted as official assets");
            // An old version can be imported too.
            Assert.IsTrue(s.CheckImport(V1Json).Ok);
        }

        // ------------------------------------------------------------------ 21: development only

        [Test]
        public void DevelopmentFunctions_OffInARelease_TestItemsNotOfficial()
        {
            Assert.IsTrue(Catalog().Items.Where(i => i.devOnly).All(i => i.itemId.StartsWith(ItemCatalog.DevItemPrefix)));
            Assert.IsFalse(ItemCatalog.OfficialItems(WeaponCatalog.Load()).Any(i => i.devOnly));
            // The development equipment's guns (Stage 33) are not items: only weapons with a decided rarity are.
            foreach (var id in new[] { "proto_rifle", "proto_smg", "proto_shotgun", "proto_handgun", "proto_lmg", "proto_knife" })
                Assert.IsNull(Catalog().Find(ItemDefinition.WeaponItemId(id)), id);
            Assert.IsNull(typeof(GachaStateData).GetField("loadout"), "no equipment in the gacha data");

            var s = Service(new MemoryGachaStore());
            GachaDev.UseTestItems = true;
            GachaDev.SimulatedEvent = GachaDev.TestEvent(false);
            GachaDev.SwitchedOff = true;
            Assert.IsFalse(GachaDev.Enabled);
            Assert.IsFalse(GachaDev.UseTestItems);
            Assert.IsNull(GachaDev.SimulatedEvent);
            Assert.AreEqual(GachaOutcome.Disabled, s.GrantDevPoints(100).Outcome);
            Assert.IsNull(s.Export());
            Assert.IsFalse(s.CheckImport(GachaSave.Serialize(new GachaStateData())).Ok);
            GachaDev.SwitchedOff = false;
            Assert.IsNotNull(s.Export());
        }
    }
}
