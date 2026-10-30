using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Combat
{
    public struct ProjectileHit
    {
        public int ProjectileId;
        public GameObject Shooter;
        public Collider Collider;
        public Vector3 Point;
        public Vector3 Normal;
        public bool HitDamageable;
        public DamageResult Result;
        /// <summary>Straight-line distance from the shot's origin to the hit point (m).</summary>
        public float Distance;
        /// <summary>Damage falloff multiplier used for this hit (1 without a falloff profile).</summary>
        public float DamageMultiplier;
        /// <summary>Damage handed to the target (base damage × multiplier; 0 when damage is not applied here).</summary>
        public float Damage;
    }

    /// <summary>
    /// Simulates travelling projectiles. Each frame a projectile advances by speed * dt and the swept segment
    /// is ray-tested, so flight time follows the projectile speed and fast projectiles cannot tunnel through thin colliders.
    /// No gravity / drop yet (undecided in the spec).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ProjectileSystem : MonoBehaviour
    {
        struct Projectile
        {
            public int Id;
            public Vector3 Origin;
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float RemainingRange;
            public float Damage;
            public GameObject Shooter;
            public DamageFalloffProfile Falloff;
        }

        [SerializeField] LayerMask hitMask = ~0;
        [Tooltip("Off for display-only projectiles (online: the host decides damage, other machines only draw).")]
        [SerializeField] bool applyDamage = true;

        readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        readonly List<Projectile> active = new List<Projectile>();
        int nextId;

        public event Action<int, Vector3> Spawned;
        public event Action<int, Vector3> Moved;
        public event Action<int> Expired;
        public event Action<ProjectileHit> Hit;

        public int ActiveCount => active.Count;

        public LayerMask HitMask
        {
            get => hitMask;
            set => hitMask = value;
        }

        public bool ApplyDamage
        {
            get => applyDamage;
            set => applyDamage = value;
        }

        public void Fire(Vector3 origin, Vector3 direction, float speed, float maxRange, float damage, GameObject shooter) =>
            Fire(origin, direction, speed, maxRange, damage, shooter, null);

        /// <summary>
        /// Fires one projectile. <paramref name="falloff"/> (optional) scales the damage by the straight-line distance from
        /// <paramref name="origin"/> to the hit point; null or empty keeps the full damage. Max range is unchanged.
        /// </summary>
        public void Fire(Vector3 origin, Vector3 direction, float speed, float maxRange, float damage, GameObject shooter,
            DamageFalloffProfile falloff)
        {
            var projectile = new Projectile
            {
                Id = nextId++,
                Origin = origin,
                Position = origin,
                Direction = direction.normalized,
                Speed = speed,
                RemainingRange = maxRange,
                Damage = damage,
                Shooter = shooter,
                Falloff = falloff,
            };
            active.Add(projectile);
            Spawned?.Invoke(projectile.Id, origin);
        }

        /// <summary>
        /// Where a projectile in flight would stop if nothing moved: its first hit along the rest of its path (the same
        /// rules as <see cref="Step"/>: mask, triggers ignored, never the shooter), else the end of its range. Display
        /// only (tracers): changes nothing. False when no such projectile is in flight.
        /// </summary>
        public bool TryPredictEnd(int id, out Vector3 end, out Vector3 direction)
        {
            end = direction = default;
            int index = active.FindIndex(a => a.Id == id);
            if (index < 0)
                return false;
            var p = active[index];
            direction = p.Direction;
            end = FirstHit(p, p.RemainingRange, out RaycastHit hit) ? hit.point : p.Position + p.Direction * p.RemainingRange;
            return true;
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>Advances all projectiles by <paramref name="deltaTime"/>. Public for deterministic tests.</summary>
        public void Step(float deltaTime)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                float distance = Mathf.Min(p.Speed * deltaTime, p.RemainingRange);

                if (FirstHit(p, distance, out RaycastHit hit))
                {
                    var damageable = hit.collider.GetComponentInParent<IDamageable>();
                    float hitDistance = Vector3.Distance(p.Origin, hit.point);
                    float multiplier = DamageFalloffProfile.Evaluate(p.Falloff, hitDistance);
                    float damage = p.Damage * multiplier;
                    bool applied = damageable != null && applyDamage;
                    var result = applied ? damageable.ApplyDamage(damage, hit.point, p.Direction) : DamageResult.Ignored;
                    RemoveAt(i);

                    Hit?.Invoke(new ProjectileHit
                    {
                        ProjectileId = p.Id,
                        Shooter = p.Shooter,
                        Collider = hit.collider,
                        Point = hit.point,
                        Normal = hit.normal,
                        HitDamageable = damageable != null,
                        Result = result,
                        Distance = hitDistance,
                        DamageMultiplier = multiplier,
                        Damage = applied ? damage : 0f,
                    });
                    continue;
                }

                Vector3 next = p.Position + p.Direction * distance;
                Moved?.Invoke(p.Id, next);
                p.Position = next;
                p.RemainingRange -= distance;

                if (p.RemainingRange <= 0f)
                {
                    RemoveAt(i);
                    Expired?.Invoke(p.Id);
                }
                else
                {
                    active[i] = p;
                }
            }
        }

        /// <summary>Nearest hit along the segment, ignoring the shooter's own colliders (no self-hits).</summary>
        bool FirstHit(Projectile p, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(p.Position, p.Direction, hitBuffer, distance, hitMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Transform shooter = p.Shooter != null ? p.Shooter.transform : null;
            for (int i = 0; i < count; i++)
            {
                var h = hitBuffer[i];
                if (h.distance >= best || (shooter != null && h.collider.transform.IsChildOf(shooter)))
                    continue;
                best = h.distance;
                nearest = h;
            }
            return best < float.MaxValue;
        }

        void RemoveAt(int index)
        {
            int last = active.Count - 1;
            active[index] = active[last];
            active.RemoveAt(last);
        }
    }
}
