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
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float RemainingRange;
            public float Damage;
            public GameObject Shooter;
        }

        [SerializeField] LayerMask hitMask = ~0;

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

        public void Fire(Vector3 origin, Vector3 direction, float speed, float maxRange, float damage, GameObject shooter)
        {
            var projectile = new Projectile
            {
                Id = nextId++,
                Position = origin,
                Direction = direction.normalized,
                Speed = speed,
                RemainingRange = maxRange,
                Damage = damage,
                Shooter = shooter,
            };
            active.Add(projectile);
            Spawned?.Invoke(projectile.Id, origin);
        }

        void Update() => Step(Time.deltaTime);

        /// <summary>Advances all projectiles by <paramref name="deltaTime"/>. Public for deterministic tests.</summary>
        public void Step(float deltaTime)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                float distance = Mathf.Min(p.Speed * deltaTime, p.RemainingRange);

                if (Physics.Raycast(p.Position, p.Direction, out RaycastHit hit, distance, hitMask, QueryTriggerInteraction.Ignore))
                {
                    var damageable = hit.collider.GetComponentInParent<IDamageable>();
                    var result = damageable != null
                        ? damageable.ApplyDamage(p.Damage, hit.point, p.Direction)
                        : DamageResult.Ignored;
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

        void RemoveAt(int index)
        {
            int last = active.Count - 1;
            active[index] = active[last];
            active.RemoveAt(last);
        }
    }
}
