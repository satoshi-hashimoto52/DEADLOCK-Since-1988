using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Match
{
    /// <summary>One participant at one moment of the match clock.</summary>
    public struct ReplaySample
    {
        public float Time;
        public Vector3 Position;
        /// <summary>Facing of the body (degrees).</summary>
        public float Yaw;
        /// <summary>Look up / down of the eyes (degrees, positive up, as the player's motor).</summary>
        public float Pitch;
        /// <summary>Height of the eyes above <see cref="Position"/> (crouching lowers it).</summary>
        public float EyeHeight;
        public float Health;
        public string WeaponId;
        public int Kills;
        public bool Alive;
        /// <summary>Posture of the body (online spectating draws the past body in it; offline leaves it standing).</summary>
        public byte Posture;
        /// <summary>Reloading at that moment (online: the first-person weapon of the watched one).</summary>
        public bool Reloading;
        /// <summary>Aiming down the sights at that moment (online).</summary>
        public bool Ads;
    }

    public enum ReplayEventKind
    {
        Gunshot,
        Footstep,
        Reload,
        Death,
    }

    /// <summary>A sound of the game at a time and place (played to the spectator at the same past time).</summary>
    public struct ReplayEvent
    {
        public float Time;
        public int SlotId;
        public ReplayEventKind Kind;
        public Vector3 Position;
        public Vector3 Direction;
        public string WeaponId;
    }

    /// <summary>
    /// The recent past of a match for the 10 s delayed spectating (Stage 36, D-063): every participant's position,
    /// facing, look, health, weapon, kills and life, and the timed sound events, kept for <see cref="KeepSeconds"/>
    /// and never more than the caps (memory bounded). A state is only ever read from what was recorded: a time before
    /// the first sample or after the last one has no state (nothing is made up). Positions and facings between two
    /// samples are interpolated; health, weapon, kills and life are those of the last sample at or before the time.
    /// </summary>
    public sealed class MatchReplay
    {
        public const float DelaySeconds = 10f;
        public const float KeepSeconds = 12f;
        public const int MaxSamplesPerSlot = 12 * 60;
        public const int MaxEvents = 2048;

        readonly Dictionary<int, List<ReplaySample>> samples = new Dictionary<int, List<ReplaySample>>();
        readonly List<ReplayEvent> events = new List<ReplayEvent>();

        /// <summary>Time of the first sample kept (NaN before any).</summary>
        public float FirstTime { get; private set; } = float.NaN;
        public float LastTime { get; private set; } = float.NaN;
        /// <summary>When recording started (the start of the match); the replay can never go before it.</summary>
        public float RecordingStart { get; private set; } = float.NaN;
        public IEnumerable<int> Slots => samples.Keys;
        public int EventCount => events.Count;

        public int SampleCount
        {
            get
            {
                int n = 0;
                foreach (var list in samples.Values)
                    n += list.Count;
                return n;
            }
        }

        public void Record(int slotId, ReplaySample sample)
        {
            if (float.IsNaN(RecordingStart))
                RecordingStart = sample.Time;
            if (!samples.TryGetValue(slotId, out var list))
                samples[slotId] = list = new List<ReplaySample>();
            if (list.Count > 0 && sample.Time <= list[list.Count - 1].Time)
                return; // time only goes forward
            list.Add(sample);
            if (list.Count > MaxSamplesPerSlot)
                list.RemoveAt(0);
            LastTime = float.IsNaN(LastTime) ? sample.Time : Mathf.Max(LastTime, sample.Time);
            Trim(sample.Time);
        }

        /// <summary>A sound event as it is recorded (live): the sound player plays the other participants' sounds from it.</summary>
        public static event Action<ReplayEvent> EventAdded;

        public void AddEvent(ReplayEvent e)
        {
            events.Add(e);
            if (events.Count > MaxEvents)
                events.RemoveAt(0);
            EventAdded?.Invoke(e);
        }

        /// <summary>Drops what is older than <see cref="KeepSeconds"/> before <paramref name="now"/>.</summary>
        public void Trim(float now)
        {
            float oldest = now - KeepSeconds;
            float first = float.MaxValue;
            foreach (var list in samples.Values)
            {
                // Keep one sample at or before the limit so a time right at it can still be read.
                int remove = 0;
                while (remove + 1 < list.Count && list[remove + 1].Time <= oldest)
                    remove++;
                if (remove > 0)
                    list.RemoveRange(0, remove);
                if (list.Count > 0)
                    first = Mathf.Min(first, list[0].Time);
            }
            FirstTime = first == float.MaxValue ? float.NaN : first;
            int drop = 0;
            while (drop < events.Count && events[drop].Time < oldest)
                drop++;
            if (drop > 0)
                events.RemoveRange(0, drop);
        }

        /// <summary>The participant's recorded state at <paramref name="time"/>; false when that time was not recorded.</summary>
        public bool TryState(int slotId, float time, out ReplaySample state)
        {
            state = default;
            if (!samples.TryGetValue(slotId, out var list) || list.Count == 0)
                return false;
            if (time < list[0].Time || time > list[list.Count - 1].Time)
                return false;
            int hi = list.FindIndex(s => s.Time >= time);
            if (hi <= 0)
            {
                state = list[Mathf.Max(0, hi)];
                return true;
            }
            var a = list[hi - 1];
            var b = list[hi];
            if (Mathf.Approximately(b.Time, time))
            {
                state = b;
                return true;
            }
            float t = (time - a.Time) / (b.Time - a.Time);
            state = a; // step values from the earlier sample
            state.Time = time;
            state.Position = Vector3.Lerp(a.Position, b.Position, t);
            state.Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, t);
            state.Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t);
            state.EyeHeight = Mathf.Lerp(a.EyeHeight, b.EyeHeight, t);
            return true;
        }

        /// <summary>
        /// The last sample recorded at or before <paramref name="time"/>, as recorded (no interpolation, so nothing of a
        /// later moment is in it); false when nothing that old is kept. Online spectating sends only these (Stage 37E).
        /// </summary>
        public bool TrySampleAtOrBefore(int slotId, float time, out ReplaySample sample)
        {
            sample = default;
            if (!samples.TryGetValue(slotId, out var list) || list.Count == 0 || list[0].Time > time)
                return false;
            int i = list.FindLastIndex(s => s.Time <= time);
            sample = list[i];
            return true;
        }

        /// <summary>The sound events after <paramref name="from"/> up to and including <paramref name="to"/>, in time order.</summary>
        public List<ReplayEvent> EventsBetween(float from, float to)
        {
            var list = new List<ReplayEvent>();
            foreach (var e in events)
                if (e.Time > from && e.Time <= to)
                    list.Add(e);
            list.Sort((x, y) => x.Time.CompareTo(y.Time));
            return list;
        }

        /// <summary>The time to show for a real time: 10 s earlier, or none while less than that has been recorded.</summary>
        public bool TryPlaybackTime(float now, out float playback)
        {
            playback = now - DelaySeconds;
            return !float.IsNaN(RecordingStart) && playback >= RecordingStart && !float.IsNaN(FirstTime) && playback >= FirstTime;
        }
    }
}
