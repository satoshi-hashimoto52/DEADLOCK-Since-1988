using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>One control point of a damage falloff: at <see cref="distance"/> metres the damage is multiplied by <see cref="multiplier"/>.</summary>
    [Serializable]
    public struct FalloffPoint
    {
        [Min(0f)] public float distance;
        [Range(0f, 1f)] public float multiplier;

        public FalloffPoint(float distance, float multiplier)
        {
            this.distance = distance;
            this.multiplier = multiplier;
        }
    }

    /// <summary>
    /// Optional damage falloff over distance: control points (distance, multiplier), linear between points, the first
    /// multiplier before the first point and the last multiplier after the last one. The gun's max range still decides
    /// whether a shot hits at all (separate). Empty (the default) or invalid: multiplier 1.0, so guns without a profile
    /// keep their damage. Values are trial data for the balance lab, not decided balance.
    /// </summary>
    [Serializable]
    public sealed class DamageFalloffProfile
    {
        public List<FalloffPoint> points = new List<FalloffPoint>();

        public bool IsEmpty => points == null || points.Count == 0;

        public DamageFalloffProfile Clone() => new DamageFalloffProfile { points = points != null ? new List<FalloffPoint>(points) : new List<FalloffPoint>() };

        /// <summary>
        /// Valid when every distance is finite and ≥ 0, distances strictly increase (no duplicates, ascending) and every
        /// multiplier is finite and within 0–1. An empty profile is valid (no falloff).
        /// </summary>
        public bool IsValid(out string error)
        {
            error = null;
            if (IsEmpty)
                return true;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (float.IsNaN(p.distance) || float.IsInfinity(p.distance) || p.distance < 0f)
                    error = $"{i + 1}点目：距離は0以上の数値にしてください";
                else if (float.IsNaN(p.multiplier) || float.IsInfinity(p.multiplier) || p.multiplier < 0f || p.multiplier > 1f)
                    error = $"{i + 1}点目：倍率は0〜1にしてください";
                else if (i > 0 && p.distance <= points[i - 1].distance)
                    error = $"{i + 1}点目：距離は前の点より大きくしてください（重複・逆順は不可）";
                if (error != null)
                    return false;
            }
            return true;
        }

        /// <summary>Damage multiplier at <paramref name="distance"/> metres (1 when empty or invalid).</summary>
        public float Evaluate(float distance)
        {
            if (IsEmpty || !IsValid(out _) || float.IsNaN(distance))
                return 1f;
            if (distance <= points[0].distance)
                return points[0].multiplier;
            for (int i = 1; i < points.Count; i++)
            {
                if (distance <= points[i].distance)
                {
                    var a = points[i - 1];
                    var b = points[i];
                    return Mathf.Lerp(a.multiplier, b.multiplier, (distance - a.distance) / (b.distance - a.distance));
                }
            }
            return points[points.Count - 1].multiplier;
        }

        /// <summary>Multiplier for a nullable profile (null: 1).</summary>
        public static float Evaluate(DamageFalloffProfile profile, float distance) => profile == null ? 1f : profile.Evaluate(distance);
    }
}
