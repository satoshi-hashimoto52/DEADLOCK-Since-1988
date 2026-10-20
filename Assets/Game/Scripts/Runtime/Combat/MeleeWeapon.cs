using System;
using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Combat
{
    public enum MeleePhase
    {
        Idle,
        Windup,
        Active,
        Recovery,
    }

    /// <summary>
    /// Sub melee weapon (prototype knife). One swing per press: windup → active window → recovery; presses during a swing
    /// are ignored (no buffering), so mashing cannot beat the swing interval. During the active window a sphere is swept
    /// forward from the eye; the first thing it meets ends the swing. Walls block, only one target is damaged per swing,
    /// and a line-of-sight check stops hits around the edge of cover. No ammo.
    /// </summary>
    public sealed class MeleeWeapon : MonoBehaviour
    {
        [SerializeField] MeleeDefinition definition;
        [SerializeField] PlayerInputRouter input;
        [Tooltip("Swing origin and direction (the player camera).")]
        [SerializeField] Transform aimOrigin;
        [Tooltip("Everything except the player itself.")]
        [SerializeField] LayerMask hitMask = ~0;

        float swingStart = -10f;
        bool swingResolved;

        public MeleeDefinition Definition => definition;
        public bool IsEquipped { get; private set; }
        public MeleePhase Phase { get; private set; }
        /// <summary>0..1 within the current phase.</summary>
        public float PhaseProgress { get; private set; }
        public bool IsSwinging => Phase != MeleePhase.Idle;
        public int SwingCount { get; private set; }

        /// <summary>Off online: the swing is only shown locally and the host decides what it hits.</summary>
        public bool LocalHitDetection { get; set; } = true;

        public LayerMask HitMask
        {
            get => hitMask;
            set => hitMask = value;
        }

        public event Action SwingStarted;
        /// <summary>Raised once per swing that reached something: the result is Ignored for walls.</summary>
        public event Action<Collider, DamageResult> Hit;

        public void SetEquipped(bool equipped)
        {
            IsEquipped = equipped;
            if (!equipped)
            {
                Phase = MeleePhase.Idle;
                PhaseProgress = 0f;
            }
        }

        void Update()
        {
            if (!IsEquipped)
                return;

            float elapsed = Time.time - swingStart;
            if (Phase == MeleePhase.Idle)
            {
                if (input.Current.FirePressed)
                    StartSwing();
                return;
            }
            UpdatePhase(elapsed);
        }

        void StartSwing()
        {
            swingStart = Time.time;
            swingResolved = false;
            SwingCount++;
            Phase = definition.windup > 0f ? MeleePhase.Windup : MeleePhase.Active;
            PhaseProgress = 0f;
            SwingStarted?.Invoke();
            UpdatePhase(0f);
        }

        void UpdatePhase(float elapsed)
        {
            MeleeDefinition d = definition;
            if (elapsed < d.windup)
            {
                Phase = MeleePhase.Windup;
                PhaseProgress = d.windup > 0f ? elapsed / d.windup : 1f;
            }
            else if (elapsed < d.windup + d.activeTime)
            {
                Phase = MeleePhase.Active;
                PhaseProgress = (elapsed - d.windup) / d.activeTime;
                if (!swingResolved)
                    Sweep();
            }
            else if (elapsed < d.TotalTime)
            {
                // A swing whose active window fell between two frames still gets its check.
                if (!swingResolved)
                    Sweep();
                Phase = MeleePhase.Recovery;
                PhaseProgress = d.recovery > 0f ? (elapsed - d.windup - d.activeTime) / d.recovery : 1f;
            }
            else
            {
                if (!swingResolved)
                    Sweep();
                Phase = MeleePhase.Idle;
                PhaseProgress = 0f;
            }
        }

        void Sweep()
        {
            if (!LocalHitDetection)
            {
                swingResolved = true; // online: the host runs the check
                return;
            }
            var result = MeleeSweep.Run(aimOrigin.position, aimOrigin.forward, definition, hitMask, transform);
            if (!result.Resolved)
                return; // nothing yet: keep checking for the rest of the active window
            swingResolved = true;
            Hit?.Invoke(result.Collider, result.Damage);
        }

#if UNITY_EDITOR
        // Debug view of reach and width (Scene view, or Game view with Gizmos on). Never part of the game UI.
        void OnDrawGizmos()
        {
            if (definition == null || aimOrigin == null || !IsEquipped)
                return;
            Gizmos.color = Phase switch
            {
                MeleePhase.Windup => new Color(1f, 0.85f, 0.2f, 0.8f),
                MeleePhase.Active => new Color(1f, 0.2f, 0.15f, 0.9f),
                MeleePhase.Recovery => new Color(0.6f, 0.6f, 0.6f, 0.6f),
                _ => new Color(0.3f, 0.8f, 1f, 0.35f),
            };
            Vector3 start = aimOrigin.position;
            Vector3 end = start + aimOrigin.forward * definition.range;
            Gizmos.DrawWireSphere(start + aimOrigin.forward * definition.radius, definition.radius);
            Gizmos.DrawWireSphere(end, definition.radius);
            Gizmos.DrawLine(start, end);
        }
#endif
    }
}
