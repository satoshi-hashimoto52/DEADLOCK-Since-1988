using System;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Development builds on a device only: small frame-time readout at the top centre, plus a console line every
    /// <see cref="LogInterval"/> seconds (readable with `xcrun devicectl device process launch --console`): average and
    /// percentile frame times, long frames, memory, scene and player position. The first seconds after a scene load are
    /// reported on their own line (loading is not normal play), and single frames over 100 ms get a line with the place.
    /// Each line also says what the player did in those seconds, so a log can be matched to play: metres moved (summed
    /// every frame; jumps over 5 m in one frame, such as a respawn, are not counted), shots, melee swings, seconds
    /// paused (menu) and seconds with input blocked (menu, layout editor, round result). The player is the scene's
    /// <see cref="PlayerRig"/>, looked up again after every scene load (retry included); a line names the object.
    /// Never created in the Editor or in non-development builds (tests attach it by hand).
    /// </summary>
    public sealed class FrameRateMonitor : MonoBehaviour
    {
        /// <summary>Seconds per console line (shorter in tests).</summary>
        public float LogInterval { get; set; } = 10f;
        /// <summary>The player whose place and movement are logged (the scene's player rig).</summary>
        public Transform TrackedPlayer => player;
        /// <summary>Every line written (tests).</summary>
        public event Action<string> Logged;

        Text label;
        float windowStart;
        int windowFrames;
        float windowWorst;
        float logStart;
        int logFrames;
        float logWorst;
        readonly List<float> logTimes = new List<float>(1024);
        const float LoadWindow = 2f;
        float loadStart = -100f;
        string loadScene;
        int loadFrames;
        float loadWorst;
        int loadLong;
        Transform player;
        PlayerInputRouter router;
        GunController gun;
        MeleeWeapon melee;
        Vector3 lastPosition;
        bool hasLastPosition;
        float moved;
        int shots;
        int swings;
        float pausedTime;
        float blockedTime;
        float inactiveSince = -1f;   // realtime when the app lost focus / paused (-1 = active)
        float lastInactiveMs;        // length of the last inactive period, reported with the next frame
        bool afterInactive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Application.isEditor || !Debug.isDebugBuild)
                return;
            Attach();
        }

        /// <summary>Creates the monitor (development builds on start-up; tests by hand).</summary>
        public static FrameRateMonitor Attach()
        {
            var go = new GameObject("FrameRateMonitor");
            DontDestroyOnLoad(go);
            return go.AddComponent<FrameRateMonitor>();
        }

        void Log(string line)
        {
            Debug.Log(line);
            Logged?.Invoke(line);
        }

        void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(transform, false);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var rect = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(safe, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.sizeDelta = new Vector2(700f, 40f);
            label = rect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.UpperCenter;
            label.color = new Color(1f, 1f, 0.6f, 0.9f);
            label.raycastTarget = false;
            windowStart = logStart = Time.unscaledTime;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single); // the first scene loaded before this existed
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Track(null);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            loadStart = Time.unscaledTime;
            loadScene = scene.name;
            loadFrames = 0;
            loadWorst = 0f;
            loadLong = 0;
            // The scene's player rig (not an object found by name: other objects may share the name "Player").
            var rig = FindAnyObjectByType<PlayerRig>();
            Track(rig);
            Log($"[DEADLOCK][perf][scene] {scene.name}: tracking {(rig != null ? $"{rig.name} #{rig.GetInstanceID()}" : "no player")}");
        }

        void Track(PlayerRig rig)
        {
            if (gun != null)
                gun.Fired -= OnFired;
            if (melee != null)
                melee.SwingStarted -= OnSwing;
            player = rig != null ? rig.transform : null;
            router = rig != null ? rig.Input : null;
            gun = rig != null ? rig.Gun : null;
            melee = rig != null ? rig.Melee : null;
            if (gun != null)
                gun.Fired += OnFired;
            if (melee != null)
                melee.SwingStarted += OnSwing;
            hasLastPosition = false;
        }

        void OnFired() => shots++;

        void OnSwing() => swings++;

        /// <summary>What the player did since the last line (cheap: one distance per frame and two counters).</summary>
        void TrackPlay(float dt)
        {
            if (Time.timeScale == 0f)
                pausedTime += dt;
            if (router != null && router.IsBlocked)
                blockedTime += dt;
            if (player == null)
                return;
            Vector3 p = player.position;
            if (hasLastPosition)
            {
                float d = Vector3.Distance(p, lastPosition);
                if (d < 5f)
                    moved += d;
            }
            lastPosition = p;
            hasLastPosition = true;
        }

        string Activity() => $"moved {moved:0.0} m, shots {shots}, melee {swings}, paused {pausedTime:0.0} s, input blocked {blockedTime:0.0} s";

        void ResetActivity()
        {
            moved = pausedTime = blockedTime = 0f;
            shots = swings = 0;
        }

        string Place() => player != null ? $"{SceneManager.GetActiveScene().name} ({player.position.x:0.0}, {player.position.y:0.0}, {player.position.z:0.0})" : SceneManager.GetActiveScene().name;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            if (now - loadStart < LoadWindow)
            {
                // The load itself and the first frames after it: reported once, kept out of the play statistics.
                loadFrames++;
                loadWorst = Mathf.Max(loadWorst, dt);
                if (dt > 0.05f)
                    loadLong++;
                return;
            }
            if (loadScene != null)
            {
                Log($"[DEADLOCK][perf][load] {loadScene}: first {LoadWindow:0.0}s {loadFrames} frames, worst {loadWorst * 1000f:0.0} ms, {loadLong} over 50 ms");
                loadScene = null;
                ResetActivity();
                windowStart = logStart = now;
                windowFrames = logFrames = 0;
                windowWorst = logWorst = 0f;
                logTimes.Clear();
                return;
            }
            TrackPlay(dt);
            if (dt > 0.1f)
                Log($"[DEADLOCK][perf][hitch] {dt * 1000f:0.0} ms at {Place()}" + (afterInactive ? $" (first frame after {lastInactiveMs:0} ms inactive)" : ""));
            afterInactive = false;
            logTimes.Add(dt);
            windowFrames++;
            logFrames++;
            windowWorst = Mathf.Max(windowWorst, dt);
            logWorst = Mathf.Max(logWorst, dt);

            if (now - windowStart >= 0.5f)
            {
                float fps = windowFrames / (now - windowStart);
                label.text = $"DEV  {fps:0.0} fps  worst {windowWorst * 1000f:0.0} ms  target {Application.targetFrameRate}";
                windowStart = now;
                windowFrames = 0;
                windowWorst = 0f;
            }
            if (now - logStart >= LogInterval)
            {
                logTimes.Sort();
                float P(float q) => logTimes.Count == 0 ? 0f : logTimes[Mathf.Min(logTimes.Count - 1, (int)(logTimes.Count * q))] * 1000f;
                int over33 = 0, over50 = 0, over100 = 0;
                foreach (float t in logTimes)
                {
                    if (t > 0.0334f) over33++;
                    if (t > 0.05f) over50++;
                    if (t > 0.1f) over100++;
                }
                Log($"[DEADLOCK][perf] {now - logStart:0.0}s avg {logFrames / (now - logStart):0.0} fps, worst frame {logWorst * 1000f:0.0} ms, {Screen.width}x{Screen.height}" +
                          $" | p50 {P(0.5f):0.0} p95 {P(0.95f):0.0} p99 {P(0.99f):0.0} ms, >33ms {over33}, >50ms {over50}, >100ms {over100}" +
                          $" | mem alloc {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0} MB reserved {Profiler.GetTotalReservedMemoryLong() / 1048576f:0} MB mono {Profiler.GetMonoUsedSizeLong() / 1048576f:0} MB gfx {Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f:0} MB" +
                          $" | {Activity()} | {Place()}");
                ResetActivity();
                logTimes.Clear();
                logStart = now;
                logFrames = 0;
                logWorst = 0f;
            }
        }

        // Inactive periods (focus lost: system sheets / notification centre; paused: background) are timed, so a long
        // frame right after one can be told apart from a slow frame by the numbers.
        void OnApplicationFocus(bool focused) => TrackActive(focused);

        void TrackActive(bool active)
        {
            float now = Time.realtimeSinceStartup;
            if (!active)
            {
                if (inactiveSince < 0f)
                    inactiveSince = now;
                return;
            }
            if (inactiveSince >= 0f)
            {
                lastInactiveMs = (now - inactiveSince) * 1000f;
                afterInactive = true;
                Log($"[DEADLOCK][perf][inactive] {lastInactiveMs:0} ms at {Place()}");
                inactiveSince = -1f;
            }
        }

        // The first frame after returning from the background is not a real frame time.
        void OnApplicationPause(bool paused)
        {
            TrackActive(!paused);
            if (paused)
                return;
            windowStart = logStart = Time.unscaledTime;
            windowFrames = logFrames = 0;
            windowWorst = logWorst = 0f;
            logTimes.Clear();
            ResetActivity();
            hasLastPosition = false;
        }
    }
}
