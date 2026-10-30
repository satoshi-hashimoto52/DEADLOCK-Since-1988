using System;
using System.Collections.Generic;
using Deadlock.Input;
using Deadlock.Map;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// "Settings → Control layout": edit the touch widgets on top of the real game screen. Drag a handle to move a
    /// widget, select it and use − / ＋ to resize. Changes preview live; Apply saves them on the device, Cancel
    /// restores the saved layout, Reset shows the defaults (saved only on Apply). Widgets may go sideways up to the
    /// screen's edges (past the Safe Area into the side margins), up and down only inside the Safe Area; moves that
    /// would make buttons overlap are refused. The ammo panel (a HUD box) moves and resizes the same way; buttons may
    /// cover it. A full-screen blocker stops game input while editing.
    /// Where the scene has a floor map (<see cref="MiniMapView"/>), it is one more item: drag to move, − / ＋ to resize
    /// (aspect kept), shown with sample markers while editing, always kept inside the Safe Area. The handle follows where
    /// the map is actually shown, which keeps clear of the HUD text and the controls (a note says when it is moved off the
    /// asked place, or when no clear place exists). The asked place is saved separately from the touch layout, only on
    /// Apply and only when it was changed here; Cancel keeps the saved one. The buttons' own places are not touched by it.
    /// </summary>
    public sealed class TouchLayoutEditor : MonoBehaviour
    {
        const float ScaleStep = 0.1f;
        const float ZoneStep = 0.1f;

        PlayerInputRouter input;
        RectTransform canvasRoot;
        Canvas canvas;
        TouchControls controls;
        TouchLayoutApplier applier;
        RectTransform layer;
        RectTransform handleRoot;
        Text selectedText;
        Text messageText;
        RectTransform toolbar;
        Text toolbarMoveLabel;
        readonly Dictionary<string, RectTransform> handles = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Image> handleImages = new Dictionary<string, Image>();
        TouchLayoutData working;
        MiniMapView miniMap;
        MiniMapPlacement mapWorking;
        bool mapChanged;
        bool mapReset;
        string selected;
        bool touchWasVisible;
        Action onClosed;

        public bool IsOpen { get; private set; }

        static int openEditors;

        /// <summary>
        /// A layout editor is open somewhere. Overlays drawn above the menu (the development recoil HUD's button) hide
        /// while it is, so they never sit on the editor's toolbar or take its touches.
        /// </summary>
        public static bool AnyOpen => openEditors > 0;

        /// <summary>Raised when <see cref="AnyOpen"/> changes (true: an editor opened; false: the last one closed).</summary>
        public static event Action<bool> AnyOpenChanged;

        public TouchLayoutData Working => working?.Clone();
        /// <summary>The floor map's place / size being edited (when the scene has one).</summary>
        public MiniMapPlacement MapWorking => mapWorking;
        public string Selected => selected;
        public string Message => messageText != null ? messageText.text : "";
        /// <summary>The toolbar sits at the bottom (true) or the top (false) of the screen.</summary>
        public bool ToolbarAtBottom { get; private set; }

        /// <summary>
        /// Moves the editor's toolbar between the top and the bottom of the screen, so controls placed in the top
        /// corners (or along the bottom) can always be reached while editing.
        /// </summary>
        public void ToggleToolbarPosition()
        {
            ToolbarAtBottom = !ToolbarAtBottom;
            Vector2 anchor = ToolbarAtBottom ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
            UiBuild.Place(toolbar, anchor, new Vector2(0f, ToolbarAtBottom ? 8f : -8f), toolbar.sizeDelta);
            toolbarMoveLabel.text = ToolbarAtBottom ? "ツールバーを上へ" : "ツールバーを下へ";
        }

        public void Init(PlayerInputRouter router, RectTransform root)
        {
            input = router;
            canvasRoot = root;
            canvas = root.GetComponentInParent<Canvas>().rootCanvas;
        }

        public void Open(Action closed)
        {
            controls = FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);
            touchWasVisible = controls.gameObject.activeSelf;
            controls.SetVisible(true); // edit the real widgets, also from desktop mode in the Editor
            // Edit at full strength even if the controls are set to be (partly) transparent.
            var opacity = controls.GetComponent<TouchControlsOpacity>();
            if (opacity != null)
                opacity.SetEditing(true);
            applier = controls.GetComponent<TouchLayoutApplier>();
            onClosed = closed;
            input.Block("layout");
            working = applier.Saved;
            miniMap = FindAnyObjectByType<MiniMapView>();
            mapChanged = mapReset = false;
            if (miniMap != null)
            {
                mapWorking = miniMap.Placement;
                miniMap.SetEditing(true);
            }
            if (layer == null)
                Build();
            if (handles.TryGetValue(MiniMapView.LayoutId, out var mapHandle))
                mapHandle.gameObject.SetActive(miniMap != null);
            foreach (string box in TouchLayout.Boxes)
                handles[box].gameObject.SetActive(applier.HasWidget(box));
            layer.gameObject.SetActive(true);
            layer.SetAsLastSibling();
            Select(null);
            ShowMessage("");
            SetOpenState(true);
        }

        void SetOpenState(bool open)
        {
            if (IsOpen == open)
                return;
            IsOpen = open;
            openEditors = Mathf.Max(0, openEditors + (open ? 1 : -1));
            AnyOpenChanged?.Invoke(AnyOpen);
        }

        void OnDestroy() => SetOpenState(false); // a scene unloaded while editing

        // ---------------------------------------------------------------- edit operations (also used by tests)

        /// <summary>Moves a widget by a screen-pixel delta. Returns false if the move was refused.</summary>
        public bool MoveBy(string id, Vector2 screenDelta)
        {
            Vector2 safe = applier.SafeSize;
            Vector2 room = applier.SideRoom;
            Vector2 delta = screenDelta / Mathf.Max(0.0001f, canvas.scaleFactor);
            Vector2 normalized = new Vector2(delta.x / Mathf.Max(1f, safe.x), delta.y / Mathf.Max(1f, safe.y));
            if (id == MiniMapView.LayoutId)
            {
                if (miniMap == null)
                    return false;
                var p = mapWorking;
                Vector2 mapSafe = miniMap.SafeSize;
                p.center += new Vector2(delta.x / Mathf.Max(1f, mapSafe.x), delta.y / Mathf.Max(1f, mapSafe.y));
                return AcceptMap(p);
            }
            var candidate = working.Clone();
            var item = candidate.Get(id);
            if (id == TouchLayout.MoveZone)
            {
                var before = TouchLayout.ClampZone(item, safe, room);
                item.center += normalized;
                item = TouchLayout.ClampZone(item, safe, room);
                // The stick rests inside the zone: it moves with it.
                var stick = candidate.Get(TouchLayout.MoveStick);
                stick.center += item.center - before.center;
                candidate.Set(stick);
            }
            else if (TouchLayout.IsBox(id))
            {
                item.center = TouchLayout.ClampBox(item.center + normalized, TouchLayout.BoxSize(item), safe, room);
            }
            else
            {
                item.center = TouchLayout.ClampCircle(item.center + normalized, TouchLayout.Diameter(item), safe, room);
            }
            candidate.Set(item);
            return TryAccept(candidate);
        }

        /// <summary>Grows (+1) or shrinks (−1) the selected widget.</summary>
        public bool Resize(string id, int direction)
        {
            if (id == MiniMapView.LayoutId)
            {
                if (miniMap == null)
                    return false;
                var p = mapWorking;
                p.scale = Mathf.Clamp(p.scale + ScaleStep * direction, MiniMapView.MinScale, MiniMapView.MaxScale);
                return AcceptMap(p);
            }
            var candidate = working.Clone();
            var item = candidate.Get(id);
            if (id == TouchLayout.MoveZone)
            {
                item.size *= 1f + ZoneStep * direction;
                item = TouchLayout.ClampZone(item, applier.SafeSize, applier.SideRoom);
            }
            else
            {
                item.scale = Mathf.Clamp(item.scale + ScaleStep * direction, TouchLayout.MinScale, TouchLayout.MaxScale);
                item.center = TouchLayout.IsBox(id)
                    ? TouchLayout.ClampBox(item.center, TouchLayout.BoxSize(item), applier.SafeSize, applier.SideRoom)
                    : TouchLayout.ClampCircle(item.center, TouchLayout.Diameter(item), applier.SafeSize, applier.SideRoom);
            }
            candidate.Set(item);
            return TryAccept(candidate);
        }

        public void ResetToDefaults()
        {
            working = TouchLayout.Defaults();
            applier.Apply(working);
            if (miniMap != null)
            {
                // The map goes back to its automatic first place for the default layout (and stays automatic once applied).
                mapWorking = miniMap.AutoPlacementFor(working);
                miniMap.Preview(mapWorking);
                mapReset = true;
                mapChanged = false;
            }
            ShowMessage("初期配置を表示中です（「適用」で保存）");
        }

        public void ApplyAndClose()
        {
            applier.Commit(working);
            if (miniMap != null)
            {
                if (mapChanged)
                    miniMap.Commit(mapWorking);
                else if (mapReset)
                {
                    MapSettings.ClearPlacement();
                    miniMap.Revert();
                }
            }
            Close();
        }

        public void CancelAndClose()
        {
            applier.Revert();
            if (miniMap != null)
                miniMap.Revert();
            Close();
        }

        /// <summary>A new place / size for the floor map: kept inside the Safe Area; an overlap with a control is allowed but noted.</summary>
        bool AcceptMap(MiniMapPlacement candidate)
        {
            mapWorking = MiniMapView.Clamp(candidate, miniMap.SafeSize);
            miniMap.Preview(mapWorking);
            mapChanged = true;
            mapReset = false;
            Rect asked = MiniMapView.PlacementRect(mapWorking, miniMap.SafeSize);
            ShowMessage(miniMap.AvoidanceFailed ? "ミニマップを置ける空きがありません（この位置のまま表示されます）"
                : Vector2.Distance(asked.center, miniMap.ShownRect.center) > 0.5f ? "HUDや操作ボタンを避けた位置に表示されます" : "");
            return true;
        }

        bool TryAccept(TouchLayoutData candidate)
        {
            if (!TouchLayout.IsUsable(candidate, applier.SafeSize, out string problem, applier.SideRoom))
            {
                ShowMessage(problem + "（変更できません）");
                return false;
            }
            working = candidate;
            applier.Apply(working);
            ShowMessage("");
            return true;
        }

        void Close()
        {
            SetOpenState(false);
            layer.gameObject.SetActive(false);
            var opacity = controls.GetComponent<TouchControlsOpacity>();
            if (opacity != null)
                opacity.SetEditing(false);
            controls.SetVisible(touchWasVisible);
            if (miniMap != null)
                miniMap.SetEditing(false);
            input.Unblock("layout");
            onClosed?.Invoke();
        }

        void Select(string id)
        {
            selected = id;
            selectedText.text = id == null ? "動かすボタンをドラッグ／選んで − ＋ で大きさ" : $"選択中：{Name(id)}";
            foreach (var pair in handleImages)
                pair.Value.color = HandleColor(pair.Key, pair.Key == id);
        }

        void ShowMessage(string text) => messageText.text = text;

        static string Name(string id) => id == MiniMapView.LayoutId ? MiniMapView.DisplayName : TouchLayout.DisplayName(id);

        static Color HandleColor(string id, bool selected)
        {
            if (selected)
                return new Color(1f, 0.8f, 0.2f, id == MiniMapView.LayoutId ? 0.3f : 0.55f);
            if (id == MiniMapView.LayoutId)
                return new Color(0.45f, 0.78f, 0.95f, 0.18f);
            if (TouchLayout.IsBox(id))
                return new Color(0.6f, 1f, 0.6f, 0.22f);
            return id == TouchLayout.MoveZone ? new Color(0.3f, 0.6f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.3f);
        }

        // ---------------------------------------------------------------- view

        void LateUpdate()
        {
            if (!IsOpen)
                return;
            // Handles follow the real widgets (which the applier positions), so what you drag is what you get.
            foreach (var pair in handles)
            {
                if (pair.Key == MiniMapView.LayoutId && miniMap == null || pair.Key != MiniMapView.LayoutId && !applier.HasWidget(pair.Key))
                    continue;
                var widget = pair.Key == MiniMapView.LayoutId ? miniMap.Frame : applier.Widget(pair.Key);
                var corners = new Vector3[4];
                widget.GetWorldCorners(corners);
                Vector2 a = handleRoot.InverseTransformPoint(corners[0]);
                Vector2 b = handleRoot.InverseTransformPoint(corners[2]);
                pair.Value.anchoredPosition = (a + b) * 0.5f;
                pair.Value.sizeDelta = b - a;
            }
        }

        void Build()
        {
            layer = UiBuild.Stretch(UiBuild.Rect("LayoutEditorLayer", canvasRoot));
            // Blocks every touch / click from reaching the game widgets underneath.
            UiBuild.Image(layer, new Color(0f, 0f, 0f, 0.35f), true);
            handleRoot = UiBuild.Stretch(UiBuild.Rect("Handles", layer));

            var ids = new List<string> { TouchLayout.MoveZone, MiniMapView.LayoutId };
            ids.AddRange(TouchLayout.Boxes);
            ids.AddRange(TouchLayout.Circles);
            foreach (string id in ids)
            {
                var handle = UiBuild.Place(UiBuild.Rect("Handle_" + id, handleRoot), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
                handleImages[id] = UiBuild.Image(handle, HandleColor(id, false), true);
                // Name above the zone, under the circles (not on top of the widget's own label).
                if (id == TouchLayout.MoveZone)
                    UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Name", handle)), Name(id), 22, TextAnchor.UpperCenter);
                else
                    UiBuild.Text(UiBuild.Place(UiBuild.Rect("Name", handle), new Vector2(0.5f, 0f), new Vector2(0f, -30f), new Vector2(260f, 30f)),
                        Name(id), 22, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.7f));
                var drag = handle.gameObject.AddComponent<LayoutHandle>();
                string captured = id;
                drag.Pressed = () => Select(captured);
                drag.Dragged = delta => MoveBy(captured, delta);
                handles[id] = handle;
            }
            // Smaller widgets above the zone, the map and the ammo panel so they stay grabbable.
            foreach (string box in TouchLayout.Boxes)
                handles[box].SetAsFirstSibling();
            handles[MiniMapView.LayoutId].SetAsFirstSibling();
            handles[TouchLayout.MoveZone].SetAsFirstSibling();

            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var bar = UiBuild.Place(UiBuild.Rect("Toolbar", safe), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(1400f, 150f));
            toolbar = bar;
            UiBuild.Image(bar, new Color(0.08f, 0.09f, 0.12f, 0.92f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", bar), new Vector2(0f, 1f), new Vector2(24f, -10f), new Vector2(700f, 40f)),
                "設定 → 操作レイアウト", 28, TextAnchor.UpperLeft);
            selectedText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Selected", bar), new Vector2(0f, 1f), new Vector2(24f, -54f), new Vector2(560f, 36f)),
                "", 24, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.4f));
            messageText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Message", bar), new Vector2(0f, 1f), new Vector2(24f, -98f), new Vector2(760f, 36f)),
                "", 22, TextAnchor.UpperLeft, new Color(1f, 0.55f, 0.5f));

            // Right side, two rows: Reset / Cancel / Apply, and − ＋ under them (fits 4:3 screens).
            Vector2 wide = new Vector2(180f, 60f), small = new Vector2(90f, 60f);
            UiBuild.Button(bar, "Apply", "適用", new Vector2(1f, 1f), new Vector2(-16f, -10f), wide, ApplyAndClose, 26);
            UiBuild.Button(bar, "Cancel", "キャンセル", new Vector2(1f, 1f), new Vector2(-208f, -10f), wide, CancelAndClose, 26);
            UiBuild.Button(bar, "Reset", "初期配置に戻す", new Vector2(1f, 1f), new Vector2(-400f, -10f), new Vector2(220f, 60f), ResetToDefaults, 24);
            UiBuild.Button(bar, "Grow", "＋", new Vector2(1f, 1f), new Vector2(-16f, -80f), small, () => { if (selected != null) Resize(selected, +1); }, 36);
            UiBuild.Button(bar, "Shrink", "−", new Vector2(1f, 1f), new Vector2(-118f, -80f), small, () => { if (selected != null) Resize(selected, -1); }, 36);
            var move = UiBuild.Button(bar, "MoveToolbar", "ツールバーを下へ", new Vector2(1f, 1f), new Vector2(-226f, -80f), new Vector2(220f, 60f), ToggleToolbarPosition, 22);
            toolbarMoveLabel = move.GetComponentInChildren<Text>();
        }
    }
}
