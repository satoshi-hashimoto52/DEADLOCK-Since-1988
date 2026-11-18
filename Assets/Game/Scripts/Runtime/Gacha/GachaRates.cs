using System;
using System.Collections.Generic;
using System.Globalization;
using Deadlock.Combat;

namespace Deadlock.Gacha
{
    /// <summary>An exact probability (numerator / denominator), so no rounded display value is ever used to draw.</summary>
    public readonly struct Fraction : IEquatable<Fraction>
    {
        public readonly long Numerator, Denominator;

        public Fraction(long numerator, long denominator)
        {
            if (denominator <= 0)
                throw new ArgumentException("denominator");
            long g = Gcd(Math.Abs(numerator), denominator);
            if (g == 0)
                g = 1;
            Numerator = numerator / g;
            Denominator = denominator / g;
        }

        static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

        public double Value => (double)Numerator / Denominator;
        public static Fraction operator *(Fraction a, Fraction b) => new Fraction(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
        public bool Equals(Fraction o) => Numerator == o.Numerator && Denominator == o.Denominator;
        public override bool Equals(object obj) => obj is Fraction f && Equals(f);
        public override int GetHashCode() => unchecked((int)(Numerator * 397) ^ Denominator.GetHashCode());
        public override string ToString() => $"{Numerator}/{Denominator}";

        /// <summary>Percent with two decimals, for display only (e.g. 60.30%).</summary>
        public string Percent => (Value * 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// The confirmed tables of D-055 / D-057, kept as whole numbers. Rarity weights 60 : 25 : 12 : 2.5 : 0.5 are held
    /// doubled (120 : 50 : 24 : 5 : 1): outside an event LEGENDARY is left out and the rest is normalised over 199
    /// (= 99.5 doubled); the RARE-or-better slot uses 24 : 5 (: 1). Kind weights 20 : 50 : 30.
    /// </summary>
    public static class GachaRates
    {
        public const int SingleCost = 100;
        public const int TenCost = 1000;
        public const int TenNormalSlots = 10;
        public const int TenSlots = 11;

        public static readonly WeaponRarity[] Rarities =
            { WeaponRarity.Common, WeaponRarity.Uncommon, WeaponRarity.Rare, WeaponRarity.Epic, WeaponRarity.Legendary };

        public static readonly ItemCategory[] Categories = { ItemCategory.Weapon, ItemCategory.Skin, ItemCategory.Skill };

        public static int RarityWeight(WeaponRarity r, bool legendaryEvent) => r switch
        {
            WeaponRarity.Common => 120,
            WeaponRarity.Uncommon => 50,
            WeaponRarity.Rare => 24,
            WeaponRarity.Epic => 5,
            WeaponRarity.Legendary => legendaryEvent ? 1 : 0,
            _ => 0,
        };

        /// <summary>Weights of the RARE-or-better slot of the 11-draw.</summary>
        public static int GuaranteedWeight(WeaponRarity r, bool legendaryEvent) => r switch
        {
            WeaponRarity.Rare => 24,
            WeaponRarity.Epic => 5,
            WeaponRarity.Legendary => legendaryEvent ? 1 : 0,
            _ => 0,
        };

        public static int CategoryWeight(ItemCategory c) => c switch
        {
            ItemCategory.Weapon => 20,
            ItemCategory.Skin => 50,
            _ => 30,
        };

        public static int Weight(WeaponRarity r, bool guaranteed, bool legendaryEvent) =>
            guaranteed ? GuaranteedWeight(r, legendaryEvent) : RarityWeight(r, legendaryEvent);

        /// <summary>Exact chance of a rarity in a normal slot or the guaranteed slot.</summary>
        public static Fraction RarityChance(WeaponRarity r, bool guaranteed, bool legendaryEvent)
        {
            long total = 0;
            foreach (var x in Rarities)
                total += Weight(x, guaranteed, legendaryEvent);
            return new Fraction(Weight(r, guaranteed, legendaryEvent), total);
        }

        /// <summary>Exact chance of a kind within a rarity, given the kinds that have items there (the others share out).</summary>
        public static Fraction CategoryChance(ItemCategory c, ICollection<ItemCategory> withItems)
        {
            if (!withItems.Contains(c))
                return new Fraction(0, 1);
            long total = 0;
            foreach (var x in withItems)
                total += CategoryWeight(x);
            return new Fraction(CategoryWeight(c), total);
        }

        public static int DuplicateMedals(WeaponRarity r) => r switch
        {
            WeaponRarity.Common => 1,
            WeaponRarity.Uncommon => 3,
            WeaponRarity.Rare => 10,
            WeaponRarity.Epic => 30,
            WeaponRarity.Legendary => 100,
            _ => 0,
        };

        public static int ExchangePrice(WeaponRarity r) => r switch
        {
            WeaponRarity.Common => 30,
            WeaponRarity.Uncommon => 100,
            WeaponRarity.Rare => 350,
            WeaponRarity.Epic => 1000,
            WeaponRarity.Legendary => 3500,
            _ => 0,
        };
    }

    /// <summary>A LEGENDARY event (D-056): its id, period (UTC) and the LEGENDARY items it brings (first run or rerun).</summary>
    [Serializable]
    public sealed class GachaEvent
    {
        public string eventId;
        public string startUtc;
        public string endUtc;
        public bool rerun;
        public List<string> targetItemIds = new List<string>();

        public DateTime Start => Parse(startUtc);
        public DateTime End => Parse(endUtc);

        public bool IsActiveAt(DateTime utc) => utc >= Start && utc < End;

        static DateTime Parse(string s) =>
            DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    /// <summary>
    /// Where the time that decides an event comes from. Official online play needs a trusted one (the server's); the
    /// device clock is not trusted, so with it no event is ever active (except a development simulation).
    /// </summary>
    public interface IGachaClock
    {
        DateTime UtcNow { get; }
        bool Trusted { get; }
    }

    public sealed class DeviceClock : IGachaClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public bool Trusted => false;
    }

    /// <summary>A clock with a fixed time (tests; later the server's time).</summary>
    public sealed class FixedClock : IGachaClock
    {
        public DateTime UtcNow { get; set; }
        public bool Trusted { get; set; } = true;

        public FixedClock(DateTime utc, bool trusted = true)
        {
            UtcNow = utc;
            Trusted = trusted;
        }
    }

    /// <summary>The event schedule. Official events: none yet.</summary>
    public sealed class EventSchedule
    {
        readonly List<GachaEvent> events;

        public EventSchedule(IEnumerable<GachaEvent> list) => events = new List<GachaEvent>(list ?? Array.Empty<GachaEvent>());

        public IReadOnlyList<GachaEvent> Events => events;

        /// <summary>The event running now, or null. Never decided by an untrusted clock.</summary>
        public GachaEvent Active(IGachaClock clock)
        {
            if (clock == null || !clock.Trusted)
                return null;
            DateTime now = clock.UtcNow;
            foreach (var e in events)
                if (e.IsActiveAt(now))
                    return e;
            return null;
        }
    }
}
