using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Audio
{
    /// <summary>
    /// The local player's own sounds (Stage 39), added by <see cref="GameAudio"/> to the body that has the scene's
    /// listener. It only listens to the player's existing events and state (shots, dry fire, reload, weapon change,
    /// knife, posture, damage, death, ground contact, sprint): nothing of the player's rules changes. Footsteps follow
    /// the game's footstep events of the delayed spectating (<see cref="Match.MatchArena.StepLength"/> of walked
    /// distance on the ground, the same for every posture: the spec has no posture footstep rule).
    /// </summary>
    public sealed class PlayerAudioBinder : MonoBehaviour
    {
        /// <summary>A landing after at least this long in the air is heard.</summary>
        public const float LandAfterAirSeconds = 0.25f;
        /// <summary>Leaving the ground faster than this upward is a jump (not a step off an edge).</summary>
        public const float JumpUpSpeed = 1f;
        /// <summary>Damage sounds no closer together than this (a shotgun's pellets are one hit to the ear).</summary>
        public const float HurtInterval = 0.12f;

        FirstPersonMotor motor;
        PostureController posture;
        GunController gun;
        MeleeWeapon melee;
        WeaponSwitcher weapons;
        PlayerHealth health;
        Vector3 lastStepAt;
        bool wasGrounded = true, wasSprinting, magazineSwapped;
        float airTime, lastHurt = -1f;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();
            posture = GetComponent<PostureController>();
            gun = GetComponent<GunController>();
            melee = GetComponent<MeleeWeapon>();
            weapons = GetComponent<WeaponSwitcher>();
            health = GetComponent<PlayerHealth>();
            lastStepAt = Flat(transform.position);
        }

        void OnEnable()
        {
            if (gun != null)
            {
                gun.Fired += OnFired;
                gun.DryFired += OnDryFired;
                gun.ReloadStarted += OnReloadStarted;
                gun.ReloadFinished += OnReloadFinished;
                gun.ReloadCanceled += OnReloadCanceled;
            }
            if (melee != null)
                melee.SwingStarted += OnSwing;
            if (weapons != null)
            {
                weapons.SwitchStarted += OnSwitch;
                weapons.GunSwapStarted += OnGunSwap;
            }
            if (posture != null)
                posture.Changed += OnPosture;
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
            }
        }

        void OnDisable()
        {
            if (gun != null)
            {
                gun.Fired -= OnFired;
                gun.DryFired -= OnDryFired;
                gun.ReloadStarted -= OnReloadStarted;
                gun.ReloadFinished -= OnReloadFinished;
                gun.ReloadCanceled -= OnReloadCanceled;
            }
            if (melee != null)
                melee.SwingStarted -= OnSwing;
            if (weapons != null)
            {
                weapons.SwitchStarted -= OnSwitch;
                weapons.GunSwapStarted -= OnGunSwap;
            }
            if (posture != null)
                posture.Changed -= OnPosture;
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
            }
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        bool Alive => health == null || !health.IsDead;

        void OnFired() => GameAudio.Play(SoundCatalog.Fire(gun.Definition != null ? gun.Definition.weaponId : null));

        void OnDryFired() => GameAudio.Play(SoundCatalog.DryFire);

        void OnReloadStarted()
        {
            magazineSwapped = false;
            GameAudio.Play(SoundCatalog.ReloadStart);
        }

        void OnReloadFinished() => GameAudio.Play(SoundCatalog.ReloadEnd);

        void OnReloadCanceled() => magazineSwapped = false;

        void OnSwing() => GameAudio.Play(SoundCatalog.KnifeSwing);

        void OnSwitch(WeaponSlot slot) => GameAudio.Play(SoundCatalog.Switch);

        void OnGunSwap(LoadoutSlot slot) => GameAudio.Play(SoundCatalog.Switch);

        void OnPosture(Posture p)
        {
            string id = p switch
            {
                Posture.Sliding => SoundCatalog.Slide,
                Posture.Crouching => SoundCatalog.Crouch,
                Posture.Prone => SoundCatalog.Prone,
                _ => null,
            };
            if (id != null)
                GameAudio.Play(id);
        }

        void OnDamaged(float amount, Vector3 source)
        {
            if (Time.unscaledTime - lastHurt < HurtInterval || !Alive)
                return;
            lastHurt = Time.unscaledTime;
            GameAudio.Play(SoundCatalog.Hurt);
        }

        void OnDied() => GameAudio.Play(SoundCatalog.Death);

        void Update()
        {
            // The magazine change: the middle of the reload (the view lowers the gun most there).
            if (gun != null && gun.IsReloading && !magazineSwapped && gun.ReloadProgress >= 0.5f)
            {
                magazineSwapped = true;
                GameAudio.Play(SoundCatalog.ReloadMagazine);
            }
            if (motor == null || !Alive)
                return;
            bool grounded = motor.IsGrounded;
            var here = Flat(transform.position);
            if (grounded)
            {
                if (!wasGrounded && airTime >= LandAfterAirSeconds)
                    GameAudio.Play(SoundCatalog.Land);
                airTime = 0f;
                if ((here - lastStepAt).magnitude >= Match.MatchArena.StepLength)
                {
                    lastStepAt = here;
                    GameAudio.Play(SoundCatalog.Footstep);
                }
            }
            else
            {
                if (wasGrounded && motor.Velocity.y > JumpUpSpeed)
                    GameAudio.Play(SoundCatalog.Jump);
                airTime += Time.deltaTime;
                lastStepAt = here; // steps count again from the landing
            }
            if (motor.IsSprinting && !wasSprinting)
                GameAudio.Play(SoundCatalog.Dash);
            wasSprinting = motor.IsSprinting;
            wasGrounded = grounded;
        }
    }
}
