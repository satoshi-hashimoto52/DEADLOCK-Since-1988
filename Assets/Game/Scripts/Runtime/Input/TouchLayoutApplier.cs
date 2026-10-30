using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Input
{
    /// <summary>
    /// Places the touch widgets according to a <see cref="TouchLayoutData"/>. Widgets are anchored at normalised
    /// Safe Area positions, so rotation and screen-size changes keep them in the same relative place; they are clamped
    /// on screen for the current size: up and down inside the Safe Area, sideways up to the screen's edges (the margins
    /// beside the Safe Area, <see cref="SideRoom"/>). Lives on the TouchControls root; widgets are found by name. HUD
    /// boxes of the layout (the ammo panel) live elsewhere and are attached by their owner (<see cref="Attach"/>); they
    /// are placed the same way in their own Safe Area, which has the same size.
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
        readonly Dictionary<string, System.Action<TouchLayoutItem>> boxes = new Dictionary<string, System.Action<TouchLayoutItem>>();
        RectTransform safeArea;
        TouchLayoutData saved;
        TouchLayoutData shown;
        Vector2 lastSafeSize;

        /// <summary>The applied-and-saved layout (a copy).</summary>
        public TouchLayoutData Saved => saved.Clone();
        /// <summary>The layout on screen now (a copy; differs from <see cref="Saved"/> while editing).</summary>
        public TouchLayoutData Shown => shown.Clone();
        public RectTransform SafeArea => safeArea;
        public Vector2 SafeSize => safeArea.rect.size;

        public RectTransform Widget(string id) => widgets[id];
        public bool HasWidget(string id) => widgets.ContainsKey(id);
        /// <summary>Screen margin left (x) and right (y) of the Safe Area, in canvas units: room to place widgets in.</summary>
        public Vector2 SideRoom => SideRoomOf(safeArea);

        /// <summary>
        /// A HUD box of the layout (<see cref="TouchLayout.Boxes"/>): its widget (for the layout editor's handle) and how
        /// to place it (called with the item, already clamped on screen, on every apply).
        /// </summary>
        public void Attach(string id, RectTransform widget, System.Action<TouchLayoutItem> place)
        {
            widgets[id] = widget;
            boxes[id] = place;
            if (shown != null)
                place(PlacedBox(shown.Get(id)));
        }

        /// <summary>A box item with its centre clamped on screen for the current Safe Area.</summary>
        public TouchLayoutItem PlacedBox(TouchLayoutItem item)
        {
            item.center = TouchLayout.ClampBox(item.center, TouchLayout.BoxSize(item), SafeSize, SideRoom);
            return item;
        }

        /// <summary>The screen's margins beside a Safe Area rectangle (canvas units, left / right).</summary>
        public static Vector2 SideRoomOf(RectTransform safe)
        {
            var canvas = safe != null ? safe.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
                return Vector2.zero;
            var screen = (RectTransform)canvas.rootCanvas.transform;
            var s = new Vector3[4];
            var c = new Vector3[4];
            safe.GetWorldCorners(s);
            screen.GetWorldCorners(c);
            float unit = Mathf.Max(1e-5f, safe.lossyScale.x);
            return new Vector2(Mathf.Max(0f, (s[0].x - c[0].x) / unit), Mathf.Max(0f, (c[2].x - s[2].x) / unit));
        }
        /// <summary>Goes up whenever the widgets are placed again (layout applied / edited / screen size changed).</summary>
        public int Version { get; private set; }

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
            Version++;
            lastSafeSize = safeArea.rect.size;
            Vector2 safe = lastSafeSize;
            Vector2 room = SideRoom;

            var zone = TouchLayout.ClampZone(layout.Get(TouchLayout.MoveZone), safe, room);
            var zoneRect = widgets[TouchLayout.MoveZone];
            zoneRect.anchorMin = zone.center - zone.size * 0.5f;
            zoneRect.anchorMax = zone.center + zone.size * 0.5f;
            zoneRect.offsetMin = zoneRect.offsetMax = Vector2.zero;

            foreach (string id in TouchLayout.Circles)
            {
                var item = layout.Get(id);
                float diameter = TouchLayout.Diameter(item);
                Vector2 center = TouchLayout.ClampCircle(item.center, diameter, safe, room);
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
            foreach (var pair in boxes)
                pair.Value(PlacedBox(layout.Get(pair.Key)));
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
