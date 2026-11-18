using System;
using System.Collections.Generic;
using Deadlock.Map;
using Deadlock.UI;
using UnityEngine;

namespace Deadlock.Match
{
    /// <summary>
    /// Where a spectator's sound events go (Stage 36). The game has no sound yet (no audio source, no approved sound
    /// file): the events reach this at the right past time and place, and nothing is heard until sounds exist.
    /// </summary>
    public interface ISpectateSoundSink
    {
        void Play(ReplayEvent e, Vector3 listenerPosition, Quaternion listenerRotation);
    }

    /// <summary>
    /// The 10 s delayed spectating of the offline normal match (Stage 36, D-063): started at once when the local
    /// player dies (no kill camera, no wait). Shows the match as it was 10 s ago from the recorded past
    /// (<see cref="MatchReplay"/>): its own camera at the eyes of the participant being watched (their past position,
    /// facing and look; the spectator cannot turn or move it), every other participant as a stand-in body at their past
    /// place (the live bodies are left out of this camera), shots as short lines, and the sound events at their past
    /// time. Watched: the living participants of that past time (humans and NPCs), previous / next; when the watched
    /// one dies at the past time the next living one is taken. While less than 10 s has been recorded nothing is shown
    /// (waiting). The HUD of the game (map, markers, footsteps, controls) is hidden; combat input stays blocked.
    /// </summary>
    public sealed class MatchSpectator : MonoBehaviour
    {
        public const string BlockReason = "spectate";
        const float ShotLineLength = 40f;

        MatchArena arena;
        MatchReplay replay;
        Func<float> clock;
        int localSlot;
        Camera cam;
        Canvas hiddenHud;
        readonly List<Camera> disabledCameras = new List<Camera>();
        readonly List<Behaviour> disabledParts = new List<Behaviour>();
        readonly Dictionary<int, GameObject> ghosts = new Dictionary<int, GameObject>();
        readonly List<(LineRenderer line, float until)> shots = new List<(LineRenderer, float)>();
        float lastPlayback = float.NaN;
        Material lineMaterial;

        public int TargetSlot { get; private set; } = -1;
        public bool Waiting { get; private set; } = true;
        public float PlaybackTime { get; private set; }
        public ReplaySample TargetState { get; private set; }
        public Camera Camera => cam;
        /// <summary>Sound events played to the spectator, newest last (bounded), for checks.</summary>
        public readonly List<ReplayEvent> PlayedSounds = new List<ReplayEvent>();
        public static ISpectateSoundSink SoundSink;
        public event Action<ReplayEvent> SoundPlayed;

        public static MatchSpectator Begin(MatchArena arena, MatchReplay replay, Func<float> clock, int localSlot)
        {
            var go = new GameObject("MatchSpectator");
            var s = go.AddComponent<MatchSpectator>();
            s.arena = arena;
            s.replay = replay;
            s.clock = clock;
            s.localSlot = localSlot;
            s.Setup();
            return s;
        }

        void Setup()
        {
            // Nothing of the game's HUD: no map, markers, footsteps, ammo panel or controls.
            var hud = FindAnyObjectByType<HudController>();
            if (hud != null)
            {
                hiddenHud = hud.GetComponentInParent<Canvas>().rootCanvas;
                hiddenHud.enabled = false;
            }
            foreach (var d in FindObjectsByType<EnemyDetector>(FindObjectsSortMode.None))
                Disable(d);
            if (arena.Rig != null)
            {
                arena.Rig.Input.Block(BlockReason);
                foreach (var c in arena.Rig.GetComponentsInChildren<Camera>())
                    if (c.enabled)
                    {
                        c.enabled = false;
                        disabledCameras.Add(c);
                    }
            }
            cam = new GameObject("SpectatorCamera").AddComponent<Camera>();
            cam.transform.SetParent(transform, false);
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            int hidden = 0;
            foreach (var layer in new[] { "Player", "Enemy", "ViewModel" })
            {
                int l = LayerMask.NameToLayer(layer);
                if (l >= 0)
                    hidden |= 1 << l;
            }
            cam.cullingMask = ~hidden; // the live bodies are not shown: their past is
            cam.enabled = false; // nothing until 10 s of past exist
        }

        void Disable(Behaviour b)
        {
            if (b != null && b.enabled)
            {
                b.enabled = false;
                disabledParts.Add(b);
            }
        }

        /// <summary>Ends spectating (the match ended, or leaving).</summary>
        public void Stop()
        {
            foreach (var g in ghosts.Values)
                Destroy(g);
            ghosts.Clear();
            Destroy(gameObject);
        }

        List<int> Candidates(float t)
        {
            var list = new List<int>();
            foreach (int slot in replay.Slots)
                if (slot != localSlot && replay.TryState(slot, t, out var s) && s.Alive)
                    list.Add(slot);
            list.Sort();
            return list;
        }

        public void Next() => Step(+1);
        public void Previous() => Step(-1);

        void Step(int dir)
        {
            var list = Candidates(PlaybackTime);
            if (list.Count == 0)
                return;
            int i = list.IndexOf(TargetSlot);
            TargetSlot = list[((i < 0 ? 0 : i + dir) % list.Count + list.Count) % list.Count];
            Apply();
        }

        /// <summary>The living participants of the shown time, in id order.</summary>
        public List<int> CurrentCandidates() => Waiting ? new List<int>() : Candidates(PlaybackTime);

        void LateUpdate()
        {
            float now = clock();
            if (!replay.TryPlaybackTime(now, out float playback))
            {
                Waiting = true;
                cam.enabled = false;
                return;
            }
            Waiting = false;
            PlaybackTime = playback;
            var list = Candidates(playback);
            if (!list.Contains(TargetSlot))
            {
                // First choice, or the watched one died at the shown time: the next living one after it.
                int next = list.Find(s => s > TargetSlot);
                TargetSlot = list.Count == 0 ? -1 : next != 0 ? next : list[0];
            }
            Apply();
            PlaySounds(playback);
            for (int i = shots.Count - 1; i >= 0; i--)
                if (Time.unscaledTime >= shots[i].until)
                {
                    Destroy(shots[i].line.gameObject);
                    shots.RemoveAt(i);
                }
        }

        void Apply()
        {
            foreach (int slot in replay.Slots)
            {
                bool has = replay.TryState(slot, PlaybackTime, out var s);
                var ghost = Ghost(slot);
                ghost.SetActive(has && slot != TargetSlot);
                if (!has)
                    continue;
                ghost.transform.SetPositionAndRotation(s.Position, s.Alive ? Quaternion.Euler(0f, s.Yaw, 0f) : Quaternion.Euler(90f, s.Yaw, 0f));
                if (slot == TargetSlot)
                {
                    TargetState = s;
                    cam.transform.SetPositionAndRotation(s.Position + Vector3.up * s.EyeHeight, Quaternion.Euler(-s.Pitch, s.Yaw, 0f));
                }
            }
            cam.enabled = TargetSlot >= 0;
        }

        GameObject Ghost(int slot)
        {
            if (ghosts.TryGetValue(slot, out var g))
                return g;
            g = new GameObject($"PastBody_{slot}");
            g.transform.SetParent(transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(g.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            var mat = body.GetComponent<MeshRenderer>().material;
            mat.color = slot == localSlot ? new Color(0.3f, 0.6f, 1f) : MatchArena.NpcColor(slot);
            if (lineMaterial == null)
                lineMaterial = new Material(mat) { color = new Color(1f, 0.85f, 0.4f) };
            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(face.GetComponent<Collider>());
            face.transform.SetParent(g.transform, false);
            face.transform.localPosition = new Vector3(0f, 1.5f, 0.35f);
            face.transform.localScale = new Vector3(0.5f, 0.15f, 0.15f);
            face.GetComponent<MeshRenderer>().material.color = Color.black;
            ghosts[slot] = g;
            return g;
        }

        void PlaySounds(float playback)
        {
            float from = float.IsNaN(lastPlayback) ? playback - Time.unscaledDeltaTime : lastPlayback;
            lastPlayback = playback;
            foreach (var e in replay.EventsBetween(from, playback))
            {
                PlayedSounds.Add(e);
                if (PlayedSounds.Count > 256)
                    PlayedSounds.RemoveAt(0);
                SoundSink?.Play(e, cam.transform.position, cam.transform.rotation);
                SoundPlayed?.Invoke(e);
                if (e.Kind == ReplayEventKind.Gunshot && e.SlotId != TargetSlot)
                    ShotLine(e);
            }
        }

        void ShotLine(ReplayEvent e)
        {
            var go = new GameObject("PastShot");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.03f;
            line.sharedMaterial = lineMaterial;
            line.SetPosition(0, e.Position);
            line.SetPosition(1, e.Position + e.Direction.normalized * ShotLineLength);
            shots.Add((line, Time.unscaledTime + 0.05f));
        }

        void OnDestroy()
        {
            foreach (var c in disabledCameras)
                if (c != null)
                    c.enabled = true;
            foreach (var b in disabledParts)
                if (b != null)
                    b.enabled = true;
            if (hiddenHud != null)
                hiddenHud.enabled = true;
            if (arena != null && arena.Rig != null)
                arena.Rig.Input.Unblock(BlockReason);
        }
    }
}
