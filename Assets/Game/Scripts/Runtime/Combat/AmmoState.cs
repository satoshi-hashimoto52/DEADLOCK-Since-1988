using System;

namespace Deadlock.Combat
{
    /// <summary>
    /// Loaded / reserve ammunition of one gun during a match.
    /// Plain C# state with no Unity dependencies so it can later be owned by an authoritative server.
    /// </summary>
    public sealed class AmmoState
    {
        public int MagazineSize { get; }
        public int ReserveMax { get; }
        public int Loaded { get; private set; }
        public int Reserve { get; private set; }
        /// <summary>
        /// Reserve never runs out (the firing range only): a reload still takes its time and fills the magazine, but
        /// takes nothing from <see cref="Reserve"/>. Off by default (every match mode).
        /// </summary>
        public bool InfiniteReserve { get; set; }

        public bool CanReload => Loaded < MagazineSize && (InfiniteReserve || Reserve > 0);
        public bool IsReserveFull => Reserve >= ReserveMax;
        public bool IsCompletelyEmpty => Loaded == 0 && Reserve == 0 && !InfiniteReserve;

        public AmmoState(int magazineSize, int reserveMax, int startLoaded, int startReserve)
        {
            if (magazineSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(magazineSize), "Magazine size must be positive.");

            MagazineSize = magazineSize;
            ReserveMax = Math.Max(0, reserveMax);
            Loaded = Math.Clamp(startLoaded, 0, MagazineSize);
            Reserve = Math.Clamp(startReserve, 0, ReserveMax);
        }

        /// <summary>Consumes one loaded round. Returns false (and changes nothing) when the magazine is empty.</summary>
        public bool TryConsume()
        {
            if (Loaded <= 0)
                return false;
            Loaded--;
            return true;
        }

        /// <summary>
        /// Prototype rule (provisional): tops the magazine up from reserve without discarding the rounds still loaded.
        /// Returns the number of rounds moved from reserve to the magazine.
        /// </summary>
        public int Reload()
        {
            int moved = InfiniteReserve ? MagazineSize - Loaded : Math.Min(MagazineSize - Loaded, Reserve);
            if (moved <= 0)
                return 0;
            Loaded += moved;
            if (!InfiniteReserve)
                Reserve -= moved;
            return moved;
        }

        /// <summary>Overwrites the counts with authoritative values (e.g. from the host in an online match).</summary>
        public void Set(int loaded, int reserve)
        {
            Loaded = Math.Clamp(loaded, 0, MagazineSize);
            Reserve = Math.Clamp(reserve, 0, ReserveMax);
        }

        /// <summary>Adds picked-up rounds to reserve, clamped to <see cref="ReserveMax"/>. Returns rounds actually added.</summary>
        public int AddReserve(int amount)
        {
            if (amount <= 0)
                return 0;
            int added = Math.Min(amount, ReserveMax - Reserve);
            Reserve += added;
            return added;
        }
    }
}
