using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Input
{
    /// <summary>One editable touch widget. Positions are normalised to the Safe Area (0..1, y up).</summary>
    [Serializable]
    public struct TouchLayoutItem
    {
        public string id;
        /// <summary>Centre of the widget (for the move zone: centre of the rectangle).</summary>
        public Vector2 center;
        /// <summary>Move zone only: normalised width / height of the touch area.</summary>
        public Vector2 size;
        /// <summary>Buttons / stick: size multiplier of the default diameter.</summary>
        public float scale;
    }

    [Serializable]
    public sealed class TouchLayoutData
    {
        public int version = 1;
        public List<TouchLayoutItem> items = new List<TouchLayoutItem>();

        public TouchLayoutData Clone()
        {
            return new TouchLayoutData { version = version, items = new List<TouchLayoutItem>(items) };
        }

        public int IndexOf(string id) => items.FindIndex(i => i.id == id);

        public TouchLayoutItem Get(string id) => items[IndexOf(id)];

        public void Set(TouchLayoutItem item)
        {
            int index = IndexOf(item.id);
            if (index >= 0)
                items[index] = item;
            else
                items.Add(item);
        }
    }

    /// <summary>
    /// Initial (provisional) touch layout, the rules that keep an edited layout usable, and device-local storage.
    /// </summary>
    public static class TouchLayout
    {
        public const string MoveZone = "move_zone";
        public const string MoveStick = "move_stick";
        public const string Fire = "fire";
        public const string Ads = "ads";
        public const string Reload = "reload";
        public const string Jump = "jump";
        public const string Posture = "posture";
        public const string Weapon = "weapon";

        public const string PrefsKey = "deadlock.touchLayout.v1";

        public const float MinScale = 0.6f;
        public const float MaxScale = 1.6f;
        public static readonly Vector2 MinZoneSize = new Vector2(0.15f, 0.2f);
        public static readonly Vector2 MaxZoneSize = new Vector2(0.6f, 1f);

        // The menu button (top centre of the Safe Area, canvas units). Only it, plus a small margin against mis-taps, is
        // kept free: everything else in the Safe Area, including the top corners at the menu's height, may hold controls.
        // Non-interactive HUD text is not protected (controls may cover it).
        public static readonly Vector2 MenuButtonSize = new Vector2(220f, 64f);
        public const float MenuButtonTopOffset = 10f;
        public const float MenuGuardMargin = 24f;
        /// <summary>Circles may touch but not overlap more than this fraction of the sum of their radii.</summary>
        public const float AllowedOverlap = 0.1f;

        /// <summary>Default diameters in canvas units (reference height 1080).</summary>
        public static float BaseDiameter(string id) => id switch
        {
            MoveStick => 280f,
            Fire => 240f,
            Ads => 160f,
            _ => id == Posture || id == Weapon ? 130f : 140f,
        };

        public static readonly string[] Circles = { MoveStick, Fire, Ads, Reload, Jump, Posture, Weapon };

        public static string DisplayName(string id) => id switch
        {
            MoveZone => "移動の操作開始領域",
            MoveStick => "移動スティック",
            Fire => "FIRE",
            Ads => "ADS",
            Reload => "リロード",
            Jump => "ジャンプ",
            Posture => "姿勢",
            Weapon => "武器切り替え",
            _ => id,
        };

        /// <summary>Same arrangement as the original prototype layout (on a 1920x1080 Safe Area).</summary>
        public static TouchLayoutData Defaults()
        {
            var data = new TouchLayoutData();
            data.items.Add(new TouchLayoutItem { id = MoveZone, center = new Vector2(0.2f, 0.4f), size = new Vector2(0.4f, 0.8f), scale = 1f });
            data.items.Add(Circle(MoveStick, 0.135f, 0.231f));
            data.items.Add(Circle(Fire, 0.870f, 0.241f));
            data.items.Add(Circle(Ads, 0.729f, 0.157f));
            data.items.Add(Circle(Reload, 0.911f, 0.500f));
            data.items.Add(Circle(Jump, 0.755f, 0.398f));
            data.items.Add(Circle(Posture, 0.602f, 0.139f));
            data.items.Add(Circle(Weapon, 0.911f, 0.676f));
            return data;
        }

        static TouchLayoutItem Circle(string id, float x, float y) =>
            new TouchLayoutItem { id = id, center = new Vector2(x, y), size = Vector2.zero, scale = 1f };

        public static float Diameter(TouchLayoutItem item) => BaseDiameter(item.id) * item.scale;

        /// <summary>Centre clamped so the whole circle stays inside the Safe Area.</summary>
        public static Vector2 ClampCircle(Vector2 center, float diameter, Vector2 safeSize)
        {
            float rx = diameter * 0.5f / Mathf.Max(1f, safeSize.x);
            float ry = diameter * 0.5f / Mathf.Max(1f, safeSize.y);
            return new Vector2(
                Mathf.Clamp(center.x, rx, Mathf.Max(rx, 1f - rx)),
                Mathf.Clamp(center.y, ry, Mathf.Max(ry, 1f - ry)));
        }

        public static TouchLayoutItem ClampZone(TouchLayoutItem zone)
        {
            zone.size = Vector2.Max(MinZoneSize, Vector2.Min(MaxZoneSize, zone.size));
            Vector2 half = zone.size * 0.5f;
            zone.center = new Vector2(
                Mathf.Clamp(zone.center.x, half.x, 1f - half.x),
                Mathf.Clamp(zone.center.y, half.y, 1f - half.y));
            return zone;
        }

        /// <summary>The menu button plus its margin, in canvas units with the origin at the Safe Area's bottom left.</summary>
        public static Rect MenuGuard(Vector2 safeSize)
        {
            float halfWidth = MenuButtonSize.x * 0.5f + MenuGuardMargin;
            float height = MenuButtonTopOffset + MenuButtonSize.y + MenuGuardMargin;
            return new Rect(safeSize.x * 0.5f - halfWidth, safeSize.y - height, halfWidth * 2f, height);
        }

        static bool CircleHitsRect(Vector2 center, float radius, Rect rect)
        {
            float cx = Mathf.Clamp(center.x, rect.xMin, rect.xMax);
            float cy = Mathf.Clamp(center.y, rect.yMin, rect.yMax);
            return (new Vector2(cx, cy) - center).sqrMagnitude < radius * radius;
        }

        /// <summary>
        /// A layout is usable when no two circles overlap (beyond a small tolerance), nothing covers the menu button or
        /// its margin, and the stick rests inside the move zone. Checked in canvas units for the current Safe Area size.
        /// </summary>
        public static bool IsUsable(TouchLayoutData data, Vector2 safeSize, out string problem)
        {
            problem = null;
            for (int a = 0; a < Circles.Length; a++)
            for (int b = a + 1; b < Circles.Length; b++)
            {
                var ia = data.Get(Circles[a]);
                var ib = data.Get(Circles[b]);
                Vector2 pa = Vector2.Scale(ClampCircle(ia.center, Diameter(ia), safeSize), safeSize);
                Vector2 pb = Vector2.Scale(ClampCircle(ib.center, Diameter(ib), safeSize), safeSize);
                float minDistance = (Diameter(ia) + Diameter(ib)) * 0.5f * (1f - AllowedOverlap);
                if (Vector2.Distance(pa, pb) < minDistance)
                {
                    problem = $"{DisplayName(ia.id)}と{DisplayName(ib.id)}が重なっています";
                    return false;
                }
            }
            Rect guard = MenuGuard(safeSize);
            foreach (var id in Circles)
            {
                var item = data.Get(id);
                Vector2 p = Vector2.Scale(ClampCircle(item.center, Diameter(item), safeSize), safeSize);
                if (CircleHitsRect(p, Diameter(item) * 0.5f, guard))
                {
                    problem = $"{DisplayName(id)}がメニューボタンに重なっています";
                    return false;
                }
            }
            var zone = ClampZone(data.Get(MoveZone));
            var zoneRect = new Rect(Vector2.Scale(zone.center - zone.size * 0.5f, safeSize), Vector2.Scale(zone.size, safeSize));
            if (zoneRect.Overlaps(guard))
            {
                problem = $"{DisplayName(MoveZone)}がメニューボタンに重なっています";
                return false;
            }
            var stick = data.Get(MoveStick);
            Vector2 min = zone.center - zone.size * 0.5f, max = zone.center + zone.size * 0.5f;
            if (stick.center.x < min.x || stick.center.y < min.y || stick.center.x > max.x || stick.center.y > max.y)
            {
                problem = "移動スティックは操作開始領域の中に置いてください";
                return false;
            }
            return true;
        }

        /// <summary>Loads the saved layout; missing or broken entries fall back to the defaults.</summary>
        public static TouchLayoutData Load()
        {
            var defaults = Defaults();
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json))
                return defaults;
            TouchLayoutData saved;
            try
            {
                saved = JsonUtility.FromJson<TouchLayoutData>(json);
            }
            catch (ArgumentException)
            {
                return defaults;
            }
            if (saved == null || saved.version != defaults.version)
                return defaults;
            foreach (var item in defaults.items)
            {
                int index = saved.IndexOf(item.id);
                if (index < 0)
                    saved.items.Add(item);
                else if (saved.items[index].scale <= 0f)
                    saved.items[index] = item;
            }
            return saved;
        }

        public static void Save(TouchLayoutData data)
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void ClearSaved()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
