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
        /// <summary>Where the enemy faced when detected (world x, z, unit length); never updated while it is hidden.</summary>
        public Vector2 Facing;
    }

    /// <summary>
    /// A footstep heard from another floor: only a rough direction (one of 8, in the world; none when nearly straight
    /// above / below), whether it is above or below, and which way the enemy faced (one of 8), never the step's
    /// position or distance. Fixed at the step.
    /// </summary>
    public struct FootstepCue
    {
        public MapEnemy Enemy;
        public float Time;
        /// <summary>Horizontal world direction from the player, rounded to 45 degrees (x, z); zero = straight above / below.</summary>
        public Vector2 Direction;
        /// <summary>+1 = above, -1 = below.</summary>
        public int Vertical;
        /// <summary>Where the enemy faced at the step (world x, z), rounded to 45 degrees like <see cref="Direction"/>.</summary>
        public Vector2 Heading;
    }

    /// <summary>An enemy that has a clear sight line to the player (where it is, refreshed with the sight checks).</summary>
    public struct Watcher
    {
        public MapEnemy Enemy;
        public Vector3 Position;
        public float Time;
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
    /// A footstep on another floor than the player's (the floor map's floor, with the same stair hysteresis) is never a
    /// marker: it is a <see cref="FootstepCue"/> (rough direction, above / below) for the heard display time. An enemy
    /// on another floor shows at its position only while it is seen (over the atrium parapet, up the stairs).
    /// Dead enemies are removed at once. No team sharing, no gunfire detection, no permanent radar. Display only:
    /// enemies and combat are not affected. Values are provisional.
    /// </summary>
    public sealed class EnemyDetector : MonoBehaviour
    {
        [SerializeField] Camera eye;
        [SerializeField] FloorMapData map;
        [Tooltip("The player's feet: the floor footsteps are compared with (no position marker for another floor).")]
        [SerializeField] Transform listener;
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
        [Tooltip("A footstep from another floor closer than this horizontally (m) has no direction (straight above / below).")]
        [SerializeField, Min(0f)] float overheadRadius = 2.5f;

        // Feet, body and head of a 1.8 m enemy (scaled by its height).
        static readonly float[] BodyHeights = { 0.3f, 0.9f, 1.6f };

        readonly List<MapContact> contacts = new List<MapContact>();
        readonly List<FootstepCue> cues = new List<FootstepCue>();
        readonly List<Watcher> watchers = new List<Watcher>();
        int listenerFloor = -1;
        readonly List<IDetectionModifier> modifiers = new List<IDetectionModifier>();
        readonly RaycastHit[] hits = new RaycastHit[16];
        float nextVision;

        public IReadOnlyList<MapContact> Contacts => contacts;
        /// <summary>
        /// Enemies that see the player now (the floor map marks the frame toward those beyond its range: "you are being
        /// watched from there"). Refreshed every sight check; one not refreshed for a moment is gone.
        /// </summary>
        public IReadOnlyList<Watcher> Watchers => watchers;
        /// <summary>Footsteps heard from another floor (direction and above / below only).</summary>
        public IReadOnlyList<FootstepCue> Cues => cues;
        public float CueDisplayTime => heardDisplayTime;
        /// <summary>The player's floor footsteps are compared with (same as the floor map shows: same hysteresis).</summary>
        public int ListenerFloor => listenerFloor;
        public FloorMapData Map => map;
        public Camera Eye => eye;
        public float VisionRange => visionRange * Scale(true);
        public float HearingRange => hearingRange * Scale(false);
        public float VisionInterval => visionInterval;
        public float DisplayTime(ContactKind kind) => kind == ContactKind.Seen ? seenDisplayTime : heardDisplayTime;

        /// <summary>A seen contact still refreshed by the sight checks (in view now), not one kept after losing sight.</summary>
        public bool InSight(MapContact contact) =>
            contact.Kind == ContactKind.Seen && Time.time - contact.Time <= visionInterval * 1.5f;

        /// <summary>Raised for every detection (tests, later sound / UI cues).</summary>
        public event System.Action<MapContact> Detected;
        /// <summary>Raised for every footstep heard from another floor.</summary>
        public event System.Action<FootstepCue> CueHeard;

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
            cues.Clear();
            watchers.Clear();
        }

        void UpdateListenerFloor()
        {
            if (map != null)
                listenerFloor = map.FloorFor(ListenerFeet.y, listenerFloor);
        }

        Vector3 ListenerFeet => listener != null ? listener.position : eye.transform.position + Vector3.down * 1.6f;

        void Update()
        {
            UpdateListenerFloor();
            if (Time.time >= nextVision)
            {
                nextVision = Time.time + visionInterval;
                var enemies = MapEnemy.All;
                watchers.Clear();
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].IsDead)
                        continue;
                    if (CanSee(enemies[i]))
                        Record(enemies[i], ContactKind.Seen, enemies[i].Position);
                    if (enemies[i].WatchingItsTarget)
                        watchers.Add(new Watcher { Enemy = enemies[i], Position = enemies[i].Position, Time = Time.time });
                }
            }
            for (int i = contacts.Count - 1; i >= 0; i--)
            {
                var c = contacts[i];
                if (c.Enemy == null || c.Enemy.IsDead || Time.time - c.Time > DisplayTime(c.Kind))
                    contacts.RemoveAt(i);
            }
            for (int i = cues.Count - 1; i >= 0; i--)
                if (cues[i].Enemy == null || cues[i].Enemy.IsDead || Time.time - cues[i].Time > heardDisplayTime)
                    cues.RemoveAt(i);
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
            UpdateListenerFloor();
            int floor = map != null ? map.NearestFloor(step.y + 0.1f) : 0;
            if (map != null && floor != listenerFloor)
            {
                // Another floor: no marker at the step, only a rough direction and above / below.
                Cue(enemy, step, floor > listenerFloor ? 1 : -1);
                return;
            }
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
                Facing = enemy.Facing,
            };
            int i = IndexOf(enemy);
            if (i >= 0)
                contacts[i] = contact;
            else
                contacts.Add(contact);
            Detected?.Invoke(contact);
        }

        void Cue(MapEnemy enemy, Vector3 step, int vertical)
        {
            Vector3 d = step - ListenerFeet;
            var flat = new Vector2(d.x, d.z);
            Vector2 direction = flat.magnitude >= overheadRadius ? Round8(flat) : Vector2.zero;
            var cue = new FootstepCue { Enemy = enemy, Time = Time.time, Direction = direction, Vertical = vertical, Heading = Round8(enemy.Facing) };
            int i = cues.FindIndex(c => c.Enemy == enemy);
            if (i >= 0)
                cues[i] = cue;
            else
                cues.Add(cue);
            // A position marker of this enemy from before it changed floor must not stay on the map.
            int m = IndexOf(enemy);
            if (m >= 0 && contacts[m].Kind == ContactKind.Heard)
                contacts.RemoveAt(m);
            CueHeard?.Invoke(cue);
        }

        /// <summary>A direction rounded to one of 8 (45 degrees), unit length.</summary>
        public static Vector2 Round8(Vector2 v)
        {
            float a = Mathf.Round(Mathf.Atan2(v.y, v.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            return new Vector2(Mathf.Round(Mathf.Cos(a) * 1e4f) / 1e4f, Mathf.Round(Mathf.Sin(a) * 1e4f) / 1e4f);
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
            cues.RemoveAll(c => c.Enemy == enemy);
            watchers.RemoveAll(w => w.Enemy == enemy);
        }

        public bool TryGetContact(MapEnemy enemy, out MapContact contact)
        {
            int i = IndexOf(enemy);
            contact = i >= 0 ? contacts[i] : default;
            return i >= 0;
        }
    }
}
