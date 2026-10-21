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
    /// and the allocated memory, and saves one screenshot of the screen as shown (Documents/equip_NN_*.png, at most
    /// <see cref="MaxCaptures"/> per run). Nothing on screen or in the controls changes.
    /// </summary>
    public sealed class EquipmentScreenDiagnostics : MonoBehaviour
    {
        const int MaxCaptures = 40;
        const float WindowSeconds = 1f;
        const float CaptureAfterSeconds = 1.2f;

        GameMenu menu;
        int opens;
        int captures;

        public static bool Enabled => Debug.isDebugBuild && !Application.isEditor;

        public void Init(GameMenu menu)
        {
            this.menu = menu;
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
            if (captures >= MaxCaptures)
                yield break;
            yield return new WaitForSecondsRealtime(CaptureAfterSeconds - WindowSeconds);
            string file = $"equip_{++captures:00}_{what}.png";
            ScreenCapture.CaptureScreenshot(file); // mobile: saved in Application.persistentDataPath (Documents)
            Debug.Log($"[DEADLOCK][equip] capture {file} at {Time.realtimeSinceStartup:0.00}s (frame time of this moment is the capture, not the screen)");
        }
    }
}
