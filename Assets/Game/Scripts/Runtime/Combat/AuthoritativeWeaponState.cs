using System;

namespace Deadlock.Combat
{
    public enum ShotRejection
    {
        None,
        Dead,
        /// <summary>The gun is not out (knife equipped or a switch in progress).</summary>
        NotEquipped,
        Reloading,
        NoAmmo,
        /// <summary>Faster than the fire interval allows.</summary>
        TooFast,
    }

    /// <summary>
    /// Weapon state of one player as the host sees it in an online match: which weapon is out, switching, ammo,
    /// reload, fire timing, melee timing and alive / dead. Every request from a player is checked here before
    /// anything happens; the player's own copy is only a prediction. Plain C# (times are passed in) so the rules can
    /// be tested without a network.
    /// </summary>
    public sealed class AuthoritativeWeaponState
    {
        /// <summary>
        /// Requests arrive with network jitter: a shot may come this much earlier than the exact interval. Idle time
        /// earns no credit (the next allowed time is a full interval after the later of the previous slot and now),
        /// so two shots can never be accepted at the same moment and the average rate cannot exceed the fire rate.
        /// </summary>
        public const float FireTimingTolerance = 0.05f;

        readonly float fireInterval;
        readonly float reloadTime;
        readonly float switchTime;
        readonly float meleeTotalTime;
        readonly int magazineSize;
        readonly int startReserve;
        float nextFireTime;
        float reloadStart;
        float switchStart;
        float nextSwingTime;

        public AmmoState Ammo { get; }
        public WeaponSlot Slot { get; private set; } = WeaponSlot.Gun;
        public bool IsSwitching { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsDead { get; set; }

        public AuthoritativeWeaponState(WeaponDefinition gun, MeleeDefinition melee, float switchTime)
            : this(gun.magazineSize, gun.reserveMax, gun.startReserve, gun.fireInterval, gun.reloadTime, switchTime, melee.TotalTime)
        {
        }

        public AuthoritativeWeaponState(int magazineSize, int reserveMax, int startReserve, float fireInterval, float reloadTime,
            float switchTime, float meleeTotalTime)
        {
            this.magazineSize = magazineSize;
            this.startReserve = startReserve;
            this.fireInterval = fireInterval;
            this.reloadTime = reloadTime;
            this.switchTime = switchTime;
            this.meleeTotalTime = meleeTotalTime;
            Ammo = new AmmoState(magazineSize, reserveMax, magazineSize, startReserve);
        }

        /// <summary>Advances timed actions (reload, weapon switch) to <paramref name="now"/>.</summary>
        public void Tick(float now)
        {
            if (IsReloading && now - reloadStart >= reloadTime)
            {
                IsReloading = false;
                Ammo.Reload();
            }
            if (IsSwitching && now - switchStart >= switchTime)
                IsSwitching = false;
        }

        /// <summary>Checks and, if allowed, performs one shot (consumes a round).</summary>
        public ShotRejection TryFire(float now)
        {
            Tick(now);
            if (IsDead)
                return ShotRejection.Dead;
            if (Slot != WeaponSlot.Gun || IsSwitching)
                return ShotRejection.NotEquipped;
            if (IsReloading)
                return ShotRejection.Reloading;
            if (now < nextFireTime - FireTimingTolerance)
                return ShotRejection.TooFast;
            if (!Ammo.TryConsume())
                return ShotRejection.NoAmmo;
            nextFireTime = Math.Max(nextFireTime, now) + fireInterval;
            return ShotRejection.None;
        }

        public bool TryStartReload(float now)
        {
            Tick(now);
            if (IsDead || Slot != WeaponSlot.Gun || IsSwitching || IsReloading || !Ammo.CanReload)
                return false;
            IsReloading = true;
            reloadStart = now;
            return true;
        }

        /// <summary>Starts a switch. Putting the gun away cancels a reload without moving rounds (same rule as offline).</summary>
        public bool TrySwitch(WeaponSlot target, float now)
        {
            Tick(now);
            if (IsDead || IsSwitching || target == Slot || now < nextSwingTime - FireTimingTolerance)
                return false;
            IsReloading = false;
            Slot = target;
            IsSwitching = true;
            switchStart = now;
            return true;
        }

        /// <summary>Checks and, if allowed, starts one melee swing.</summary>
        public bool TrySwing(float now)
        {
            Tick(now);
            if (IsDead || Slot != WeaponSlot.Melee || IsSwitching || now < nextSwingTime - FireTimingTolerance)
                return false;
            nextSwingTime = Math.Max(nextSwingTime, now) + meleeTotalTime;
            return true;
        }

        /// <summary>Fresh state after a respawn: gun out, full magazine, starting reserve.</summary>
        public void Reset()
        {
            Ammo.Set(magazineSize, startReserve);
            Slot = WeaponSlot.Gun;
            IsSwitching = false;
            IsReloading = false;
            IsDead = false;
            nextFireTime = 0f;
            nextSwingTime = 0f;
        }
    }
}
