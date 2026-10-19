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

        public WeaponDefinition Definition => definition;
        public AmmoState Ammo => ammo;
        public bool IsReloading { get; private set; }
        public float ReloadProgress => IsReloading ? Mathf.Clamp01((Time.time - reloadStartTime) / definition.reloadTime) : 0f;
        /// <summary>0 = hip, 1 = fully aimed.</summary>
        public float AdsBlend { get; private set; }
        public bool AdsRequested { get; private set; }
        public float CurrentSpreadDegrees => Mathf.Lerp(definition.hipSpreadDegrees, definition.adsSpreadDegrees, AdsBlend);

        public event Action Fired;
        public event Action DryFired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action AmmoChanged;

        void Awake()
        {
            ammo = new AmmoState(definition.magazineSize, definition.reserveMax, definition.magazineSize, definition.startReserve);
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            float now = Time.time;

            if (IsReloading && now - reloadStartTime >= definition.reloadTime)
                FinishReload();

            AdsRequested = frame.AdsActive;
            AdsBlend = Mathf.MoveTowards(AdsBlend, AdsRequested ? 1f : 0f, Time.deltaTime / definition.adsTime);

            if (frame.ReloadPressed)
                TryStartReload();

            bool trigger = definition.automatic ? frame.FireHeld : frame.FirePressed;
            if (trigger)
                TryFire(now, frame.FirePressed);

            // Applied by the motor next frame.
            bool recentlyFired = now - lastFireTime < 0.3f;
            motor.SprintBlocked = AdsRequested || frame.FireHeld || recentlyFired;
            motor.MoveSpeedMultiplier = Mathf.Lerp(1f, definition.adsMoveSpeedMultiplier, AdsBlend);
            motor.LookSensitivityMultiplier = Mathf.Lerp(1f, definition.adsLookSensitivityMultiplier, AdsBlend);
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
            if (IsReloading || !ammo.CanReload)
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
