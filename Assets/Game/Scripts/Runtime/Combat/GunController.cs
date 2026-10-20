using System;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Combat state of the equipped gun: fire timing, ammo, reload and ADS. Has no rendering code;
    /// the view (weapon sprites, HUD) observes the public state and events.
    /// </summary>
    public sealed class GunController : MonoBehaviour
    {
        [SerializeField] WeaponDefinition definition;
        [SerializeField] PlayerInputRouter input;
        [SerializeField] FirstPersonMotor motor;
        [Tooltip("Projectiles start here and travel along its forward (the player camera).")]
        [SerializeField] Transform aimOrigin;
        [SerializeField] ProjectileSystem projectiles;

        AmmoState ammo;
        float nextFireTime;
        float reloadStartTime;
        float lastFireTime = -10f;
        bool waitForTriggerRelease;

        public WeaponDefinition Definition => definition;
        public AmmoState Ammo => ammo;
        public bool IsReloading { get; private set; }
        /// <summary>False while the melee weapon is out or a switch is in progress (set by <see cref="WeaponSwitcher"/>).</summary>
        public bool IsEquipped { get; private set; } = true;
        public float ReloadProgress => IsReloading ? Mathf.Clamp01((Time.time - reloadStartTime) / definition.reloadTime) : 0f;
        /// <summary>0 = hip, 1 = fully aimed.</summary>
        public float AdsBlend { get; private set; }
        public bool AdsRequested { get; private set; }
        public float CurrentSpreadDegrees => Mathf.Lerp(definition.hipSpreadDegrees, definition.adsSpreadDegrees, AdsBlend);

        public event Action Fired;
        public event Action DryFired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action ReloadCanceled;
        public event Action AmmoChanged;

        /// <summary>
        /// When set (online), a shot calls this with (origin, direction) instead of firing a local damaging projectile:
        /// the host decides whether the shot happens and what it hits. Ammo, recoil and effects stay local predictions.
        /// </summary>
        public Action<Vector3, Vector3> ShotOverride { get; set; }

        void Awake()
        {
            ammo = new AmmoState(definition.magazineSize, definition.reserveMax, definition.magazineSize, definition.startReserve);
        }

        /// <summary>
        /// Putting the gun away cancels a reload in progress: no rounds are moved, and the reload is not resumed when the
        /// gun comes back (provisional rule). After drawing, the trigger must be released before the gun fires again.
        /// </summary>
        public void SetEquipped(bool equipped)
        {
            if (equipped == IsEquipped)
                return;
            IsEquipped = equipped;
            if (!equipped && IsReloading)
            {
                IsReloading = false;
                ReloadCanceled?.Invoke();
            }
            if (equipped)
                waitForTriggerRelease = true;
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            float now = Time.time;

            if (!IsEquipped)
            {
                AdsRequested = false;
                AdsBlend = Mathf.MoveTowards(AdsBlend, 0f, Time.deltaTime / definition.adsTime);
                motor.SprintBlocked = false;
                motor.MoveSpeedMultiplier = Mathf.Lerp(1f, definition.adsMoveSpeedMultiplier, AdsBlend);
                motor.LookSensitivityMultiplier = Mathf.Lerp(1f, definition.adsLookSensitivityMultiplier, AdsBlend);
                return;
            }

            if (IsReloading && now - reloadStartTime >= definition.reloadTime)
                FinishReload();

            if (waitForTriggerRelease && !frame.FireHeld)
                waitForTriggerRelease = false;

            AdsRequested = frame.AdsActive;
            AdsBlend = Mathf.MoveTowards(AdsBlend, AdsRequested ? 1f : 0f, Time.deltaTime / definition.adsTime);

            if (frame.ReloadPressed)
                TryStartReload();

            bool trigger = definition.automatic ? frame.FireHeld : frame.FirePressed;
            if (trigger && !waitForTriggerRelease)
                TryFire(now, frame.FirePressed);

            // Applied by the motor next frame.
            bool recentlyFired = now - lastFireTime < 0.3f;
            motor.SprintBlocked = AdsRequested || frame.FireHeld || recentlyFired;
            motor.MoveSpeedMultiplier = Mathf.Lerp(1f, definition.adsMoveSpeedMultiplier, AdsBlend);
            motor.LookSensitivityMultiplier = Mathf.Lerp(1f, definition.adsLookSensitivityMultiplier, AdsBlend);
        }

        /// <summary>Applies authoritative ammo counts (online: from the host).</summary>
        public void SetAmmo(int loaded, int reserve)
        {
            if (ammo.Loaded == loaded && ammo.Reserve == reserve)
                return;
            ammo.Set(loaded, reserve);
            AmmoChanged?.Invoke();
        }

        /// <summary>Back to a fresh gun (respawn): full magazine, starting reserve, no reload, no ADS.</summary>
        public void ResetState()
        {
            ammo.Set(definition.magazineSize, definition.startReserve);
            IsReloading = false;
            AdsBlend = 0f;
            nextFireTime = 0f;
            waitForTriggerRelease = true;
            AmmoChanged?.Invoke();
        }

        /// <summary>Adds rounds to reserve (pickups). Returns the amount actually added.</summary>
        public int AddReserveAmmo(int amount)
        {
            int added = ammo.AddReserve(amount);
            if (added > 0)
                AmmoChanged?.Invoke();
            return added;
        }

        public bool TryStartReload()
        {
            if (!IsEquipped || IsReloading || !ammo.CanReload)
                return false;
            IsReloading = true;
            reloadStartTime = Time.time;
            ReloadStarted?.Invoke();
            return true;
        }

        void FinishReload()
        {
            IsReloading = false;
            ammo.Reload();
            AmmoChanged?.Invoke();
            ReloadFinished?.Invoke();
        }

        void TryFire(float now, bool pressedThisFrame)
        {
            if (IsReloading || now < nextFireTime)
                return;

            if (!ammo.TryConsume())
            {
                if (pressedThisFrame)
                {
                    DryFired?.Invoke();
                    TryStartReload();
                }
                return;
            }

            // Keep a steady cadence while the trigger is held, without banking shots after a pause.
            nextFireTime = (now - nextFireTime <= Time.deltaTime ? nextFireTime : now) + definition.fireInterval;
            lastFireTime = now;

            Vector3 direction = ApplySpread(aimOrigin.forward, CurrentSpreadDegrees);
            if (ShotOverride != null)
                ShotOverride(aimOrigin.position, direction);
            else
                projectiles.Fire(aimOrigin.position, direction, definition.projectileSpeed, definition.maxRange, definition.damage, gameObject);

            float recoilScale = Mathf.Lerp(1f, definition.adsRecoilMultiplier, AdsBlend);
            motor.AddRecoil(
                definition.recoilPitchDegrees * recoilScale,
                UnityEngine.Random.Range(-1f, 1f) * definition.recoilYawDegrees * recoilScale);

            AmmoChanged?.Invoke();
            Fired?.Invoke();
        }

        Vector3 ApplySpread(Vector3 forward, float spreadDegrees)
        {
            if (spreadDegrees <= 0f)
                return forward;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            return (forward + aimOrigin.right * offset.x + aimOrigin.up * offset.y).normalized;
        }
    }
}
