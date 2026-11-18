using System;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Combat state of the equipped gun: fire timing, ammo, reload and ADS. Has no rendering code;
    /// the view (weapon sprites, HUD) observes the public state and events. The guns carried (Slot 1 main gun, Slot 2
    /// handgun, D-052) and the ammunition of each are kept in <see cref="Inventory"/>; the gun in hand is
    /// <see cref="ActiveSlot"/>. A scene starts with its gun in its slot and nothing else.
    /// </summary>
    public sealed class GunController : MonoBehaviour
    {
        [SerializeField] WeaponDefinition definition;
        [SerializeField] PlayerInputRouter input;
        [SerializeField] FirstPersonMotor motor;
        [Tooltip("Projectiles start here and travel along its forward (the player camera).")]
        [SerializeField] Transform aimOrigin;
        [SerializeField] ProjectileSystem projectiles;

        readonly GunInventory inventory = new GunInventory();
        AmmoState ammo;
        float nextFireTime;
        float reloadStartTime;
        float lastFireTime = -10f;
        bool waitForTriggerRelease;
        int shotIndex;

        public WeaponDefinition Definition => definition;
        public AmmoState Ammo => ammo;
        /// <summary>The carried guns and the ammunition of each (every gun keeps its own rounds, D-054).</summary>
        public GunInventory Inventory => inventory;
        public LoadoutSlot ActiveSlot { get; private set; } = LoadoutSlot.Primary;
        public bool IsReloading { get; private set; }
        /// <summary>False while the melee weapon is out or a switch is in progress (set by <see cref="WeaponSwitcher"/>).</summary>
        public bool IsEquipped { get; private set; } = true;
        public float ReloadProgress => IsReloading ? Mathf.Clamp01((Time.time - reloadStartTime) / definition.reloadTime) : 0f;
        /// <summary>0 = hip, 1 = fully aimed.</summary>
        public float AdsBlend { get; private set; }
        public bool AdsRequested { get; private set; }
        public float CurrentSpreadDegrees => Mathf.Lerp(definition.hipSpreadDegrees, definition.adsSpreadDegrees, AdsBlend)
            * Mathf.Lerp(1f, definition.movingSpreadMultiplier, MoveFactor);

        /// <summary>0 = standing still, 1 = walking speed or faster (scales <see cref="WeaponDefinition.movingSpreadMultiplier"/>).</summary>
        float MoveFactor => motor == null || motor.WalkSpeed <= 0f ? 0f : Mathf.Clamp01(motor.PlanarSpeed / motor.WalkSpeed);

        public event Action Fired;
        public event Action DryFired;
        public event Action ReloadStarted;
        public event Action ReloadFinished;
        public event Action ReloadCanceled;
        public event Action AmmoChanged;
        /// <summary>Kick of the last shot (x = pitch up, y = yaw right, degrees).</summary>
        public Vector2 LastRecoil { get; private set; }
        /// <summary>A longer pause than this (or two fire intervals) between shots starts the recoil pattern again.</summary>
        public const float ShotSequenceGap = 0.5f;
        /// <summary>Shot number since firing started (0 = first; back to 0 after a pause or reload); drives alternating recoil.</summary>
        public int ShotIndex => shotIndex;

        /// <summary>The gun was swapped (<see cref="SetDefinition"/>): views re-read the definition's look.</summary>
        public event Action DefinitionChanged;

        /// <summary>
        /// When set (online), a shot calls this with (origin, direction) instead of firing a local damaging projectile:
        /// the host decides whether the shot happens and what it hits. Ammo, recoil and effects stay local predictions.
        /// </summary>
        public Action<Vector3, Vector3> ShotOverride { get; set; }

        void Awake()
        {
            ActiveSlot = WeaponSlotRules.SlotFor(definition.category);
            inventory.Replace(ActiveSlot, definition, GunInventory.Fresh(definition, infiniteReserve));
            ammo = inventory.Ammo(ActiveSlot);
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
            if (!equipped)
                input.CancelAds(); // never resumes by itself after switching back: a new press is needed
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
            input.AdsAvailable = IsEquipped;

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
            if (now - lastFireTime > Mathf.Max(ShotSequenceGap, definition.fireInterval * 2f))
                shotIndex = 0; // a pause in firing starts the recoil pattern again

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
            motor.MoveSpeedMultiplier = Mathf.Lerp(1f, definition.adsMoveSpeedMultiplier, AdsBlend) * definition.moveSpeedMultiplier;
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

        /// <summary>Back to fresh guns (respawn): every carried gun has a full magazine and its starting reserve; no reload, no ADS.</summary>
        public void ResetState()
        {
            inventory.ResetAll();
            ammo = inventory.Ammo(ActiveSlot);
            IsReloading = false;
            AdsBlend = 0f;
            nextFireTime = 0f;
            shotIndex = 0;
            waitForTriggerRelease = true;
            AmmoChanged?.Invoke();
        }

        /// <summary>
        /// Reserve ammo never runs out (set by <see cref="FiringRangeAmmo"/> in the firing range only). Magazine size,
        /// reloading and its time stay as the weapon defines them; it carries over to a swapped gun of the same scene.
        /// </summary>
        public bool InfiniteReserve
        {
            get => infiniteReserve;
            set
            {
                infiniteReserve = value;
                inventory.SetInfiniteReserve(value);
                AmmoChanged?.Invoke();
            }
        }

        bool infiniteReserve;

        /// <summary>The next shot is the first of the recoil pattern again (as after a pause; nothing else changes).</summary>
        public void ResetShotSequence() => shotIndex = 0;

        /// <summary>
        /// Swaps the gun (development test choice, <see cref="DevGunSelection"/>): the new definition goes in its own slot
        /// and into the hand, and starts fresh — full magazine and starting reserve, no reload, no ADS, trigger released
        /// first. Same rig, same everything else.
        /// </summary>
        public void SetDefinition(WeaponDefinition next)
        {
            if (next == null || next == definition)
                return;
            var slot = WeaponSlotRules.SlotFor(next.category);
            inventory.Replace(slot, next, GunInventory.Fresh(next, infiniteReserve));
            Draw(slot);
        }

        /// <summary>
        /// Puts a gun in a loadout slot with fresh ammunition (D-052: a handgun only in Slot 2, the other guns only in
        /// Slot 1). Refused (false, nothing changes) for a slot the gun may not use. Equipping the slot in hand draws it.
        /// </summary>
        public bool EquipGun(LoadoutSlot slot, WeaponDefinition gun)
        {
            if (!inventory.TryEquip(slot, gun, infiniteReserve))
                return false;
            if (slot == ActiveSlot)
                Draw(slot, force: true);
            return true;
        }

        /// <summary>
        /// Takes another carried gun in hand. Each gun keeps its own magazine and reserve; a reload in progress is
        /// canceled without moving rounds (as when putting the gun away). False when no gun is in that slot.
        /// </summary>
        public bool SelectGun(LoadoutSlot slot)
        {
            if (!inventory.Has(slot))
                return false;
            if (slot != ActiveSlot)
                Draw(slot);
            return true;
        }

        void Draw(LoadoutSlot slot, bool force = false)
        {
            var next = inventory.Gun(slot);
            if (!force && slot == ActiveSlot && next == definition)
                return;
            if (IsReloading)
            {
                IsReloading = false;
                ReloadCanceled?.Invoke();
            }
            ActiveSlot = slot;
            definition = next;
            ammo = inventory.Ammo(slot);
            AdsBlend = 0f;
            nextFireTime = 0f;
            shotIndex = 0;
            waitForTriggerRelease = true;
            input.CancelAds();
            AmmoChanged?.Invoke();
            DefinitionChanged?.Invoke();
        }

        /// <summary>
        /// Swaps in other values for the gun in hand (the weapon lab's test values, or back to the official ones): the
        /// state carries over — shot number of the recoil pattern, fire timing, ADS, a reload in progress, the held
        /// trigger, and the rounds (cut to the new magazine and reserve). The aim and its recoil are the motor's and stay.
        /// </summary>
        public void SetDefinitionKeepingState(WeaponDefinition next)
        {
            if (next == null || next == definition)
                return;
            definition = next;
            ammo = new AmmoState(definition.magazineSize, definition.reserveMax, Mathf.Min(ammo.Loaded, definition.magazineSize),
                Mathf.Min(ammo.Reserve, definition.reserveMax)) { InfiniteReserve = infiniteReserve };
            inventory.Replace(ActiveSlot, definition, ammo);
            AmmoChanged?.Invoke();
            DefinitionChanged?.Invoke();
        }

        /// <summary>Adds rounds to the reserve of the gun in hand. Returns the amount actually added.</summary>
        public int AddReserveAmmo(int amount)
        {
            int added = ammo.AddReserve(amount);
            if (added > 0)
                AmmoChanged?.Invoke();
            return added;
        }

        /// <summary>
        /// One ammo box (D-054): every carried gun gets one of its own magazines into reserve, capped at its maximum; the
        /// magazines are not filled. Returns the rounds added over all guns (0: every reserve was already full).
        /// </summary>
        public int ApplyAmmoBox()
        {
            int added = inventory.ApplyAmmoBox();
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
            shotIndex = 0;
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

            float spread = CurrentSpreadDegrees;
            if (ShotOverride != null)
                ShotOverride(aimOrigin.position, ApplySpread(aimOrigin.forward, spread)); // online: one projectile per shot (the host's rule)
            else
                // One round, several projectiles for a shotgun: each has its own spread and hits on its own.
                for (int i = 0; i < Mathf.Max(1, definition.projectilesPerShot); i++)
                    projectiles.Fire(aimOrigin.position, ApplySpread(aimOrigin.forward, spread), definition.projectileSpeed, definition.maxRange,
                        definition.damage, gameObject, definition.damageFalloff);

            // An enabled, valid recoil profile sets direction and strength (no randomness); otherwise the earlier recoil.
            float pitch, yaw;
            if (definition.recoilProfile == null || !definition.recoilProfile.enabled
                || !RecoilCalculator.TryCompute(definition.recoilProfile, shotIndex, AdsBlend, out pitch, out yaw))
            {
                float recoilScale = Mathf.Lerp(1f, definition.adsRecoilMultiplier, AdsBlend);
                pitch = definition.recoilPitchDegrees * recoilScale;
                yaw = UnityEngine.Random.Range(-1f, 1f) * definition.recoilYawDegrees * recoilScale;
            }
            if (definition.recoilProfile != null && definition.recoilProfile.enabled && definition.recoilProfile.IsValid(out _))
            {
                motor.RecoverySpeedOverride = definition.recoilProfile.recoverySpeed;
                motor.AddRecoil(pitch, yaw, definition.recoilProfile.recoveryFraction);
            }
            else
            {
                motor.RecoverySpeedOverride = null; // the earlier recovery (half, at the scene's speed)
                motor.AddRecoil(pitch, yaw);
            }
            LastRecoil = new Vector2(pitch, yaw);
            shotIndex++;

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
