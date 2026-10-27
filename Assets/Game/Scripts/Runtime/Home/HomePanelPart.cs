using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Home
{
    /// <summary>
    /// One home panel (button, window or row). Drawn in Unity UI by default (dark fill, brass lines, rivets). With panel
    /// images set it shows them as two 9-slice layers: the fill on the panel's own Image (so button state colours still
    /// tint it, alpha from <see cref="HomePanelLook"/>) and the frame with its rivets on a child above it (always opaque).
    /// The slice border is 20 design px, made smaller on small panels so the corners never cover the content.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomePanelPart : MonoBehaviour
    {
        /// <summary>Slice border on screen at full size (design px).</summary>
        public const float BorderDesignPx = 20f;
        /// <summary>The border never takes more than this share of the panel's shorter side.</summary>
        public const float MaxBorderShare = 0.3f;
        /// <summary>"準備中" rows: fill alpha relative to the others (as the Unity-drawn 0.6 vs 0.84).</summary>
        public const float DimFactor = 0.6f / 0.84f;

        public HomePanelTarget target;
        public bool dimmed;

        Image fill;
        Image frame;
        Color drawnColor;
        Sprite frameSprite, fillSprite;

        public bool Skinned => fillSprite != null && frameSprite != null;
        public Image Fill => fill != null ? fill : fill = GetComponent<Image>();
        public Image FrameImage => frame;

        /// <summary>Border on screen (canvas units) for a panel of this size.</summary>
        public static float BorderUnits(Vector2 size) => Mathf.Min(BorderDesignPx, Mathf.Min(size.x, size.y) * MaxBorderShare);

        /// <summary>Shows the panel images (both set) or goes back to the Unity-drawn panel (either null).</summary>
        public void SetSkin(Sprite frameImage, Sprite fillImage)
        {
            bool skin = frameImage != null && fillImage != null;
            if (skin && !Skinned)
                drawnColor = Fill.color;
            frameSprite = skin ? frameImage : null;
            fillSprite = skin ? fillImage : null;
            foreach (Transform c in transform)
                if (c.name == "Frame" || c.name == "Rivet")
                    c.gameObject.SetActive(!skin);
            var outline = GetComponent<Outline>();
            if (outline != null)
                outline.enabled = !skin;
            if (skin)
            {
                if (frame == null)
                {
                    var r = new GameObject("PanelFrame", typeof(RectTransform)).GetComponent<RectTransform>();
                    r.SetParent(transform, false);
                    r.anchorMin = Vector2.zero;
                    r.anchorMax = Vector2.one;
                    r.offsetMin = r.offsetMax = Vector2.zero;
                    r.SetSiblingIndex(0); // above the fill, below the icon and text
                    frame = r.gameObject.AddComponent<Image>();
                    frame.raycastTarget = false;
                    frame.type = Image.Type.Sliced;
                }
                frame.gameObject.SetActive(true);
                frame.sprite = frameSprite;
                frame.color = Color.white;
                Fill.sprite = fillSprite;
                Fill.type = Image.Type.Sliced;
                Fill.fillCenter = true;
            }
            else
            {
                if (frame != null)
                    frame.gameObject.SetActive(false);
                Fill.sprite = null;
                Fill.type = Image.Type.Simple;
                Fill.pixelsPerUnitMultiplier = 1f;
                Fill.color = drawnColor.a > 0f ? drawnColor : Fill.color;
            }
            Refresh();
        }

        /// <summary>
        /// Alpha the fill is drawn with: its Image alpha times its renderer alpha, which a button's state colour sets
        /// (colour × colour multiplier, alpha included), capped at 1 by the shader.
        /// </summary>
        public float ShownAlpha => Mathf.Min(1f, Fill.color.a * Fill.canvasRenderer.GetColor().a);

        /// <summary>Applies the current transparency and border (images shown only).</summary>
        public void Refresh()
        {
            if (!Skinned)
                return;
            float a = HomePanelLook.FillAlpha(HomePanelLook.Get(target)) * (dimmed ? DimFactor : 1f);
            // A button's state colours are multiplied (×1.15 on the home buttons) into the drawn alpha as well: without
            // this, any fill alpha above 1/1.15 would be drawn fully opaque (0–13 % looked the same).
            var button = GetComponent<Button>();
            float tint = button != null && button.transition == Selectable.Transition.ColorTint ? button.colors.normalColor.a * button.colors.colorMultiplier : 1f;
            Fill.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a / tint));
            UpdateBorder();
        }

        void OnRectTransformDimensionsChange() => UpdateBorder();

        void UpdateBorder()
        {
            if (!Skinned || frame == null)
                return;
            var canvas = GetComponentInParent<Canvas>();
            float refPpu = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            // Border in canvas units = border px * refPpu / spritePpu / multiplier.
            float units = BorderUnits(((RectTransform)transform).rect.size);
            float m = units > 0f ? frameSprite.border.x * refPpu / frameSprite.pixelsPerUnit / units : 1f;
            frame.pixelsPerUnitMultiplier = m;
            Fill.pixelsPerUnitMultiplier = m;
        }
    }
}
