using System;
using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.Gacha
{
    /// <summary>
    /// The player's gacha data for the app (one <see cref="GachaService"/>). Kept in its own file in the app's data
    /// folder, apart from the settings (PlayerPrefs), the touch layout and the weapon lab's files, none of which it reads
    /// or writes. A development build keeps its data in another file (<see cref="DevFileName"/>), so test data never
    /// mixes with a release build's. Official events: none yet; official items: the weapon catalog's with a decided
    /// rarity (today only the EPIC SMG, so the official gacha cannot open: every rarity needs items).
    /// </summary>
    public static class GachaAccount
    {
        public const string FolderName = "gacha";
        public const string FileName = "gacha_state.json";
        public const string DevFileName = "gacha_state_dev.json";

        static GachaService service;

        /// <summary>Tests: the store (and clock / random) to use instead of the app's file.</summary>
        public static IGachaStore StoreOverride;
        public static IGachaClock ClockOverride;
        public static IGachaRandom RandomOverride;

        /// <summary>Official LEGENDARY events (id, period, items). None decided yet.</summary>
        public static readonly List<GachaEvent> OfficialEvents = new List<GachaEvent>();

        public static event Action Changed;

        public static GachaService Service
        {
            get
            {
                if (service == null)
                {
                    service = new GachaService(Store(), BuildCatalog(), new EventSchedule(OfficialEvents), ClockOverride ?? new DeviceClock(),
                        RandomOverride ?? new SystemGachaRandom(), () => GachaDev.UseTestItems, () => GachaDev.SimulatedEvent);
                    service.Changed += () => Changed?.Invoke();
                }
                return service;
            }
        }

        public static bool Exists => service != null;

        static IGachaStore Store() => StoreOverride ?? new FileGachaStore(Path.Combine(Application.persistentDataPath, FolderName),
            Debug.isDebugBuild ? DevFileName : FileName);

        /// <summary>The items the data may name: the official ones, and the test items in a development build.</summary>
        public static ItemCatalog BuildCatalog()
        {
            var items = ItemCatalog.OfficialItems(WeaponCatalog.Load());
            if (Debug.isDebugBuild)
                items.AddRange(ItemCatalog.DevTestItems());
            return new ItemCatalog(items);
        }

        /// <summary>Forgets the loaded data (tests; the next access loads again).</summary>
        public static void Reset() => service = null;
    }
}
