using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Map
{
    public enum ContactKind
    {
        /// <summary>In the player's view and not hidden by walls / floors.</summary>
        Seen,
        /// <summary>A footstep within hearing range (walls and floors in between shorten it).</summary>
        Heard,
    }

    /// <summary>One enemy on the map: where and when it was last detected (never its live position).</summary>
    public struct MapContact
    {
        public MapEnemy Enemy;
        public ContactKind Kind;
        public Vector3 Position;
        public float Time;
        /// <summary>Floor index of the detected position.</summary>
        public int Floor;
    }

    /// <summary>
    /// Later perks / skills (wider hearing, scouting): scales the ranges. None exist yet; nothing registers one.
    /// The enemy side (quiet steps) is <see cref="MapEnemy.FootstepLoudness"/>.
    /// </summary>
    public interface IDetectionModifier
    {
        float VisionRangeScale { get; }
        float HearingRangeScale { get; }
    }

    /// <summary>
    /// Prototype detection rules of this game for the floor map (not a copy of another game's rules). Without perks or
    /// skills an enemy shows only while
    /// 1. it is seen: inside the camera view, within range, and a clear line from the eye reaches its feet, body or head
    ///    (walls and floors block it), checked every <see cref="visionInterval"/>; or
    /// 2. it is heard: one of its footsteps (<see cref="MapEnemy.Stepped"/>; a standing enemy makes none) is within
    ///    hearing range, which each wall / floor between the step and the player's ears shortens.
    /// The marker stays where the enemy was detected and fades out after a short time; it never follows a hidden enemy.
    /// Dead enemies are removed at once. No team sharing, no gunfire detection, no permanent radar. Display only:
    /// enemies and combat are not affected. Values are provisional.
    /// </summary>
    public sealed class EnemyDetector : MonoBehaviour
    {
        [SerializeField] Camera eye;
        [SerializeField] FloorMapData map;
        [Tooltip("What blocks sight and muffles footsteps (the level, not players / enemies).")]
        [SerializeField] LayerMask occluders = 1;

        [Header("Sight")]
        [SerializeField, Min(1f)] float visionRange = 50f;
        [SerializeField, Min(0.02f)] float visionInterval = 0.1f;
        [Tooltip("A seen enemy's marker stays this long after it was last seen.")]
        [SerializeField, Min(0.1f)] float seenDisplayTime = 1.5f;

        [Header("Footsteps")]
        [Tooltip("Hearing range of a normal footstep with nothing in between (m).")]
        [SerializeField, Min(0f)] float hearingRange = 15f;
        [Tooltip("Each wall / floor between the step and the ears multiplies the range by this.")]
        [SerializeField, Range(0f, 1f)] float obstacleAttenuation = 0.5f;
        [SerializeField, Min(0.1f)] float heardDisplayTime = 2f;

        // Feet, body and head of a 1.8 m enemy (scaled by its height).
        static readonly float[] BodyHeights = { 0.3f, 0.9f, 1.6f };

        readonly List<MapContact> contacts = new List<MapContact>();
        readonly List<IDetectionModifier> modifiers = new List<IDetectionModifier>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        float nextVision;

        public IReadOnlyList<MapContact> Contacts => contacts;
        public FloorMapData Map => map;
        public Camera Eye => eye;
        public float VisionRange => visionRange * Scale(true);
        public float HearingRange => hearingRange * Scale(false);
        public float VisionInterval => visionInterval;
        public float DisplayTime(ContactKind kind) => kind == ContactKind.Seen ? seenDisplayTime : heardDisplayTime;

        /// <summary>Raised for every detection (tests, later sound / UI cues).</summary>
        public event System.Action<MapContact> Detected;

        public void AddModifier(IDetectionModifier modifier) => modifiers.Add(modifier);
        public void RemoveModifier(IDetectionModifier modifier) => modifiers.Remove(modifier);

        float Scale(bool vision)
        {
            float s = 1f;
            foreach (var m in modifiers)
                s *= vision ? m.VisionRangeScale : m.HearingRangeScale;
            return s;
        }

        void OnEnable()
        {
            MapEnemy.Stepped += OnStepped;
            MapEnemy.Gone += Remove;
        }

        void OnDisable()
        {
            MapEnemy.Stepped -= OnStepped;
            MapEnemy.Gone -= Remove;
            contacts.Clear();
        }

        void Update()
        {
            if (Time.time >= nextVision)
            {
                nextVision = Time.time + visionInterval;
                var enemies = MapEnemy.All;
                for (int i = 0; i < enemies.Count; i++)
                    if (!enemies[i].IsDead && CanSee(enemies[i]))
                        Record(enemies[i], ContactKind.Seen, enemies[i].Position);
            }
            for (int i = contacts.Count - 1; i >= 0; i--)
            {
                var c = contacts[i];
                if (c.Enemy == null || c.Enemy.IsDead || Time.time - c.Time > DisplayTime(c.Kind))
                    contacts.RemoveAt(i);
            }
        }

        /// <summary>Inside the camera view, within range, and not hidden by the level (feet, body or head).</summary>
        public bool CanSee(MapEnemy enemy)
        {
            Vector3 from = eye.transform.position;
            float range = VisionRange;
            for (int i = 0; i < BodyHeights.Length; i++)
            {
                Vector3 p = enemy.Position + Vector3.up * (BodyHeights[i] * enemy.Height / 1.8f);
                Vector3 v = eye.WorldToViewportPoint(p);
                if (v.z < eye.nearClipPlane || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                    continue;
                if ((p - from).sqrMagnitude > range * range)
                    continue;
                if (!Physics.Linecast(from, p, occluders, QueryTriggerInteraction.Ignore))
                    return true;
            }
            return false;
        }

        /// <summary>Range left for a footstep at this position after the walls / floors in between (0 = inaudible).</summary>
        public float EffectiveHearingRange(MapEnemy enemy, Vector3 step, out int obstacles)
        {
            Vector3 from = step + Vector3.up * 0.3f;
            Vector3 to = eye.transform.position;
            Vector3 d = to - from;
            obstacles = Physics.RaycastNonAlloc(from, d.normalized, hits, d.magnitude, occluders, QueryTriggerInteraction.Ignore);
            return HearingRange * enemy.FootstepLoudness * Mathf.Pow(obstacleAttenuation, obstacles);
        }

        void OnStepped(MapEnemy enemy, Vector3 step)
        {
            float distance = Vector3.Distance(step, eye.transform.position);
            if (distance > HearingRange * enemy.FootstepLoudness)
                return; // out of range even in the open: no ray needed
            if (distance > EffectiveHearingRange(enemy, step, out _))
                return;
            // A seen enemy stays a seen marker (sight is the stronger information).
            int i = IndexOf(enemy);
            if (i >= 0 && contacts[i].Kind == ContactKind.Seen && Time.time - contacts[i].Time < visionInterval * 1.5f)
                return;
            Record(enemy, ContactKind.Heard, step);
        }

        void Record(MapEnemy enemy, ContactKind kind, Vector3 position)
        {
            var contact = new MapContact
            {
                Enemy = enemy,
                Kind = kind,
                Position = position,
                Time = Time.time,
                Floor = map != null ? map.NearestFloor(position.y + 0.1f) : 0,
            };
            int i = IndexOf(enemy);
            if (i >= 0)
                contacts[i] = contact;
            else
                contacts.Add(contact);
            Detected?.Invoke(contact);
        }

        int IndexOf(MapEnemy enemy)
        {
            for (int i = 0; i < contacts.Count; i++)
                if (contacts[i].Enemy == enemy)
                    return i;
            return -1;
        }

        void Remove(MapEnemy enemy)
        {
            int i = IndexOf(enemy);
            if (i >= 0)
                contacts.RemoveAt(i);
        }

        public bool TryGetContact(MapEnemy enemy, out MapContact contact)
        {
            int i = IndexOf(enemy);
            contact = i >= 0 ? contacts[i] : default;
            return i >= 0;
        }
    }
}
