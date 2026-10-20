using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// One melee check: a sphere swept forward from the eye. The first thing it meets ends the check (walls block),
    /// at most one target is damaged, the attacker's own colliders are ignored, and a clear line to the target is
    /// required so the sphere cannot graze a target around the edge of cover. Shared by the offline knife and the
    /// online host.
    /// </summary>
    public static class MeleeSweep
    {
        static readonly RaycastHit[] Buffer = new RaycastHit[16];

        public struct Result
        {
            /// <summary>The sweep met something (a wall or a target): the swing is resolved.</summary>
            public bool Resolved;
            public Collider Collider;
            public Vector3 Point;
            public DamageResult Damage;
        }

        /// <param name="ignoreRoot">The attacker; its colliders never stop or take the swing. May be null.</param>
        /// <param name="applyDamage">False to only find what would be hit.</param>
        public static Result Run(Vector3 origin, Vector3 direction, MeleeDefinition definition, LayerMask mask, Transform ignoreRoot, bool applyDamage = true)
        {
            var result = new Result();
            int count = Physics.SphereCastNonAlloc(origin, definition.radius, direction, Buffer, definition.range, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit nearest = default;
            for (int i = 0; i < count; i++)
            {
                var h = Buffer[i];
                if (h.distance >= best || (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)))
                    continue;
                // Overlaps at the start report distance 0 and no point: treat the collider's closest point as the hit.
                if (h.distance <= 0f)
                    h.point = h.collider.ClosestPoint(origin);
                best = h.distance;
                nearest = h;
            }
            if (best == float.MaxValue)
                return result; // nothing yet: keep checking for the rest of the active window

            result.Resolved = true;
            result.Collider = nearest.collider;
            result.Point = nearest.point;
            var damageable = nearest.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && HasLineOfSight(origin, nearest.collider, mask, ignoreRoot))
                result.Damage = applyDamage ? damageable.ApplyDamage(definition.damage, nearest.point, direction) : DamageResult.Damaged;
            return result;
        }

        /// <summary>The sphere can graze a target past the edge of cover; require a clear ray to the target as well.</summary>
        static bool HasLineOfSight(Vector3 origin, Collider target, LayerMask mask, Transform ignoreRoot)
        {
            Vector3 to = target.bounds.center;
            to.y = Mathf.Clamp(origin.y, target.bounds.min.y, target.bounds.max.y);
            Vector3 delta = to - origin;
            int count = Physics.RaycastNonAlloc(origin, delta, Buffer, delta.magnitude + 0.5f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < count; i++)
            {
                var h = Buffer[i];
                if (h.distance >= best || (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)))
                    continue;
                best = h.distance;
                first = h.collider;
            }
            // Another part of the same target (e.g. the head in front of the body) is not cover.
            return first == null || first == target || SameDamageable(first, target);
        }

        static bool SameDamageable(Collider a, Collider b)
        {
            var da = a.GetComponentInParent<IDamageable>();
            return da != null && da == b.GetComponentInParent<IDamageable>();
        }
    }
}
