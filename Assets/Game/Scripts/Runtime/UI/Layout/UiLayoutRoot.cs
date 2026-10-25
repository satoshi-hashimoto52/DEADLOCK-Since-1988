using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.UI.Layout
{
    /// <summary>
    /// Full-screen container of freely placed UI elements. Put it on a RectTransform stretched over a screen-space
    /// canvas: it covers the whole screen (black margins and the area outside the safe area included) and is
    /// independent of any background framing. Elements are positioned from the layout data (normalised screen
    /// coordinates, <see cref="UiLayoutItem"/>), again whenever the screen size changes. The saved layout is read once
    /// and written only by <see cref="Save"/> (the editor's Save): defaults computed for this screen never overwrite it.
    /// Reusable for the game HUD later with its own <see cref="layoutId"/>.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiLayoutRoot : MonoBehaviour
    {
        public string layoutId = "home";
        public int layoutVersion = 1;

        readonly Dictionary<string, (RectTransform rect, UiLayoutSpec spec)> elements = new Dictionary<string, (RectTransform, UiLayoutSpec)>();
        readonly List<string> order = new List<string>();
        Func<Vector2, Rect, UiLayoutData> defaultsProvider;
        Vector2 appliedSize;

        public UiLayoutData Current { get; private set; }
        /// <summary>True when no saved layout of the current version exists (the defaults are shown).</summary>
        public bool ShowingDefaults { get; private set; }
        public event Action Applied;

        public RectTransform Rect => (RectTransform)transform;
        public Vector2 CanvasSize => Rect.rect.size;
        public IReadOnlyList<string> Ids => order;

        /// <summary>The safe area in this root's canvas units (origin bottom left).</summary>
        public Rect SafeRect => UiLayoutMath.SafeRectInCanvas(Screen.safeArea, new Vector2(Screen.width, Screen.height), CanvasSize);

        public IEnumerable<UiLayoutSpec> Specs
        {
            get
            {
                foreach (var id in order)
                    yield return elements[id].spec;
            }
        }

        public UiLayoutSpec Spec(string id) => elements[id].spec;

        public RectTransform Element(string id) => elements[id].rect;

        /// <summary>Registers an element (a child RectTransform) with its spec.</summary>
        public void Register(RectTransform rect, UiLayoutSpec spec)
        {
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            elements[spec.id] = (rect, spec);
            if (!order.Contains(spec.id))
                order.Add(spec.id);
        }

        /// <summary>Default layout for a canvas size and safe area (canvas units); may adapt to the screen.</summary>
        public void SetDefaults(Func<Vector2, Rect, UiLayoutData> provider) => defaultsProvider = provider;

        public UiLayoutData Defaults() => defaultsProvider(CanvasSize, SafeRect);

        /// <summary>Reads the saved layout (or the defaults) and shows it.</summary>
        public void Load()
        {
            Canvas.ForceUpdateCanvases(); // the canvas scaler has sized the root before the defaults are computed
            var resolved = UiLayoutMath.Resolve(UiLayoutStore.Load(layoutId), Defaults(), layoutVersion, out bool usedDefaults);
            ShowingDefaults = usedDefaults;
            Apply(resolved);
        }

        /// <summary>Shows a layout (kept on screen for display; the data passed in is kept as it is).</summary>
        public void Apply(UiLayoutData data)
        {
            Current = data.Clone();
            appliedSize = CanvasSize;
            Vector2 canvas = CanvasSize;
            foreach (var id in order)
            {
                var (rect, spec) = elements[id];
                if (!Current.TryGet(id, out var item))
                    continue;
                var shown = UiLayoutMath.KeepOnScreen(item, spec, canvas);
                rect.gameObject.SetActive(item.visible);
                rect.sizeDelta = UiLayoutMath.Size(shown, spec);
                rect.anchoredPosition = UiLayoutMath.ToCanvas(shown.center, canvas);
            }
            Applied?.Invoke();
        }

        public void Save(UiLayoutData data)
        {
            data = data.Clone();
            data.layoutVersion = layoutVersion;
            UiLayoutStore.Save(layoutId, data);
            ShowingDefaults = false;
            Apply(data);
        }

        void LateUpdate()
        {
            // Screen size / rotation change: same normalised layout on the new screen. Before Load, nothing to apply.
            if (Current != null && CanvasSize != appliedSize)
                Apply(ShowingDefaults ? Defaults() : Current); // unsaved defaults adapt to the new screen
        }
    }
}
