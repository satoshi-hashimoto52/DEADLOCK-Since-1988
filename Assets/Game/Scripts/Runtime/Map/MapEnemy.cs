using System;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.View;
using UnityEngine;

namespace Deadlock.Map
{
    /// <summary>
    /// An enemy the floor map can detect (offline zombie): its position, alive state and footsteps (the walk cycle of
    /// its <see cref="ZombieView"/>). Display only: it never changes the zombie.
    /// </summary>
    public sealed class MapEnemy : MonoBehaviour
    {
        [SerializeField] Zombie zombie;
        [SerializeField] ZombieView view;
        [Tooltip("Footstep loudness (1 = normal). Hook for later perks / skills (quiet steps lower it); none exist yet.")]
        [SerializeField, Min(0f)] float footstepLoudness = 1f;
        [Tooltip("Height of the eyes / head above the feet, for line-of-sight checks.")]
        [SerializeField] float height = 1.8f;

        static readonly List<MapEnemy> all = new List<MapEnemy>();

        /// <summary>Enabled enemies of the loaded scene.</summary>
        public static IReadOnlyList<MapEnemy> All => all;

        /// <summary>A footstep at this position (the enemy's feet).</summary>
        public static event Action<MapEnemy, Vector3> Stepped;
        /// <summary>Died, or left the scene: its marker goes at once.</summary>
        public static event Action<MapEnemy> Gone;

        public Vector3 Position => transform.position;
        /// <summary>Where the enemy faces (world x, z, unit length): it turns toward where it walks and whom it attacks.</summary>
        public Vector2 Facing
        {
            get
            {
                var f = new Vector2(transform.forward.x, transform.forward.z);
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector2.up;
            }
        }
        public float Height => height;
        public bool IsDead => zombie == null || zombie.IsDead;

        public float FootstepLoudness
        {
            get => footstepLoudness;
            set => footstepLoudness = Mathf.Max(0f, value);
        }

        void OnEnable()
        {
            all.Add(this);
            if (view != null)
                view.Stepped += OnStepped;
            if (zombie != null)
                zombie.Died += OnDied;
        }

        void OnDisable()
        {
            all.Remove(this);
            if (view != null)
                view.Stepped -= OnStepped;
            if (zombie != null)
                zombie.Died -= OnDied;
            Gone?.Invoke(this);
        }

        void OnStepped(ZombieView _)
        {
            if (!IsDead)
                Stepped?.Invoke(this, transform.position);
        }

        void OnDied(Zombie _) => Gone?.Invoke(this);
    }
}
