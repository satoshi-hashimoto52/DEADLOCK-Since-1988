using System;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>Tiny helpers for building prototype uGUI at runtime (menu, result panel, layout editor).</summary>
    public static class UiBuild
    {
        static Font font;

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>Anchors at a normalised point of the parent with the given size.</summary>
        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Image(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Text(RectTransform rect, string content, int size, TextAnchor alignment, Color? color = null)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button Button(RectTransform parent, string name, string label, Vector2 anchor, Vector2 position, Vector2 size, Action onClick, int fontSize = 30)
        {
            var rect = Place(Rect(name, parent), anchor, position, size);
            var image = Image(rect, new Color(0.15f, 0.16f, 0.2f, 0.92f), true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.85f, 0.4f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            Text(Stretch(Rect("Label", rect)), label, fontSize, TextAnchor.MiddleCenter);
            return button;
        }

        /// <summary>Single-line text field (opens the on-screen keyboard on mobile).</summary>
        public static InputField InputField(RectTransform parent, string name, string placeholder, Vector2 anchor, Vector2 position, Vector2 size,
            int fontSize = 30, UnityEngine.UI.InputField.ContentType contentType = UnityEngine.UI.InputField.ContentType.Standard)
        {
            var rect = Place(Rect(name, parent), anchor, position, size);
            var image = Image(rect, new Color(0.05f, 0.06f, 0.08f, 0.95f), true);
            var textRect = Stretch(Rect("Text", rect));
            textRect.offsetMin = new Vector2(16f, 0f);
            textRect.offsetMax = new Vector2(-16f, 0f);
            var text = Text(textRect, "", fontSize, TextAnchor.MiddleLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            var hintRect = Stretch(Rect("Placeholder", rect));
            hintRect.offsetMin = textRect.offsetMin;
            hintRect.offsetMax = textRect.offsetMax;
            var hint = Text(hintRect, placeholder, fontSize, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
            var field = rect.gameObject.AddComponent<UnityEngine.UI.InputField>();
            field.targetGraphic = image;
            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.lineType = UnityEngine.UI.InputField.LineType.SingleLine;
            return field;
        }
    }
}
