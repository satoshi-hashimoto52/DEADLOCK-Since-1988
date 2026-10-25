using UnityEngine;

namespace Deadlock.Home
{
    public enum HomeFramingMode
    {
        /// <summary>The whole 1672x941 reference is visible; the rest of the screen is black.</summary>
        Fit,
        /// <summary>The screen is filled; the overflowing side of the reference is cropped equally at both ends.</summary>
        Cover,
    }

    /// <summary>
    /// Screen framing of the home background (docs/HOME_BACKGROUND_ASSET_SPEC.md). Every home layer is placed in the
    /// same 1672x941 reference space (1 unit = 1 reference px, origin top left, y down = -y) and one camera shows it,
    /// so the framing only changes the camera's orthographic size: all layers share one scale and keep their
    /// relative positions. The mode is chosen from the screen aspect, not from the device name:
    /// screens at least as wide as the reference (iPhone landscape) use Cover, which crops only the top and bottom;
    /// narrower screens (iPad 4:3, 16:10) use Fit, because Cover would crop the sides and push the candle off screen
    /// (Fit there is provisional).
    /// </summary>
    public static class HomeFraming
    {
        public const float RefWidth = 1672f, RefHeight = 941f;
        public const float RefAspect = RefWidth / RefHeight;
        /// <summary>Tolerance so a 16:9 screen (1.7778 vs 1.7768) counts as "as wide as the reference".</summary>
        const float AspectTolerance = 0.002f;

        public static HomeFramingMode Choose(float width, float height) =>
            width / height >= RefAspect - AspectTolerance ? HomeFramingMode.Cover : HomeFramingMode.Fit;

        /// <summary>Camera orthographic size (half the visible height, reference px) for a screen aspect.</summary>
        public static float OrthoSize(HomeFramingMode mode, float aspect)
        {
            float byHeight = RefHeight / 2f, byWidth = RefWidth / 2f / aspect;
            return mode == HomeFramingMode.Cover ? Mathf.Min(byHeight, byWidth) : Mathf.Max(byHeight, byWidth);
        }

        /// <summary>Screen px per reference px.</summary>
        public static float Scale(HomeFramingMode mode, float width, float height) => height / (2f * OrthoSize(mode, width / height));

        /// <summary>Visible part of the reference (x, y, w, h in reference px, y down); outside 0..1672 / 0..941 is black.</summary>
        public static Rect Visible(HomeFramingMode mode, float width, float height)
        {
            float s = Scale(mode, width, height), w = width / s, h = height / s;
            return new Rect((RefWidth - w) / 2f, (RefHeight - h) / 2f, w, h);
        }

        /// <summary>Unity world position of a reference point (top-left origin, y down).</summary>
        public static Vector3 ToWorld(Vector2 refPosition) => new Vector3(refPosition.x, -refPosition.y, 0f);

        /// <summary>Camera position looking at the centre of the reference.</summary>
        public static Vector3 CameraPosition(float z) => new Vector3(RefWidth / 2f, -RefHeight / 2f, z);
    }
}
