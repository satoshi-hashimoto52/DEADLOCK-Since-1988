using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>
    /// Candle animation of the home screen: one shared frame index 0 -> 1 -> 2 -> 3 -> 0 every
    /// <see cref="HomeLightingSettings.frameInterval"/> seconds drives three sprite renderers.
    /// Flame and light show frames 00-03; the glow shows 01, 01, 02, 03 (glow 00 is not used: it drew a flat disc
    /// with a visible edge). Only sprites are swapped: each layer's frames share one canvas size and position, so
    /// the flame base, glow centre and light placement never move and nothing else is repositioned.
    /// The light layer uses the shared alpha <see cref="HomeLightingSettings.lightAlpha"/>.
    /// </summary>
    public sealed class HomeCandleAnimator : MonoBehaviour
    {
        public const int FrameCount = 4;
        static readonly int[] GlowSequence = { 1, 1, 2, 3 };

        public HomeLightingSettings settings;
        public SpriteRenderer flameRenderer, glowRenderer, lightRenderer;
        [Tooltip("candle_flame_00..03")] public Sprite[] flameFrames = new Sprite[FrameCount];
        [Tooltip("candle_glow_00..03; element 0 is never shown and left empty, so glow 00 is not in the build")] public Sprite[] glowFrames = new Sprite[FrameCount];
        [Tooltip("candle_light_00..03")] public Sprite[] lightFrames = new Sprite[FrameCount];
        public bool playing = true;

        public int Frame { get; private set; } = -1;

        public static int FlameIndex(int frame) => frame;
        public static int GlowIndex(int frame) => GlowSequence[frame];
        public static int LightIndex(int frame) => frame;

        /// <summary>Frame shown at a time (seconds) for an interval.</summary>
        public static int FrameAt(float time, float interval) => Mathf.FloorToInt(time / interval) % FrameCount;

        float Interval => settings != null ? settings.frameInterval : HomeLightingSettings.DefaultFrameInterval;
        float LightAlpha => settings != null ? settings.lightAlpha : HomeLightingSettings.DefaultLightAlpha;

        void OnEnable() => SetFrame(FrameAt(Time.time, Interval));

        void Update()
        {
            if (!playing)
                return;
            int frame = FrameAt(Time.time, Interval);
            if (frame != Frame)
                SetFrame(frame);
            else
                ApplyLightAlpha(); // follows a live change of the setting
        }

        public void SetFrame(int frame)
        {
            Frame = frame;
            flameRenderer.sprite = flameFrames[FlameIndex(frame)];
            glowRenderer.sprite = glowFrames[GlowIndex(frame)];
            lightRenderer.sprite = lightFrames[LightIndex(frame)];
            ApplyLightAlpha();
        }

        void ApplyLightAlpha()
        {
            var c = lightRenderer.color;
            if (!Mathf.Approximately(c.a, LightAlpha))
                lightRenderer.color = new Color(c.r, c.g, c.b, LightAlpha);
        }
    }
}
