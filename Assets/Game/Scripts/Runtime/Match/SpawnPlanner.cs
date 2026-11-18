using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Match
{
    /// <summary>
    /// Picks the start places of the eight (D-060): from valid candidate points of the map, far apart, out of each
    /// other's direct line of sight, preferring sheltered points (cover around), never two on one point, and different
    /// from match to match (the seed). The distances are provisional, adjustable per map. If the strict rules cannot be
    /// met the minimum distance is lowered step by step down to <see cref="FloorDistance"/>, then the line of sight is
    /// allowed; each relaxation is logged. If even that fails, nothing is placed (no one is forced onto an invalid point).
    /// </summary>
    public sealed class SpawnPlanner
    {
        /// <summary>Wanted distance between any two (metres, provisional).</summary>
        public float WantedDistance = 14f;
        /// <summary>Lowest distance accepted after relaxing (metres, provisional).</summary>
        public float FloorDistance = 5f;
        /// <summary>Two places never closer than this (no overlap).</summary>
        public float Separation = 1.5f;
        /// <summary>Randomised attempts per rule level.</summary>
        public int Attempts = 40;

        /// <summary>True when two points see each other (eye height). Null: no line-of-sight rule.</summary>
        public Func<Vector3, Vector3, bool> LineOfSight;
        /// <summary>0..1, how sheltered a point is. Null: all equal.</summary>
        public Func<Vector3, float> Shelter;

        public readonly List<string> Log = new List<string>();

        public bool TryPlan(IReadOnlyList<Vector3> candidates, int count, int seed, out List<Vector3> spawns)
        {
            spawns = null;
            Log.Clear();
            var unique = new List<Vector3>();
            foreach (var c in candidates)
                if (unique.TrueForAll(u => (u - c).sqrMagnitude >= Separation * Separation))
                    unique.Add(c);
            if (unique.Count < count)
            {
                Log.Add($"出現候補が足りません（{unique.Count} 地点 / 必要 {count}）");
                return false;
            }
            var shelter = new float[unique.Count];
            for (int i = 0; i < unique.Count; i++)
                shelter[i] = Shelter != null ? Mathf.Clamp01(Shelter(unique[i])) : 0f;
            sightCache.Clear();
            var rng = new System.Random(seed);
            var distances = new List<float>();
            for (float d = WantedDistance; d > FloorDistance; d *= 0.85f)
                distances.Add(d);
            distances.Add(FloorDistance);
            foreach (bool avoidSight in new[] { true, false })
            {
                if (!avoidSight && LineOfSight == null)
                    break;
                foreach (float d in distances)
                    for (int attempt = 0; attempt < Attempts; attempt++)
                        if (TryOnce(unique, shelter, count, d, avoidSight, rng, out spawns))
                        {
                            if (d < WantedDistance)
                                Log.Add($"距離を {d:0.0} m まで下げて配置しました");
                            if (!avoidSight)
                                Log.Add("射線を避けられないため、見通しのある配置を含みます");
                            return true;
                        }
                Log.Add(avoidSight ? $"{FloorDistance:0.0} m 以上・射線なしの配置が見つかりません" : $"{FloorDistance:0.0} m 以上の配置が見つかりません");
            }
            spawns = null;
            return false;
        }

        readonly Dictionary<long, bool> sightCache = new Dictionary<long, bool>();

        // Each pair is checked once per plan (a line cast in the scene).
        bool Sees(List<Vector3> pts, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!sightCache.TryGetValue(key, out bool sees))
                sightCache[key] = sees = LineOfSight(pts[a], pts[b]);
            return sees;
        }

        bool TryOnce(List<Vector3> pts, float[] shelter, int count, float minDistance, bool avoidSight, System.Random rng, out List<Vector3> chosen)
        {
            chosen = new List<Vector3>(count);
            var chosenIndex = new List<int>(count);
            var used = new bool[pts.Count];
            int first = rng.Next(pts.Count);
            chosen.Add(pts[first]);
            chosenIndex.Add(first);
            used[first] = true;
            while (chosen.Count < count)
            {
                int best = -1;
                float bestScore = float.MinValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (used[i])
                        continue;
                    float nearest = float.MaxValue;
                    bool ok = true;
                    foreach (int c in chosenIndex)
                    {
                        float dist = Vector3.Distance(pts[c], pts[i]);
                        if (dist < minDistance || (avoidSight && LineOfSight != null && Sees(pts, c, i)))
                        {
                            ok = false;
                            break;
                        }
                        nearest = Mathf.Min(nearest, dist);
                    }
                    if (!ok)
                        continue;
                    // Far from the others, sheltered, and a little random so matches differ.
                    float score = Mathf.Min(nearest, minDistance * 2f) + shelter[i] * minDistance * 0.5f + (float)rng.NextDouble() * minDistance * 0.4f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                if (best < 0)
                    return false;
                used[best] = true;
                chosen.Add(pts[best]);
                chosenIndex.Add(best);
            }
            return true;
        }
    }
}
