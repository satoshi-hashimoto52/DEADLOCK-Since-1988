using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Development builds on a device only: small frame-time readout at the top centre, plus a console line every
    /// <see cref="LogInterval"/> seconds (readable with `xcrun devicectl device process launch --console`).
    /// Never created in the Editor or in non-development builds.
    /// </summary>
    public sealed class FrameRateMonitor : MonoBehaviour
    {
        const float LogInterval = 10f;

        Text label;
        float windowStart;
        int windowFrames;
        float windowWorst;
        float logStart;
        int logFrames;
        float logWorst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Application.isEditor || !Debug.isDebugBuild)
                return;
            var go = new GameObject("FrameRateMonitor");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameRateMonitor>();
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
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
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
                Debug.Log($"[DEADLOCK][perf] {now - logStart:0.0}s avg {logFrames / (now - logStart):0.0} fps, worst frame {logWorst * 1000f:0.0} ms, {Screen.width}x{Screen.height}");
                logStart = now;
                logFrames = 0;
                logWorst = 0f;
            }
        }

        // The first frame after returning from the background is not a real frame time.
        void OnApplicationPause(bool paused)
        {
            if (paused)
                return;
            windowStart = logStart = Time.unscaledTime;
            windowFrames = logFrames = 0;
            windowWorst = logWorst = 0f;
        }
    }
}
