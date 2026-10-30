using System;
using System.Collections;
using Deadlock.View;
using UnityEngine;
using UnityEngine.Profiling;

namespace Deadlock.UI
{
    /// <summary>
    /// Development builds on a device only (never in the Editor or a release build): log lines for checking the
    /// equipment screen on the device. For each opening / closing / skin change it logs the worst frame in the
    /// following second, then (after that window, so the counting is not measured) the number of materials / textures
    /// and the allocated memory. Screenshots only when asked for at launch (<see cref="CaptureVariable"/>=1): one of the
    /// screen as shown per event, at most <see cref="MaxCaptures"/> per run, in a new folder of the development capture
    /// store for each launch (<see cref="DevCaptureStore"/>, Library/Caches/DeadlockDevCaptures/equip/&lt;run&gt;; earlier
    /// captures are never removed, the store's limits stop writing instead). Opening the screen
    /// alone never saves a file (2026-10-28: the automatic captures filled the device). Nothing on screen changes.
    /// </summary>
    public sealed class EquipmentScreenDiagnostics : MonoBehaviour
    {
        const int MaxCaptures = 40;
        public const string CaptureVariable = "DEADLOCK_EQUIP_CAPTURE";
        const string CaptureSource = "equip";
        const float WindowSeconds = 1f;
        const float CaptureAfterSeconds = 1.2f;

        GameMenu menu;
        int opens;
        int captures;

        public static bool Enabled => Debug.isDebugBuild && !Application.isEditor;

        /// <summary>Screenshots only with the launch variable set to 1, and only where development captures are allowed.</summary>
        public static bool CaptureRequested(Func<string, string> environment) =>
            DevCaptureStore.Allowed && environment(CaptureVariable) == "1";

        bool capture;
        DevCaptureRun run;

        public void Init(GameMenu menu)
        {
            this.menu = menu;
            capture = CaptureRequested(Environment.GetEnvironmentVariable);
            menu.SkinSettingsOpened += () => Measure($"open{++opens}");
            menu.SkinSettingsClosed += () => Measure("close");
            SkinSelection.Changed += OnSkinChanged;
        }

        void OnDestroy() => SkinSelection.Changed -= OnSkinChanged;

        void OnSkinChanged() => Measure($"skin_w-{SkinSelection.GetWeaponSkinId("proto_rifle")}_p-{SkinSelection.PlayerSkinId}");

        void Measure(string what)
        {
            if (isActiveAndEnabled)
                StartCoroutine(Run(what));
        }

        IEnumerator Run(string what)
        {
            float start = Time.realtimeSinceStartup;
            Debug.Log($"[DEADLOCK][equip] {what} at {start:0.00}s (screen open: {menu.IsSkinSettingsOpen})");
            float worst = 0f;
            int frames = 0;
            while (Time.realtimeSinceStartup - start < WindowSeconds)
            {
                yield return null;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                frames++;
            }
            Debug.Log($"[DEADLOCK][equip] {what}: worst frame {worst * 1000f:0.0} ms over {frames} frames in the first {WindowSeconds:0.0} s");
            Debug.Log($"[DEADLOCK][equip] {what}: materials {Resources.FindObjectsOfTypeAll<Material>().Length}, textures {Resources.FindObjectsOfTypeAll<Texture>().Length}, " +
                      $"allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0} MB, reserved {Profiler.GetTotalReservedMemoryLong() / 1048576f:0.0} MB");
            if (!capture || captures >= MaxCaptures)
                yield break;
            yield return new WaitForSecondsRealtime(CaptureAfterSeconds - WindowSeconds);
            yield return new WaitForEndOfFrame();
            string file = $"equip_{++captures:00}_{what}.png";
            run ??= DevCaptureStore.Device.BeginRun(CaptureSource);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            string path = run?.Write(file, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log(path != null
                ? $"[DEADLOCK][equip] capture {path} at {Time.realtimeSinceStartup:0.00}s (frame time of this moment is the capture, not the screen)"
                : "[DEADLOCK][equip] capture not saved (capture store limit reached or not available; nothing was removed)");
        }
    }
}
