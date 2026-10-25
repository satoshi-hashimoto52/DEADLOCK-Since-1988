using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>
    /// Adjustable candle lighting values of the home screen (asset: Assets/Game/Data/HomeLighting.asset).
    /// Adopted 2026-10-25 after the device check: light (reflection) alpha 0.65 shared by the four light frames,
    /// frame interval 0.15 s. The PNGs are never changed; the alpha is a renderer colour.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Home Lighting Settings")]
    public sealed class HomeLightingSettings : ScriptableObject
    {
        public const float DefaultLightAlpha = 0.65f;
        public const float DefaultFrameInterval = 0.15f;

        [Tooltip("Renderer alpha of the candle light (reflection) layer, shared by frames 00-03.")]
        [Range(0f, 1f)] public float lightAlpha = DefaultLightAlpha;

        [Tooltip("Seconds per candle frame (flame, glow and light change together).")]
        [Min(0.01f)] public float frameInterval = DefaultFrameInterval;
    }
}
