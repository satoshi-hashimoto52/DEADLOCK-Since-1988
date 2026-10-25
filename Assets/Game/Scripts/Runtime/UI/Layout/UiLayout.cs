using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.UI.Layout
{
    /// <summary>
    /// One freely placed UI element of a layout. <see cref="center"/> is normalised to the whole screen (0..1, origin
    /// bottom left), not to the safe area or to the home background, so the element can sit in the black margins and
    /// keeps its relative spot on other screen sizes. <see cref="scale"/> multiplies the element's base size (the
    /// aspect ratio never changes).
    /// </summary>
    [Serializable]
    public struct UiLayoutItem
    {
        public string id;
        public Vector2 center;
        public float scale;
        public bool visible;
    }

    /// <summary>Saved layout of one screen (home, and later the game HUD: each has its own data and key).</summary>
    [Serializable]
    public sealed class UiLayoutData
    {
        public int layoutVersion;
        public List<UiLayoutItem> items = new List<UiLayoutItem>();

        public UiLayoutData Clone() => new UiLayoutData { layoutVersion = layoutVersion, items = new List<UiLayoutItem>(items) };

        public int IndexOf(string id) => items.FindIndex(i => i.id == id);

        public bool TryGet(string id, out UiLayoutItem item)
        {
            int index = IndexOf(id);
            item = index >= 0 ? items[index] : default;
            return index >= 0;
        }

        public UiLayoutItem Get(string id) => items[IndexOf(id)];

        public void Set(UiLayoutItem item)
        {
            int index = IndexOf(item.id);
            if (index >= 0)
                items[index] = item;
            else
                items.Add(item);
        }
    }

    /// <summary>Fixed description of an element: its base size (canvas units) and allowed scale range.</summary>
    [Serializable]
    public struct UiLayoutSpec
    {
        public string id;
        public Vector2 baseSize;
        public float minScale;
        public float maxScale;

        public UiLayoutSpec(string id, Vector2 baseSize, float minScale = 0.6f, float maxScale = 1.6f)
        {
            this.id = id;
            this.baseSize = baseSize;
            this.minScale = minScale;
            this.maxScale = maxScale;
        }
    }

    /// <summary>Coordinate conversion and the rules that keep a layout usable (pure functions, tested in EditMode).</summary>
    public static class UiLayoutMath
    {
        /// <summary>Canvas position (units, origin bottom left) of a normalised screen point.</summary>
        public static Vector2 ToCanvas(Vector2 normalized, Vector2 canvasSize) => Vector2.Scale(normalized, canvasSize);

        public static Vector2 ToNormalized(Vector2 canvasPosition, Vector2 canvasSize) =>
            new Vector2(canvasPosition.x / Mathf.Max(1f, canvasSize.x), canvasPosition.y / Mathf.Max(1f, canvasSize.y));

        /// <summary>The safe area (screen px) in canvas units.</summary>
        public static Rect SafeRectInCanvas(Rect safeArea, Vector2 screenSize, Vector2 canvasSize)
        {
            float sx = canvasSize.x / Mathf.Max(1f, screenSize.x), sy = canvasSize.y / Mathf.Max(1f, screenSize.y);
            return new Rect(safeArea.x * sx, safeArea.y * sy, safeArea.width * sx, safeArea.height * sy);
        }

        public static float ClampScale(float scale, UiLayoutSpec spec) => Mathf.Clamp(scale, spec.minScale, spec.maxScale);

        public static Vector2 Size(UiLayoutItem item, UiLayoutSpec spec) => spec.baseSize * ClampScale(item.scale, spec);

        /// <summary>Element rectangle in canvas units.</summary>
        public static Rect ItemRect(UiLayoutItem item, UiLayoutSpec spec, Vector2 canvasSize)
        {
            Vector2 size = Size(item, spec), c = ToCanvas(item.center, canvasSize);
            return new Rect(c - size * 0.5f, size);
        }

        public static bool Contains(Rect outer, Rect inner, float tolerance = 0.5f) =>
            inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance &&
            inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;

        /// <summary>True when the whole element is inside the safe area (canvas units).</summary>
        public static bool InsideSafeArea(UiLayoutItem item, UiLayoutSpec spec, Vector2 canvasSize, Rect safeRect) =>
            Contains(safeRect, ItemRect(item, spec, canvasSize));

        /// <summary>
        /// Keeps an element usable on any screen: scale within its range and no larger than the screen, the whole
        /// element on screen (the black margins and the area outside the safe area are allowed, off screen is not).
        /// </summary>
        public static UiLayoutItem KeepOnScreen(UiLayoutItem item, UiLayoutSpec spec, Vector2 canvasSize)
        {
            float fit = Mathf.Min(canvasSize.x / Mathf.Max(1f, spec.baseSize.x), canvasSize.y / Mathf.Max(1f, spec.baseSize.y));
            item.scale = Mathf.Min(ClampScale(item.scale, spec), Mathf.Max(spec.minScale, fit));
            Vector2 half = spec.baseSize * item.scale * 0.5f;
            Vector2 c = ToCanvas(item.center, canvasSize);
            c.x = Mathf.Clamp(c.x, half.x, Mathf.Max(half.x, canvasSize.x - half.x));
            c.y = Mathf.Clamp(c.y, half.y, Mathf.Max(half.y, canvasSize.y - half.y));
            item.center = ToNormalized(c, canvasSize);
            return item;
        }

        /// <summary>
        /// The layout to show: the saved one when it exists and has the current version (unknown ids dropped, missing
        /// ids filled from the defaults), otherwise the defaults. The saved data itself is not changed here.
        /// </summary>
        public static UiLayoutData Resolve(UiLayoutData saved, UiLayoutData defaults, int version, out bool usedDefaults)
        {
            usedDefaults = saved == null || saved.layoutVersion != version;
            var result = new UiLayoutData { layoutVersion = version };
            foreach (var def in defaults.items)
                result.items.Add(!usedDefaults && saved.TryGet(def.id, out var s) ? s : def);
            return result;
        }
    }

    /// <summary>
    /// Device-local storage of layouts (PlayerPrefs, JSON), one key per layout id ("home", later "hud"). Reading never
    /// throws: missing or broken data reads as null.
    /// </summary>
    public static class UiLayoutStore
    {
        public static string Key(string layoutId) => "deadlock.uiLayout." + layoutId;

        public static UiLayoutData Load(string layoutId)
        {
            try
            {
                string json = PlayerPrefs.GetString(Key(layoutId), "");
                if (string.IsNullOrEmpty(json))
                    return null;
                var data = JsonUtility.FromJson<UiLayoutData>(json);
                return data != null && data.items != null ? data : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(string layoutId, UiLayoutData data)
        {
            PlayerPrefs.SetString(Key(layoutId), JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static bool HasSaved(string layoutId) => PlayerPrefs.HasKey(Key(layoutId));
    }

    /// <summary>
    /// One editing session: changes go to a draft; <see cref="Cancel"/> returns the layout from before the session,
    /// <see cref="ResetToDefaults"/> puts the defaults into the draft (saved only by <see cref="Commit"/>).
    /// </summary>
    public sealed class UiLayoutEditSession
    {
        readonly UiLayoutData original;
        readonly Func<UiLayoutData> defaults;
        readonly Dictionary<string, UiLayoutSpec> specs;

        public UiLayoutData Draft { get; private set; }

        public UiLayoutEditSession(UiLayoutData current, Func<UiLayoutData> defaults, IEnumerable<UiLayoutSpec> specs)
        {
            original = current.Clone();
            Draft = current.Clone();
            this.defaults = defaults;
            this.specs = new Dictionary<string, UiLayoutSpec>();
            foreach (var s in specs)
                this.specs[s.id] = s;
        }

        public UiLayoutSpec Spec(string id) => specs[id];

        /// <summary>Moves an element by a canvas delta (kept on screen).</summary>
        public UiLayoutItem Move(string id, Vector2 canvasDelta, Vector2 canvasSize)
        {
            var item = Draft.Get(id);
            item.center += UiLayoutMath.ToNormalized(canvasDelta, canvasSize);
            item = UiLayoutMath.KeepOnScreen(item, specs[id], canvasSize);
            Draft.Set(item);
            return item;
        }

        /// <summary>Sets an element's scale (clamped to its range, kept on screen).</summary>
        public UiLayoutItem SetScale(string id, float scale, Vector2 canvasSize)
        {
            var item = Draft.Get(id);
            item.scale = UiLayoutMath.ClampScale(scale, specs[id]);
            item = UiLayoutMath.KeepOnScreen(item, specs[id], canvasSize);
            Draft.Set(item);
            return item;
        }

        public void ResetToDefaults() => Draft = defaults().Clone();

        public UiLayoutData Cancel()
        {
            Draft = original.Clone();
            return Draft;
        }

        public UiLayoutData Commit() => Draft.Clone();
    }
}
