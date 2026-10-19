using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Input
{
    /// <summary>
    /// Places the touch widgets according to a <see cref="TouchLayoutData"/>. Widgets are anchored at normalised
    /// Safe Area positions, so rotation and screen-size changes keep them in the same relative place; circles are
    /// clamped inside the Safe Area for the current size. Lives on the TouchControls root; widgets are found by name.
    /// </summary>
    public sealed class TouchLayoutApplier : MonoBehaviour
    {
        static readonly Dictionary<string, string> WidgetNames = new Dictionary<string, string>
        {
            { TouchLayout.MoveZone, "MoveZone" },
            { TouchLayout.MoveStick, "StickBase" },
            { TouchLayout.Fire, "FireButton" },
            { TouchLayout.Ads, "AdsButton" },
            { TouchLayout.Reload, "ReloadButton" },
            { TouchLayout.Jump, "JumpButton" },
            { TouchLayout.Posture, "PostureButton" },
            { TouchLayout.Weapon, "WeaponButton" },
        };

        readonly Dictionary<string, RectTransform> widgets = new Dictionary<string, RectTransform>();
        RectTransform safeArea;
        TouchLayoutData saved;
        TouchLayoutData shown;
        Vector2 lastSafeSize;

        /// <summary>The applied-and-saved layout (a copy).</summary>
        public TouchLayoutData Saved => saved.Clone();
        public RectTransform SafeArea => safeArea;
        public Vector2 SafeSize => safeArea.rect.size;

        public RectTransform Widget(string id) => widgets[id];

        void Awake()
        {
            foreach (var pair in WidgetNames)
            {
                var found = FindDeep(transform, pair.Value);
                if (found != null)
                    widgets[pair.Key] = (RectTransform)found;
            }
            safeArea = (RectTransform)widgets[TouchLayout.Fire].parent;
            saved = TouchLayout.Load();
            shown = saved.Clone();
        }

        void OnEnable() => lastSafeSize = Vector2.zero; // re-apply once visible again

        void LateUpdate()
        {
            // Rotation / resize / safe-area change: keep everything inside the new Safe Area.
            if (safeArea.rect.size != lastSafeSize)
                Apply(shown);
        }

        /// <summary>Shows a layout without saving it (live preview while editing).</summary>
        public void Apply(TouchLayoutData layout)
        {
            shown = layout.Clone();
            lastSafeSize = safeArea.rect.size;
            Vector2 safe = lastSafeSize;

            var zone = TouchLayout.ClampZone(layout.Get(TouchLayout.MoveZone));
            var zoneRect = widgets[TouchLayout.MoveZone];
            zoneRect.anchorMin = zone.center - zone.size * 0.5f;
            zoneRect.anchorMax = zone.center + zone.size * 0.5f;
            zoneRect.offsetMin = zoneRect.offsetMax = Vector2.zero;

            foreach (string id in TouchLayout.Circles)
            {
                var item = layout.Get(id);
                float diameter = TouchLayout.Diameter(item);
                Vector2 center = TouchLayout.ClampCircle(item.center, diameter, safe);
                var rect = widgets[id];
                if (id == TouchLayout.MoveStick)
                {
                    // The stick base is a child of the zone: its rest point is expressed in zone space.
                    Vector2 local = new Vector2(
                        Mathf.InverseLerp(zoneRect.anchorMin.x, zoneRect.anchorMax.x, center.x),
                        Mathf.InverseLerp(zoneRect.anchorMin.y, zoneRect.anchorMax.y, center.y));
                    rect.anchorMin = rect.anchorMax = local;
                }
                else
                {
                    rect.anchorMin = rect.anchorMax = center;
                }
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(diameter, diameter);
            }
        }

        /// <summary>Applies and stores a layout on the device (restored on the next launch).</summary>
        public void Commit(TouchLayoutData layout)
        {
            saved = layout.Clone();
            TouchLayout.Save(saved);
            Apply(saved);
        }

        /// <summary>Drops a preview and shows the saved layout again.</summary>
        public void Revert() => Apply(saved);

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
