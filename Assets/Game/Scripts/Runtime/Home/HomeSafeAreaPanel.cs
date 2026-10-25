using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>
    /// Keeps a RectTransform (child of a screen-space canvas) on Screen.safeArea, refitted when the safe area or the
    /// screen size changes. The home menu UI (mode select, equipment, skills, gacha, points, settings; not made yet)
    /// goes under it; the background is not limited by it.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HomeSafeAreaPanel : MonoBehaviour
    {
        Rect applied;
        Vector2Int appliedScreen;

        void OnEnable() => Fit(Screen.safeArea, Screen.width, Screen.height);

        void Update()
        {
            if (Screen.safeArea != applied || Screen.width != appliedScreen.x || Screen.height != appliedScreen.y)
                Fit(Screen.safeArea, Screen.width, Screen.height);
        }

        /// <summary>Sets the anchors to a safe area given in screen px (public for tests).</summary>
        public void Fit(Rect safeArea, int screenWidth, int screenHeight)
        {
            applied = safeArea;
            appliedScreen = new Vector2Int(screenWidth, screenHeight);
            if (screenWidth <= 0 || screenHeight <= 0)
                return;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safeArea.xMin / screenWidth, safeArea.yMin / screenHeight);
            rt.anchorMax = new Vector2(safeArea.xMax / screenWidth, safeArea.yMax / screenHeight);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
