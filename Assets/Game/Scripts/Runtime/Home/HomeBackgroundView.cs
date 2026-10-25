using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>
    /// Shows the home background on screen: keeps the home camera on the reference centre and applies the framing
    /// (<see cref="HomeFraming"/>) for the current screen, again whenever the screen size changes (rotation,
    /// resolution, window resize). The background is never limited to the safe area; only the menu root
    /// (<see cref="HomeSafeAreaPanel"/>) is.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class HomeBackgroundView : MonoBehaviour
    {
        Camera cam;
        Vector2Int appliedSize;

        public HomeFramingMode Mode { get; private set; }

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
        }

        void OnEnable() => Apply(Screen.width, Screen.height);

        void LateUpdate()
        {
            if (Screen.width != appliedSize.x || Screen.height != appliedSize.y)
                Apply(Screen.width, Screen.height);
        }

        /// <summary>Frames the background for a screen of the given size (public for tests and tools).</summary>
        public void Apply(int width, int height)
        {
            if (cam == null)
                cam = GetComponent<Camera>();
            appliedSize = new Vector2Int(width, height);
            if (width <= 0 || height <= 0)
                return;
            Mode = HomeFraming.Choose(width, height);
            cam.transform.position = HomeFraming.CameraPosition(cam.transform.position.z);
            cam.orthographicSize = HomeFraming.OrthoSize(Mode, (float)width / height);
        }
    }
}
