using UnityEngine;

namespace Deadlock.UI
{
    /// <summary>
    /// Fits this RectTransform to <see cref="Screen.safeArea"/> (notch / home indicator). Children laid out
    /// inside it stay reachable and visible on devices with cut-outs.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect applied;
        Vector2Int appliedScreen;

        void OnEnable() => Apply(force: true);

        void Update() => Apply(force: false);

        void Apply(bool force)
        {
            Rect safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == applied && screen == appliedScreen)
                return;
            if (screen.x <= 0 || screen.y <= 0)
                return;

            applied = safe;
            appliedScreen = screen;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            rect.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
