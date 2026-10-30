using System;
using System.Collections.Generic;
using System.Globalization;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (Deadlock.Dev). Recoil HUD over the firing range, for tuning while playing. Its rows are the full
    /// lab's own (<see cref="WeaponRecoilEditor.EditorLayout.Inline"/>: same parts, columns, sizes and units), so all the
    /// recoil items fit on one screen to compare; each control takes the touches of its own drawn area, and neighbours
    /// keep a gap. A "反動調整" button shows it (the button hides while the HUD is open; the HUD has its own 閉じる); the
    /// "≡" cell drags it. While the touch layout editor is open (<see cref="TouchLayoutEditor.AnyOpen"/>) the button is
    /// hidden (not drawn, no touches: it would sit on the editor's toolbar) and an open HUD closes; after Save or Cancel
    /// the button is back and the HUD stays closed.
    /// <code>
    /// [≡][武器：PROTO RIFLE（ライフル） ▼][展開][閉じる]   header
    /// 試射なし（A＝正式値）｜B未適用 2項目               state (compact: A and B values too)
    /// ┌ scrolling body ───────────────────────────────┐  expanded: the lab's recoil rows,
    /// │ B：編集        [スライダー：粗調整]   B    A    │  then the helpers (end of test,
    /// │ 上下 [上][下][なし] ───●─── [0.70°] 上 0.70°   │  aim reference, B to official,
    /// │ …                                             │  the full lab) and the measured kick
    /// └───────────────────────────────────────────────┘
    /// [Bを試射に適用][BをAに戻す][照準リセット]          footer (expanded)
    /// </code>
    /// Its width is <see cref="WideWidth"/> or <see cref="NarrowWidth"/>, whichever lets the expanded panel be taller clear
    /// of the crosshair and the HUD's controls; its height is the most the safe area gives there. B is the lab's edited
    /// profile; A, what the test runs with, only changes when B is applied. The background is half transparent (its own
    /// colour's alpha 0.5; texts, buttons and sliders stay opaque, texts get an outline). Only the panel's own area takes
    /// touches; with a mouse, the player's input is held while the pointer is over the panel, so a click there never
    /// shoots or turns.
    /// </summary>
    public sealed class WeaponBalanceQuickPanel : MonoBehaviour
    {
        public const string HoverBlockReason = "weaponQuickHover";
        /// <summary>Row height of the recoil rows: the full lab's.</summary>
        public const float Row = 50f;
        const float Pad = 4f, Gap = 4f;
        /// <summary>Width of the panel where the screen leaves room (one-line labels, roomy A column and a long slider).</summary>
        public const float WideWidth = 920f;
        /// <summary>Width when it makes the expanded panel clearly taller (the sliders get shorter).</summary>
        public const float NarrowWidth = 860f;
        /// <summary>
        /// Narrowest width: every text still on one line (the weapon's full name, the state, the slider switch, the labels,
        /// A / B values such as "ランダム ±0.25°"). Never narrower, even where the HUD is crowded (no wrapping comes first).
        /// </summary>
        public const float NarrowestWidth = 800f;
        /// <summary>The widths tried, widest first.</summary>
        public static readonly float[] Widths = { WideWidth, NarrowWidth, NarrowestWidth };
        const float HeaderH = 52f, HandleW = 64f, HeaderButtonW = 76f;
        const float HeaderBlock = Pad + HeaderH + Gap;
        const float CompactStatus = 92f; // four lines
        public const float CompactHeight = HeaderBlock + CompactStatus + Pad;
        const float ExpandedStatus = 46f; // two lines
        const float TopBlock = HeaderBlock + ExpandedStatus;
        const float FooterBlock = Pad + Row + Gap;
        /// <summary>Smallest expanded height: header, state, the rows' header and one row, footer.</summary>
        public const float MinExpandedHeight = TopBlock + 30f + Gap + Row + Gap + FooterBlock;
        const float EditorHeaderBlock = 30f + Gap; // the rows' header (B / coarse-fine / A)
        /// <summary>Expanded height showing every recoil row (the helpers below them may need a scroll).</summary>
        public const float FullHeight = TopBlock + EditorHeaderBlock + 6f * (Row + Gap) + FooterBlock;

        /// <summary>Recoil rows fully in view at the top of the body of an expanded panel this tall.</summary>
        public static int RecoilRowsInView(float panelHeight) =>
            Mathf.Clamp(Mathf.FloorToInt((panelHeight - TopBlock - FooterBlock - EditorHeaderBlock + Gap + 0.01f) / (Row + Gap)), 0, 6);

        /// <summary>A narrow panel is used when it is at least this much taller (expanded) than a wide one.</summary>
        public const float NarrowGain = 32f;
        /// <summary>Background alpha of the panel (its face only; contents stay opaque).</summary>
        public const float BackgroundAlpha = 0.5f;
        /// <summary>Preferred top-left spot (safe-area units from its top left): right of the HUD's top-left button.</summary>
        public static readonly Vector2 PreferredSpot = new Vector2(150f, 8f);
        /// <summary>Half size of the area around the screen centre (crosshair) the panel keeps clear of.</summary>
        public const float CrosshairClearance = 45f;
        /// <summary>Space kept around each HUD part (units).</summary>
        public const float ObstacleMargin = 6f;
        /// <summary>Font of the rows, buttons and numbers (a step larger than the full lab's 19, for reading in play).</summary>
        public const int Font = 21;
        const int SmallFont = 18;
        /// <summary>
        /// The rows' look here: every label on one line in a column sized for the longest ("回復速度", "ADS倍率"), thin
        /// slider tracks (each slider still takes touches over its whole row), A and number columns wide enough for
        /// "ランダム ±0.25°" and "9.0°/秒" on one line.
        /// </summary>
        public static WeaponRecoilEditor.InlineStyle RowStyle => new WeaponRecoilEditor.InlineStyle
        {
            LabelWidth = 96f, OneLineLabels = true, ThinSliders = true, AWidth = 156f, FieldWidth = 108f, WideFineToggle = true,
        };
        const float DetailHeight = 44f;
        /// <summary>The "反動調整" button's touch cell (44 pt on an iPhone 16 Pro in landscape), drawn smaller.</summary>
        const float T = WeaponRecoilEditor.TouchSize;
        const float IY = WeaponRecoilEditor.TouchInsetY;

        static readonly Color Bg = new Color(0.04f, 0.045f, 0.06f, BackgroundAlpha);
        static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        static readonly Color HelperColor = new Color(0.12f, 0.16f, 0.2f, 1f);
        static readonly Color ActionColor = new Color(0.42f, 0.2f, 0.06f, 1f);

        WeaponBalanceLab lab;
        GunController gun;
        FirstPersonMotor motor;
        PlayerInputRouter input;
        RectTransform safe, body, footer, viewport, content;
        ScrollRect scroll;
        bool hoverBlocked;
        float contentHeight;
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();

        public RectTransform Panel { get; private set; }
        public Image Background { get; private set; }
        public Button ToggleButton { get; private set; }
        /// <summary>The state: test, A / B difference count, message; compact: A's and B's main values too.</summary>
        public Text StatusText { get; private set; }
        /// <summary>Expanded only: measured kick, ADS, recovery speed (bottom of the scrolling body).</summary>
        public Text DetailText { get; private set; }
        public WeaponRecoilEditor Editor { get; private set; }
        public WeaponPicker Picker { get; private set; }
        public ScrollRect Scroll => scroll;
        /// <summary>The scrolling body's visible part.</summary>
        public RectTransform Viewport => viewport;
        public bool IsOpen => Panel != null && Panel.gameObject.activeSelf;
        public bool IsExpanded => body != null && body.gameObject.activeSelf;
        /// <summary>Moved by a drag: the panel stays where it was put (and keeps its width).</summary>
        public bool UserMoved { get; private set; }
        /// <summary>Tallest the expanded panel gets (everything without scrolling, or the safe area's height).</summary>
        public float FullExpandedHeight => TopBlock + contentHeight + FooterBlock;
        public Button Control(string name) => buttons.TryGetValue(name, out var b) ? b : null;

        public static WeaponBalanceQuickPanel Create(WeaponBalanceLab lab, GunController gun, RectTransform canvas)
        {
            var safe = UiBuild.Stretch(UiBuild.Rect("QuickSafeArea", canvas));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var host = new GameObject("QuickPanelHost", typeof(RectTransform));
            host.transform.SetParent(safe, false);
            var q = host.AddComponent<WeaponBalanceQuickPanel>();
            q.lab = lab;
            q.gun = gun;
            q.safe = safe;
            q.motor = gun.GetComponent<FirstPersonMotor>();
            q.input = gun.GetComponent<PlayerInputRouter>();
            q.Build();
            lab.Refreshed += q.Refresh;
            return q;
        }

        void OnDestroy()
        {
            if (lab != null)
                lab.Refreshed -= Refresh;
            ReleaseHover();
        }

        void OnEnable() => TouchLayoutEditor.AnyOpenChanged += OnLayoutEditing;

        void OnDisable() => TouchLayoutEditor.AnyOpenChanged -= OnLayoutEditing;

        /// <summary>The "反動調整" button is shown: not while the HUD, the full lab or the layout editor is open.</summary>
        bool ToggleShown => !IsOpen && lab != null && !lab.IsOpen && !TouchLayoutEditor.AnyOpen;

        void OnLayoutEditing(bool editing)
        {
            if (ToggleButton == null)
                return;
            if (editing && IsOpen)
                SetOpen(false); // closed, not reopened afterwards
            ToggleButton.gameObject.SetActive(ToggleShown);
        }

        /// <summary>A button filling its cell (as the lab's).</summary>
        Button Make(RectTransform parent, string name, string label, TouchUi.Span cell, float y, float h, Action onClick, int font, Color color)
        {
            var b = TouchUi.CellButton(parent, name, label, cell, y, h, onClick, font, color, 0f, 0f);
            buttons[name] = b;
            return b;
        }

        Text Read(RectTransform parent, string name, TouchUi.Span span, float y, float h, int font)
        {
            var t = UiBuild.Text(TouchUi.Put(UiBuild.Rect(name, parent), span, y, h), "", font, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        void Build()
        {
            var host = (RectTransform)transform;
            UiBuild.Stretch(host);
            // Show: top band, left of the menu button (centre). Hidden while the HUD is open.
            ToggleButton = UiBuild.Button(host, "QuickToggle", "反動調整 ▼", new Vector2(0.5f, 1f), new Vector2(-300f, -4f - IY), new Vector2(220f, T - 2f * IY), () => SetOpen(!IsOpen), 26);
            ((RectTransform)ToggleButton.transform).pivot = new Vector2(0.5f, 1f);
            ((Image)ToggleButton.targetGraphic).color = new Color(0.35f, 0.08f, 0.08f, 0.85f);
            TouchUi.AddTouchArea((RectTransform)ToggleButton.transform, 10f, IY);

            Panel = UiBuild.Place(UiBuild.Rect("QuickPanel", host), new Vector2(0f, 1f), new Vector2(PreferredSpot.x, -PreferredSpot.y), new Vector2(WideWidth, CompactHeight));
            Panel.pivot = new Vector2(0f, 1f);
            Background = UiBuild.Image(Panel, Bg, true); // the face only is see-through; takes the touches on the panel (and only there)
            Panel.gameObject.AddComponent<HoverBlocker>().Owner = this;
            // Frame: four opaque edges (an Outline effect on the face would redraw the face itself and hide its transparency).
            foreach (var (min, max, offMin, offMax) in new[]
            {
                (new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), Vector2.zero),
                (new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f)),
                (new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f)),
                (new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-3f, 0f), Vector2.zero),
            })
            {
                var edge = UiBuild.Rect("Frame", Panel);
                edge.anchorMin = min;
                edge.anchorMax = max;
                edge.offsetMin = offMin;
                edge.offsetMax = offMax;
                UiBuild.Image(edge, new Color(1f, 0.4f, 0.32f, 1f), false);
            }

            // Header: drag handle, weapon drop-down, expand, close.
            var title = TouchUi.Put(UiBuild.Rect("Title", Panel), TouchUi.Span.L(Pad, HandleW), -Pad, HeaderH);
            UiBuild.Image(title, new Color(0.3f, 0.08f, 0.08f, 1f), true);
            UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("T", title)), "≡\n移動", SmallFont, TextAnchor.MiddleCenter);
            title.gameObject.AddComponent<DragForwarder>().Target = this;
            Make(Panel, "QExpand", "", TouchUi.Span.R(Pad + HeaderButtonW + Gap, HeaderButtonW), -Pad, HeaderH, () => SetExpanded(!IsExpanded), SmallFont, ButtonColor);
            Make(Panel, "QFold", "閉じる", TouchUi.Span.R(Pad, HeaderButtonW), -Pad, HeaderH, () => SetOpen(false), Font, ButtonColor);

            // State: compact, four lines; expanded, two.
            StatusText = Read(Panel, "Status", TouchUi.Span.S(Pad + 6f, Pad + 6f), -HeaderBlock, CompactStatus, SmallFont);

            // Expanded body: a scrolling list of the lab's rows.
            body = UiBuild.Stretch(UiBuild.Rect("Body", Panel));
            body.offsetMin = new Vector2(Pad, FooterBlock);
            body.offsetMax = new Vector2(-Pad, -TopBlock);
            viewport = UiBuild.Stretch(UiBuild.Rect("Viewport", body));
            UiBuild.Image(viewport, new Color(0f, 0f, 0f, 0f), true); // drags on empty parts scroll
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UiBuild.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            scroll = body.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            Editor = WeaponRecoilEditor.Build(lab, content, Vector2.zero, "Q", Row, Font, buttons, WeaponRecoilEditor.EditorLayout.Inline, 0f, RowStyle);
            float y = -Editor.Height - Gap;
            // Helpers (less used): a row of their own, in the body's colour, apart from the footer's actions.
            int i = 0;
            foreach (var (name, label, action) in new (string, string, Action)[]
            {
                ("QEnd", "試射を終了", lab.EndTest),
                ("QAimRef", "照準の基準に", lab.SetAimReference),
                ("QReset", "Bを正式値に戻す", lab.ResetEdited),
                ("QOpenLab", "詳細画面", () => { SetOpen(false); lab.Open(); }),
            })
                Quarter(content, name, label, i++, 4, y, action, HelperColor, SmallFont);
            y -= Row + Gap;
            DetailText = Read(content, "Detail", TouchUi.Span.S(4f, 4f), y, DetailHeight, SmallFont - 1);
            contentHeight = -y + DetailHeight + Pad;
            content.sizeDelta = new Vector2(0f, contentHeight);

            // Footer: the actions of a test round, always in reach.
            footer = UiBuild.Rect("Footer", Panel);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = Vector2.zero;
            footer.offsetMin = new Vector2(Pad, Pad);
            footer.offsetMax = new Vector2(-Pad, Pad + Row);
            i = 0;
            foreach (var (name, label, action) in new (string, string, Action)[]
            {
                ("QApply", "Bを試射に適用", lab.ApplyToTest),
                ("QRevert", "BをAに戻す", lab.RevertEditedToApplied),
                ("QAimReset", "照準リセット", lab.ResetAim),
            })
                Quarter(footer, name, label, i++, 3, 0f, action, ActionColor, Font);
            body.gameObject.SetActive(false);
            footer.gameObject.SetActive(false);
            // Drop-down last, so its list opens over the rows.
            Picker = WeaponPicker.Build(lab, Panel, "Q", TouchUi.Span.S(Pad + HandleW + Gap, Pad + 2f * (HeaderButtonW + Gap)), -Pad, HeaderH, Font, buttons, 440f);
            Readable(Panel);
            Panel.gameObject.SetActive(false);
            LayoutState();
            Refresh();
        }

        /// <summary>Cell <paramref name="index"/> of <paramref name="count"/> equal cells across a row (a gap between them).</summary>
        void Quarter(RectTransform parent, string name, string label, int index, int count, float y, Action onClick, Color color, int font)
        {
            var b = Make(parent, name, label, TouchUi.Span.L(0f, Row), y, Row, onClick, font, color);
            var r = (RectTransform)b.transform;
            r.anchorMin = new Vector2((float)index / count, r.anchorMin.y);
            r.anchorMax = new Vector2((float)(index + 1) / count, r.anchorMax.y);
            r.offsetMin = new Vector2(index == 0 ? 0f : Gap / 2f, r.offsetMin.y);
            r.offsetMax = new Vector2(index == count - 1 ? 0f : -Gap / 2f, r.offsetMax.y);
        }

        /// <summary>Texts on the see-through face get a dark outline (texts on buttons and fields sit on opaque faces).</summary>
        static void Readable(RectTransform root)
        {
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.GetComponentInParent<Selectable>(true) != null)
                    continue;
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.9f);
                o.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        public void SetOpen(bool open)
        {
            if (open && TouchLayoutEditor.AnyOpen)
                return; // not over the layout editor
            Panel.gameObject.SetActive(open);
            ToggleButton.gameObject.SetActive(ToggleShown);
            if (!open)
            {
                Picker.Close();
                ReleaseHover();
                return;
            }
            Fit();
            Refresh();
        }

        public void SetExpanded(bool expanded)
        {
            body.gameObject.SetActive(expanded);
            footer.gameObject.SetActive(expanded);
            Picker.Close();
            LayoutState();
            Fit();
            Refresh();
        }

        /// <summary>Compact: the state takes four lines. Expanded: two (the coarse / fine switch is in the rows' header).</summary>
        void LayoutState()
        {
            bool expanded = IsExpanded;
            TouchUi.Put(StatusText.rectTransform, TouchUi.Span.S(Pad + 6f, Pad + 6f), -HeaderBlock, expanded ? ExpandedStatus : CompactStatus);
        }

        /// <summary>
        /// Brings a part of the scrolling body into view (tests and the device check press controls by their screen position).
        /// </summary>
        public void Reveal(RectTransform target)
        {
            if (!IsExpanded || target == null || !target.IsChildOf(content))
                return;
            Canvas.ForceUpdateCanvases();
            var c = new Vector3[4];
            target.GetWorldCorners(c);
            float top = content.rect.yMax - content.InverseTransformPoint(c[1]).y - Gap;
            float bottom = content.rect.yMax - content.InverseTransformPoint(c[0]).y + Gap;
            float view = viewport.rect.height;
            float s = content.anchoredPosition.y;
            if (top < s)
                s = top;
            else if (bottom > s + view)
                s = bottom - view;
            scroll.velocity = Vector2.zero;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(s, 0f, Mathf.Max(0f, contentHeight - view)));
        }

        /// <summary>Width, size and spot for the state, clear of the crosshair and the HUD; kept after a drag.</summary>
        void Fit()
        {
            Canvas.ForceUpdateCanvases();
            var area = safe.rect.size;
            if (!IsExpanded)
            {
                if (UserMoved)
                    Panel.sizeDelta = new Vector2(Panel.sizeDelta.x, CompactHeight);
                else
                {
                    var (pos, width) = ChooseCompact(area, PreferredSpot, Obstacles(), CrosshairRect(), TextObstacles());
                    Panel.sizeDelta = new Vector2(width, CompactHeight);
                    Panel.anchoredPosition = pos;
                }
                Clamp();
                return;
            }
            float max = Mathf.Min(FullExpandedHeight, area.y);
            if (UserMoved)
            {
                Panel.sizeDelta = new Vector2(Panel.sizeDelta.x, Mathf.Max(MinExpandedHeight, Mathf.Min(max, area.y + Panel.anchoredPosition.y)));
                Clamp();
                return;
            }
            var (p, height, w) = ChooseExpanded(area, max, MinExpandedHeight, PreferredSpot, Obstacles(), CrosshairRect(), TextObstacles());
            Panel.sizeDelta = new Vector2(w, height);
            Panel.anchoredPosition = p;
        }

        void Clamp()
        {
            var area = safe.rect.size;
            var size = Panel.sizeDelta;
            var pos = Panel.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, area.x - size.x));
            pos.y = Mathf.Clamp(pos.y, -Mathf.Max(0f, area.y - size.y), 0f);
            Panel.anchoredPosition = pos;
        }

        /// <summary>A screen point in safe-area units, from its top left (y downwards).</summary>
        Vector2 ToArea(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe, screen, null, out var local);
            var r = safe.rect;
            return new Vector2(local.x - r.xMin, r.yMax - local.y);
        }

        Rect CrosshairRect()
        {
            var c = ToArea(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            return new Rect(c.x - CrosshairClearance, c.y - CrosshairClearance, 2f * CrosshairClearance, 2f * CrosshairClearance);
        }

        /// <summary>
        /// HUD parts to keep clear of (safe-area units, from the top left): every visible image of the other screen-space
        /// canvases (buttons, sticks, bars) except large touch zones and full-screen layers.
        /// </summary>
        public List<Rect> Obstacles() => HudParts(false);

        /// <summary>HUD texts (ammo, help, …): covered only when there is no other way.</summary>
        public List<Rect> TextObstacles() => HudParts(true);

        List<Rect> HudParts(bool texts)
        {
            var list = new List<Rect>();
            var c = new Vector3[4];
            foreach (var g in FindObjectsByType<Graphic>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled || g.color.a < 0.05f || g.canvas == null || !g.canvas.isActiveAndEnabled
                    || g.canvas.renderMode == RenderMode.WorldSpace || g.transform.IsChildOf(lab.transform) || g is Text != texts
                    || g is Text t && string.IsNullOrWhiteSpace(t.text)) // an empty text shows nothing
                    continue;
                var cam = g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera;
                g.rectTransform.GetWorldCorners(c);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                if (Mathf.Abs(b.x - a.x) > Screen.width * 0.45f || Mathf.Abs(b.y - a.y) > Screen.height * 0.7f)
                    continue;
                Vector2 p = ToArea(a), q = ToArea(b);
                list.Add(Rect.MinMaxRect(Mathf.Min(p.x, q.x) - ObstacleMargin, Mathf.Min(p.y, q.y) - ObstacleMargin,
                    Mathf.Max(p.x, q.x) + ObstacleMargin, Mathf.Max(p.y, q.y) + ObstacleMargin));
            }
            return list;
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        /// <summary>
        /// Top-left spot (anchored position: x right, y negative downwards) for a panel of <paramref name="size"/> in a
        /// safe area of <paramref name="area"/>: the least covered crosshair (weighted heavily) and HUD parts, then the
        /// nearest to <paramref name="preferred"/>. Rectangles are in area units from the top left, y downwards.
        /// </summary>
        public static Vector2 ChoosePosition(Vector2 area, Vector2 size, Vector2 preferred, IList<Rect> obstacles, Rect crosshair, IList<Rect> texts = null) =>
            ChoosePosition(area, size, preferred, obstacles, crosshair, texts, 8f, out _, out _);

        /// <summary>Weight of a covered control (and 50 × for the crosshair) against a covered text.</summary>
        const float HardWeight = 1000f;

        /// <summary>A score with no control and no crosshair covered (texts only).</summary>
        static bool Clean(float score) => score < HardWeight * 0.01f;

        static Vector2 ChoosePosition(Vector2 area, Vector2 size, Vector2 preferred, IList<Rect> obstacles, Rect crosshair, IList<Rect> texts, float step, out float score,
            out float textCover)
        {
            float maxX = Mathf.Max(0f, area.x - size.x), maxY = Mathf.Max(0f, area.y - size.y);
            // Candidates: a grid, plus the spots that just touch an edge of a HUD part or the crosshair (a gap narrower
            // than the grid step is still found).
            var xs = new List<float>();
            var ys = new List<float>();
            for (float v = 0f; v < maxX; v += step)
                xs.Add(v);
            for (float v = 0f; v < maxY; v += step)
                ys.Add(v);
            xs.Add(maxX);
            ys.Add(maxY);
            void Edges(Rect o)
            {
                xs.Add(o.xMax); xs.Add(o.xMin - size.x);
                ys.Add(o.yMax); ys.Add(o.yMin - size.y);
            }
            Edges(crosshair);
            foreach (var o in obstacles)
                Edges(o);
            float bestScore = float.MaxValue, bestDist = float.MaxValue, bestText = 0f;
            Vector2 best = new Vector2(Mathf.Min(preferred.x, maxX), Mathf.Min(preferred.y, maxY));
            foreach (float cx in xs)
            {
                if (cx < 0f || cx > maxX)
                    continue;
                foreach (float cy in ys)
                {
                    if (cy < 0f || cy > maxY)
                        continue;
                    var r = new Rect(cx, cy, size.x, size.y);
                    float sc = Overlap(r, crosshair) * 50f;
                    foreach (var o in obstacles)
                        sc += Overlap(r, o);
                    sc *= HardWeight;
                    float tc = 0f;
                    if (texts != null)
                        foreach (var o in texts)
                            tc += Overlap(r, o);
                    sc += tc;
                    float dist = (new Vector2(cx, cy) - preferred).sqrMagnitude;
                    if (sc < bestScore - 0.5f || sc <= bestScore + 0.5f && dist < bestDist)
                    {
                        bestScore = sc;
                        bestDist = dist;
                        bestText = tc;
                        best = new Vector2(cx, cy);
                    }
                }
            }
            score = bestScore;
            textCover = bestText;
            return new Vector2(best.x, -best.y);
        }

        /// <summary>
        /// Expanded spot and height for a panel <paramref name="width"/> wide: the tallest height from
        /// <paramref name="maxHeight"/> down to <paramref name="minHeight"/> that covers neither the crosshair nor a HUD
        /// control (texts as little as possible); if none, the least covering one (taller first on a tie). A height that covers
        /// no text either is taken when it is at most <see cref="TextYield"/> shorter; otherwise texts do not shorten the panel.
        /// </summary>
        public static (Vector2 pos, float height) ChooseSpot(Vector2 area, float width, float maxHeight, float minHeight, Vector2 preferred, IList<Rect> obstacles, Rect crosshair,
            IList<Rect> texts = null)
        {
            var (pos, height, _, _) = ChooseSpotScored(area, width, maxHeight, minHeight, preferred, obstacles, crosshair, texts);
            return (pos, height);
        }

        /// <summary>How much shorter the expanded panel may get to keep clear of HUD texts (about one touch row).</summary>
        public const float TextYield = 112f;

        /// <summary>Spot, height and score; <c>clear</c> when it covers neither the crosshair nor a HUD control.</summary>
        static (Vector2 pos, float height, float score, bool clear) ChooseSpotScored(Vector2 area, float width, float maxHeight, float minHeight, Vector2 preferred,
            IList<Rect> obstacles, Rect crosshair, IList<Rect> texts)
        {
            float bestScore = float.MaxValue;
            (Vector2, float, float, bool) best = (Vector2.zero, minHeight, float.MaxValue, false);
            (Vector2, float, float, bool) tallest = default;
            bool found = false;
            for (float h = maxHeight; h >= minHeight - 0.01f; h -= 16f)
            {
                var size = new Vector2(width, h);
                var pos = ChoosePosition(area, size, preferred, obstacles, crosshair, texts, 16f, out float score, out float text);
                if (Clean(score - text))
                {
                    if (found && h < tallest.Item2 - TextYield)
                        return tallest;
                    pos = ChoosePosition(area, size, preferred, obstacles, crosshair, texts, 8f, out score, out text); // refined at the finer step
                    if (Clean(score))
                        return (pos, h, score, true);
                    if (!found)
                    {
                        found = true;
                        tallest = (pos, h, score, true);
                    }
                    continue;
                }
                if (!found && score < bestScore - 0.5f)
                {
                    bestScore = score;
                    best = (pos, h, score, false);
                }
            }
            return found ? tallest : best;
        }

        /// <summary>
        /// Compact spot and width: the widest of <see cref="Widths"/> that keeps clear of the crosshair and the HUD's
        /// controls (if none does, the least covering).
        /// </summary>
        public static (Vector2 pos, float width) ChooseCompact(Vector2 area, Vector2 preferred, IList<Rect> obstacles, Rect crosshair, IList<Rect> texts = null)
        {
            Vector2 best = Vector2.zero;
            float bestWidth = WideWidth, bestScore = float.MaxValue;
            foreach (float w in Widths)
            {
                var pos = ChoosePosition(area, new Vector2(w, CompactHeight), preferred, obstacles, crosshair, texts, 8f, out float score, out float text);
                if (Clean(score - text))
                    return (pos, w);
                if (score < bestScore - 0.5f)
                {
                    best = pos;
                    bestWidth = w;
                    bestScore = score;
                }
            }
            return (best, bestWidth);
        }

        /// <summary>
        /// Expanded spot, height and width: from <see cref="Widths"/>, widest first, a narrower one is taken when only it
        /// keeps clear of the crosshair and the HUD's controls, or when it is at least <see cref="NarrowGain"/> taller.
        /// </summary>
        public static (Vector2 pos, float height, float width) ChooseExpanded(Vector2 area, float maxHeight, float minHeight, Vector2 preferred, IList<Rect> obstacles,
            Rect crosshair, IList<Rect> texts = null)
        {
            var (bp, bh, bs, bc) = ChooseSpotScored(area, Widths[0], maxHeight, minHeight, preferred, obstacles, crosshair, texts);
            float bw = Widths[0];
            for (int i = 1; i < Widths.Length; i++)
            {
                var (p, h, sc, c) = ChooseSpotScored(area, Widths[i], maxHeight, minHeight, preferred, obstacles, crosshair, texts);
                bool take = c != bc ? c : c ? h >= bh + NarrowGain : sc < bs - 0.5f;
                if (take)
                    (bp, bh, bs, bc, bw) = (p, h, sc, c, Widths[i]);
            }
            return (bp, bh, bw);
        }

        // Mouse only: hold the player's input while the pointer is over the panel (touches are separated by the EventSystem).
        void HoverEnter()
        {
            if (input != null && input.Mode == InputMode.Desktop && !hoverBlocked)
            {
                hoverBlocked = true;
                input.Block(HoverBlockReason);
            }
        }

        /// <summary>True while a mouse over the panel holds the player's input.</summary>
        public bool HoldingInputForMouse => hoverBlocked;

        /// <summary>On the panel itself (entering its buttons does not leave it).</summary>
        sealed class HoverBlocker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public WeaponBalanceQuickPanel Owner;
            public void OnPointerEnter(PointerEventData e) => Owner.HoverEnter();
            public void OnPointerExit(PointerEventData e) => Owner.ReleaseHover();
        }

        void ReleaseHover()
        {
            if (!hoverBlocked)
                return;
            hoverBlocked = false;
            input?.Unblock(HoverBlockReason); // nothing pressed carries over
        }

        /// <summary>Drag by the title: the panel follows, kept inside the safe area.</summary>
        public void OnDrag(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            UserMoved = true;
            Panel.anchoredPosition += e.delta / scale;
            Clamp();
        }

        /// <summary>Passes drags of the title to the panel.</summary>
        sealed class DragForwarder : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public WeaponBalanceQuickPanel Target;
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e) => Target.OnDrag(e);
        }

        static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Main values of a recoil in one short line: direction, strength, base angles, recovery amount.</summary>
        public static string Main(RecoilProfile r) =>
            !r.enabled
                ? $"従来（上・左右ランダム） 縦{F(r.pitchDegrees)}° 横±{F(r.yawDegrees)}° 回復量 {RecoilParams.Shown(RecoilParam.RecoveryAmount, FirstPersonMotor.DefaultRecoveryFraction)}"
                : $"{RecoilCalculator.DirectionName(r)}・{(r.strength == RecoilStrength.Weak ? "弱" : r.strength == RecoilStrength.Strong ? "強" : "中")} 縦{F(r.pitchDegrees)}° 横{F(r.yawDegrees)}° 回復量 {RecoilParams.Shown(RecoilParam.RecoveryAmount, r.recoveryFraction)}";

        void Update()
        {
            if (IsOpen && (lab.IsOpen || TouchLayoutEditor.AnyOpen))
                SetOpen(false); // the full lab or the layout editor is on top
            bool showToggle = ToggleShown;
            if (ToggleButton.gameObject.activeSelf != showToggle)
                ToggleButton.gameObject.SetActive(showToggle);
            if (IsOpen)
                RefreshStatus();
        }

        public void Refresh()
        {
            if (Panel == null)
                return;
            buttons["QExpand"].GetComponentInChildren<Text>().text = IsExpanded ? "縮小\n▲" : "展開\n▼";
            Picker.Refresh();
            Editor.Refresh();
            RefreshStatus();
        }

        void RefreshStatus()
        {
            if (StatusText == null)
                return;
            var id = lab.SelectedId;
            var a = lab.Applied(id);
            var b = lab.Edited(id);
            int diffs = WeaponBalanceProfile.Diff(a, b).Count;
            string state = lab.IsTesting(id) ? "試射中：Bを適用済み（A）" : "試射なし（A＝正式値）";
            string message = lab.Message != null ? lab.Message.text : "";
            string head = state + (diffs == 0 ? "｜B＝A" : $"｜<color=#FFC04D>B未適用 {diffs}項目</color>");
            string msg = string.IsNullOrEmpty(message) ? "" : $"\n<color={(lab.MessageIsError ? "#FF7366" : "#FFC04D")}>{message}</color>";
            StatusText.text = IsExpanded
                ? head + msg
                : head + "\nA " + Main(a.recoil) + "\nB " + (diffs == 0 ? "＝A" : Main(b.recoil)) + msg;
            if (!IsExpanded)
                return;
            var rem = motor != null ? motor.RecoveryRemaining : Vector2.zero;
            DetailText.text = string.Format(CultureInfo.InvariantCulture,
                "実測 縦{0:+0.00;-0.00;0.00}° 横{1:+0.00;-0.00;0.00}°｜ADS {2:0.00}\n回復速度 {3:0.0}°/秒 残り 縦{4:0.00}° 横{5:0.00}°",
                gun.LastRecoil.x, gun.LastRecoil.y, gun.AdsBlend, motor != null ? motor.CurrentRecoverySpeed : 0f, rem.y, rem.x);
        }
    }
}
