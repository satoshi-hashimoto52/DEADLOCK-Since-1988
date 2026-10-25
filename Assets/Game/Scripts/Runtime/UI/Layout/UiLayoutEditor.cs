using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Deadlock.UI.Layout
{
    /// <summary>Pointer relay on an editor frame / resize handle (drag in canvas units).</summary>
    public sealed class UiLayoutPointer : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public Action Down;
        public Action<Vector2> Drag; // delta in canvas units

        public void OnPointerDown(PointerEventData e) => Down?.Invoke();

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            Drag?.Invoke(e.delta / Mathf.Max(0.0001f, canvas.scaleFactor));
        }
    }

    /// <summary>
    /// Layout edit mode over a <see cref="UiLayoutRoot"/>: every movable element gets a frame (tap to select, drag to
    /// move, drag the corner handle or use − / ＋ to resize, aspect kept, within the element's min / max), the area
    /// outside the safe area is shaded, and a selected element that is (partly) outside the safe area shows a warning
    /// (it may be hidden or not respond: Dynamic Island, notch, rounded corners, home indicator; the device shape is
    /// not assumed beyond Screen.safeArea). Moving there is allowed; off screen is not. The toolbar (Save / Cancel /
    /// Reset) sits inside the safe area above everything, so editing can always be ended. Cancel restores the layout
    /// from before editing; Reset puts the defaults into the draft (saved only with Save).
    /// </summary>
    public sealed class UiLayoutEditor : MonoBehaviour
    {
        static readonly Color FrameColor = new Color(0.55f, 0.8f, 1f, 0.9f);
        static readonly Color SelectedColor = new Color(1f, 0.82f, 0.3f, 1f);
        static readonly Color DangerColor = new Color(0.9f, 0.15f, 0.1f, 0.28f);

        UiLayoutRoot root;
        RectTransform overlay;
        RectTransform frames;
        RectTransform dangerLayer;
        RectTransform toolbar;
        Text selectedText;
        Text warningText;
        readonly Dictionary<string, RectTransform> frameOf = new Dictionary<string, RectTransform>();
        UiLayoutEditSession session;
        Action onClosed;
        Rect builtSafe;
        Vector2 builtCanvas;

        public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
        public string SelectedId { get; private set; }
        public UiLayoutData Draft => session?.Draft;
        public string Warning => warningText != null && warningText.gameObject.activeSelf ? warningText.text : "";
        public Button SaveButton { get; private set; }
        public Button CancelButton { get; private set; }
        public Button ResetButton { get; private set; }
        /// <summary>Name shown for the selected element (defaults to the id).</summary>
        public Func<string, string> DisplayName = id => id;

        public const string OutsideSafeAreaWarning =
            "Safe Area の外です。端末の形状（Dynamic Island・ノッチ・角丸・ホームインジケーター）で見えない、または押せない場合があります";

        /// <param name="root">Layout to edit.</param>
        /// <param name="parent">Full-screen RectTransform above the root (same canvas).</param>
        public void Init(UiLayoutRoot root, RectTransform parent)
        {
            this.root = root;
            overlay = UiBuild.Stretch(UiBuild.Rect("LayoutEditor", parent));
            UiBuild.Image(overlay, new Color(0f, 0f, 0f, 0.15f), true); // blocks the menu buttons while editing
            dangerLayer = UiBuild.Stretch(UiBuild.Rect("OutsideSafeArea", overlay));
            frames = UiBuild.Stretch(UiBuild.Rect("Frames", overlay));
            BuildToolbar();
            overlay.gameObject.SetActive(false);
            root.Applied += Refresh;
        }

        void BuildToolbar()
        {
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", overlay));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            toolbar = UiBuild.Place(UiBuild.Rect("Toolbar", safe), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(720f, 170f));
            UiBuild.Image(toolbar, new Color(0.05f, 0.05f, 0.07f, 0.92f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", toolbar), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(700f, 30f)),
                "レイアウト編集：枠をドラッグで移動、角の□で大きさ変更", 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            SaveButton = UiBuild.Button(toolbar, "SaveButton", "保存", new Vector2(0.5f, 1f), new Vector2(-235f, -42f), new Vector2(210f, 56f), Save, 24);
            CancelButton = UiBuild.Button(toolbar, "CancelButton", "キャンセル", new Vector2(0.5f, 1f), new Vector2(0f, -42f), new Vector2(210f, 56f), Cancel, 24);
            ResetButton = UiBuild.Button(toolbar, "ResetButton", "初期配置に戻す", new Vector2(0.5f, 1f), new Vector2(235f, -42f), new Vector2(210f, 56f), ResetToDefaults, 22);
            UiBuild.Button(toolbar, "SmallerButton", "－", new Vector2(0.5f, 1f), new Vector2(150f, -106f), new Vector2(70f, 52f), () => StepScale(-0.1f), 30);
            UiBuild.Button(toolbar, "LargerButton", "＋", new Vector2(0.5f, 1f), new Vector2(235f, -106f), new Vector2(70f, 52f), () => StepScale(0.1f), 30);
            selectedText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Selected", toolbar), new Vector2(0.5f, 1f), new Vector2(-110f, -106f), new Vector2(460f, 52f)),
                "", 20, TextAnchor.MiddleLeft);
            warningText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Warning", safe), new Vector2(0.5f, 1f), new Vector2(0f, -186f), new Vector2(1100f, 56f)),
                OutsideSafeAreaWarning, 18, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f));
            warningText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiBuild.Image(UiBuild.Stretch(UiBuild.Rect("WarningBack", warningText.rectTransform)), new Color(0f, 0f, 0f, 0.75f), false).transform.SetAsFirstSibling();
            warningText.gameObject.SetActive(false);
        }

        public void Open(Action closed = null)
        {
            onClosed = closed;
            session = new UiLayoutEditSession(root.Current, root.Defaults, root.Specs);
            overlay.SetAsLastSibling();
            overlay.gameObject.SetActive(true);
            BuildFrames();
            Select(root.Ids.Count > 0 ? root.Ids[0] : null);
        }

        public void Save()
        {
            if (!IsOpen)
                return;
            root.Save(session.Commit());
            Close();
        }

        public void Cancel()
        {
            if (!IsOpen)
                return;
            root.Apply(session.Cancel());
            Close();
        }

        public void ResetToDefaults()
        {
            session.ResetToDefaults();
            root.Apply(session.Draft);
        }

        public void Select(string id)
        {
            SelectedId = id;
            Refresh();
        }

        /// <summary>Moves an element by a canvas delta (what a drag does).</summary>
        public void DragBy(string id, Vector2 canvasDelta)
        {
            session.Move(id, canvasDelta, root.CanvasSize);
            root.Apply(session.Draft);
        }

        public void SetScale(string id, float scale)
        {
            session.SetScale(id, scale, root.CanvasSize);
            root.Apply(session.Draft);
        }

        void StepScale(float step)
        {
            if (SelectedId != null)
                SetScale(SelectedId, session.Draft.Get(SelectedId).scale + step);
        }

        /// <summary>True when the element is (partly) outside the safe area in the draft.</summary>
        public bool IsOutsideSafeArea(string id) =>
            !UiLayoutMath.InsideSafeArea(session.Draft.Get(id), root.Spec(id), root.CanvasSize, root.SafeRect);

        void Close()
        {
            overlay.gameObject.SetActive(false);
            session = null;
            SelectedId = null;
            var closed = onClosed;
            onClosed = null;
            closed?.Invoke();
        }

        void BuildFrames()
        {
            foreach (var f in frameOf.Values)
                Destroy(f.gameObject);
            frameOf.Clear();
            foreach (var id in root.Ids)
            {
                string captured = id;
                var frame = UiBuild.Rect("Frame_" + id, frames);
                frame.anchorMin = frame.anchorMax = Vector2.zero;
                frame.pivot = new Vector2(0.5f, 0.5f);
                UiBuild.Image(frame, new Color(1f, 1f, 1f, 0.06f), true);
                var outline = frame.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(3f, -3f);
                var pointer = frame.gameObject.AddComponent<UiLayoutPointer>();
                pointer.Down = () => Select(captured);
                pointer.Drag = d => DragBy(captured, d);

                var handle = UiBuild.Place(UiBuild.Rect("ResizeHandle", frame), new Vector2(1f, 1f), Vector2.zero, new Vector2(44f, 44f));
                UiBuild.Image(handle, SelectedColor, true);
                var resize = handle.gameObject.AddComponent<UiLayoutPointer>();
                resize.Down = () => Select(captured);
                resize.Drag = d => ResizeBy(captured, d);
                frameOf[id] = frame;
            }
            builtCanvas = Vector2.zero; // rebuild the shaded area on the next refresh
            Refresh();
        }

        void ResizeBy(string id, Vector2 canvasDelta)
        {
            // Dragging the top-right corner outwards grows the element (uniform scale, aspect kept).
            var spec = root.Spec(id);
            var item = session.Draft.Get(id);
            Vector2 size = spec.baseSize * item.scale;
            float grow = (canvasDelta.x / Mathf.Max(1f, size.x) + canvasDelta.y / Mathf.Max(1f, size.y));
            SetScale(id, item.scale * (1f + grow));
        }

        void Refresh()
        {
            if (!IsOpen || session == null)
                return;
            Vector2 canvas = root.CanvasSize;
            Rect safe = root.SafeRect;
            if (canvas != builtCanvas || safe != builtSafe)
                BuildDangerZones(canvas, safe);
            foreach (var id in root.Ids)
            {
                var spec = root.Spec(id);
                var shown = UiLayoutMath.KeepOnScreen(session.Draft.Get(id), spec, canvas);
                var frame = frameOf[id];
                frame.anchoredPosition = UiLayoutMath.ToCanvas(shown.center, canvas);
                frame.sizeDelta = UiLayoutMath.Size(shown, spec);
                bool selected = id == SelectedId;
                frame.GetComponent<Outline>().effectColor = selected ? SelectedColor : FrameColor;
                frame.Find("ResizeHandle").gameObject.SetActive(selected);
                if (selected)
                    frame.SetAsLastSibling();
            }
            if (SelectedId != null)
            {
                var item = session.Draft.Get(SelectedId);
                selectedText.text = $"選択中：{DisplayName(SelectedId)}　大きさ {Mathf.RoundToInt(item.scale * 100f)}%";
                warningText.gameObject.SetActive(IsOutsideSafeArea(SelectedId));
            }
            else
            {
                selectedText.text = "枠をタップして選択";
                warningText.gameObject.SetActive(false);
            }
        }

        void BuildDangerZones(Vector2 canvas, Rect safe)
        {
            builtCanvas = canvas;
            builtSafe = safe;
            for (int i = dangerLayer.childCount - 1; i >= 0; i--)
                Destroy(dangerLayer.GetChild(i).gameObject);
            void Zone(string name, Rect r)
            {
                if (r.width <= 0.5f || r.height <= 0.5f)
                    return;
                var z = UiBuild.Rect(name, dangerLayer);
                z.anchorMin = z.anchorMax = z.pivot = Vector2.zero;
                z.anchoredPosition = r.position;
                z.sizeDelta = r.size;
                UiBuild.Image(z, DangerColor, false);
            }
            Zone("Left", new Rect(0f, 0f, safe.xMin, canvas.y));
            Zone("Right", new Rect(safe.xMax, 0f, canvas.x - safe.xMax, canvas.y));
            Zone("Bottom", new Rect(safe.xMin, 0f, safe.width, safe.yMin));
            Zone("Top", new Rect(safe.xMin, safe.yMax, safe.width, canvas.y - safe.yMax));
        }
    }
}
