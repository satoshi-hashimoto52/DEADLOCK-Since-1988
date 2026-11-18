using System;
using System.Collections.Generic;

namespace Deadlock.Combat
{
    /// <summary>Kind of gun. Decides the loadout slot it may go in (<see cref="WeaponSlotRules"/>, D-052).</summary>
    public enum WeaponCategory
    {
        Rifle,
        Smg,
        Shotgun,
        Lmg,
        Handgun,
    }

    /// <summary>The three loadout slots (D-052): Slot 1 main gun, Slot 2 sub gun (handgun only), Slot 3 knife.</summary>
    public enum LoadoutSlot
    {
        Primary = 1,
        Secondary = 2,
        Knife = 3,
    }

    /// <summary>
    /// Ammunition type of a gun (D-054). Only a label for now: the reserve belongs to each gun and is never shared, even
    /// between guns of the same type. The concrete types are provisional (DECISIONS Q-012).
    /// </summary>
    public enum AmmoType
    {
        Unassigned,
        Rifle,
        Pistol,
        Shotgun,
        Heavy,
    }

    /// <summary>Which slot a gun may be equipped in (D-052). Data driven by <see cref="WeaponDefinition.category"/>.</summary>
    public static class WeaponSlotRules
    {
        public static LoadoutSlot SlotFor(WeaponCategory category) =>
            category == WeaponCategory.Handgun ? LoadoutSlot.Secondary : LoadoutSlot.Primary;

        public static bool CanEquip(WeaponDefinition gun, LoadoutSlot slot) =>
            gun != null && slot != LoadoutSlot.Knife && SlotFor(gun.category) == slot;
    }

    /// <summary>
    /// Reserve ammunition rule (D-054): a match starts with a full magazine and two magazines in reserve; the reserve holds
    /// at most three magazines; an ammo box gives every equipped gun one magazine (capped). Counted in each gun's own
    /// magazine size.
    /// </summary>
    public static class AmmoRules
    {
        public const int StartReserveMagazines = 2;
        public const int ReserveMaxMagazines = 3;
        public const int AmmoBoxMagazines = 1;

        public static int StartReserve(int magazineSize) => magazineSize * StartReserveMagazines;
        public static int ReserveMax(int magazineSize) => magazineSize * ReserveMaxMagazines;
        public static int AmmoBoxRounds(int magazineSize) => magazineSize * AmmoBoxMagazines;

        /// <summary>The definition's reserve values follow the rule.</summary>
        public static bool Follows(WeaponDefinition gun) =>
            gun.startReserve == StartReserve(gun.magazineSize) && gun.reserveMax == ReserveMax(gun.magazineSize);

        /// <summary>Writes the rule's reserve values into a definition (editor setup of the weapon assets).</summary>
        public static void Apply(WeaponDefinition gun)
        {
            gun.startReserve = StartReserve(gun.magazineSize);
            gun.reserveMax = ReserveMax(gun.magazineSize);
        }
    }

    /// <summary>
    /// A checked set of equipment (D-052): a main gun in Slot 1, an optional handgun in Slot 2, a knife in Slot 3.
    /// Created only through <see cref="TryCreate"/>, so an invalid combination never exists.
    /// </summary>
    public sealed class Loadout
    {
        public WeaponDefinition Primary { get; }
        public WeaponDefinition Secondary { get; }
        public MeleeDefinition Knife { get; }

        Loadout(WeaponDefinition primary, WeaponDefinition secondary, MeleeDefinition knife)
        {
            Primary = primary;
            Secondary = secondary;
            Knife = knife;
        }

        public WeaponDefinition GunIn(LoadoutSlot slot) => slot switch
        {
            LoadoutSlot.Primary => Primary,
            LoadoutSlot.Secondary => Secondary,
            _ => null,
        };

        /// <summary>Checks the combination. The main gun and the knife are required; Slot 2 may stay empty.</summary>
        public static bool TryCreate(WeaponDefinition primary, WeaponDefinition secondary, MeleeDefinition knife,
            out Loadout loadout, out string error)
        {
            loadout = null;
            error = Check(primary, secondary, knife);
            if (error != null)
                return false;
            loadout = new Loadout(primary, secondary, knife);
            return true;
        }

        static string Check(WeaponDefinition primary, WeaponDefinition secondary, MeleeDefinition knife)
        {
            if (primary == null)
                return "Slot 1 needs a main gun";
            if (!WeaponSlotRules.CanEquip(primary, LoadoutSlot.Primary))
                return $"{primary.weaponId} ({primary.category}) cannot go in Slot 1";
            if (secondary != null && !WeaponSlotRules.CanEquip(secondary, LoadoutSlot.Secondary))
                return $"{secondary.weaponId} ({secondary.category}) cannot go in Slot 2";
            if (knife == null)
                return "Slot 3 needs a knife";
            return null;
        }
    }

    /// <summary>
    /// The guns a player carries and the ammunition of each (D-054): every gun keeps its own magazine and reserve, so
    /// putting one away and drawing another never moves rounds between them. Plain C# so a host can own it later.
    /// </summary>
    public sealed class GunInventory
    {
        readonly Dictionary<LoadoutSlot, (WeaponDefinition gun, AmmoState ammo)> guns =
            new Dictionary<LoadoutSlot, (WeaponDefinition, AmmoState)>();

        public IEnumerable<LoadoutSlot> Slots => guns.Keys;
        public int Count => guns.Count;

        public bool Has(LoadoutSlot slot) => guns.ContainsKey(slot);
        public WeaponDefinition Gun(LoadoutSlot slot) => guns.TryGetValue(slot, out var g) ? g.gun : null;
        public AmmoState Ammo(LoadoutSlot slot) => guns.TryGetValue(slot, out var g) ? g.ammo : null;

        /// <summary>
        /// Puts a gun in its slot with fresh ammunition (full magazine, the definition's starting reserve). Refuses a gun
        /// that may not go in that slot.
        /// </summary>
        public bool TryEquip(LoadoutSlot slot, WeaponDefinition gun, bool infiniteReserve = false)
        {
            if (!WeaponSlotRules.CanEquip(gun, slot))
                return false;
            guns[slot] = (gun, Fresh(gun, infiniteReserve));
            return true;
        }

        /// <summary>Replaces the gun's state kept for a slot (the gun in hand swapped its values, keeping its rounds).</summary>
        internal void Replace(LoadoutSlot slot, WeaponDefinition gun, AmmoState ammo) => guns[slot] = (gun, ammo);

        public void Remove(LoadoutSlot slot) => guns.Remove(slot);

        /// <summary>Every gun back to a fresh state (respawn, retry).</summary>
        public void ResetAll()
        {
            foreach (var slot in new List<LoadoutSlot>(guns.Keys))
            {
                var (gun, ammo) = guns[slot];
                guns[slot] = (gun, Fresh(gun, ammo.InfiniteReserve));
            }
        }

        public void SetInfiniteReserve(bool value)
        {
            foreach (var (_, ammo) in guns.Values)
                ammo.InfiniteReserve = value;
        }

        /// <summary>
        /// One ammo box: every carried gun gets one of its own magazines into reserve, each capped at its own maximum.
        /// The magazines themselves are never filled. Returns the rounds added over all guns (0: every reserve was full).
        /// </summary>
        public int ApplyAmmoBox()
        {
            int added = 0;
            foreach (var (gun, ammo) in guns.Values)
                added += ammo.AddReserve(AmmoRules.AmmoBoxRounds(gun.magazineSize));
            return added;
        }

        public static AmmoState Fresh(WeaponDefinition gun, bool infiniteReserve = false) =>
            new AmmoState(gun.magazineSize, gun.reserveMax, gun.magazineSize, gun.startReserve) { InfiniteReserve = infiniteReserve };
    }
}
