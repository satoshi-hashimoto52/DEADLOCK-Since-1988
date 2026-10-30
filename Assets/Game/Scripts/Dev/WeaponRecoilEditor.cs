using System;
using System.Collections.Generic;
using System.Globalization;
using Deadlock.Combat;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>The recoil numbers the lab edits with a slider and a number field.</summary>
    public enum RecoilParam
    {
        Pitch,
        Yaw,
        Ads,
        RecoveryAmount,
        RecoverySpeed,
    }

    /// <summary>The rows of the A / B recoil editor (direction rows carry their base angle).</summary>
    public enum RecoilRow
    {
        Vertical,
        Horizontal,
        Strength,
        Ads,
        RecoveryAmount,
        RecoverySpeed,
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Ranges, steps and texts of the recoil numbers, shared by the full lab and the in-game panel.
    /// Coarse: the slider spans the whole range. Fine: it spans a small window around the value, at a smaller step.
    /// </summary>
    public static class RecoilParams
    {
        public const float MaxAngle = 10f;

        public static float Min(RecoilParam p) => p == RecoilParam.RecoverySpeed ? RecoilProfile.MinRecoverySpeed : 0f;

        public static float Max(RecoilParam p) => p switch
        {
            RecoilParam.Pitch or RecoilParam.Yaw => MaxAngle,
            RecoilParam.RecoverySpeed => RecoilProfile.MaxRecoverySpeed,
            _ => 1f,
        };

        public static float Step(RecoilParam p, bool fine) => p == RecoilParam.RecoverySpeed ? (fine ? 0.1f : 0.5f) : (fine ? 0.01f : 0.05f);

        /// <summary>Half the width of the fine slider's window.</summary>
        public static float FineSpan(RecoilParam p) => p switch
        {
            RecoilParam.Pitch or RecoilParam.Yaw => 0.5f,
            RecoilParam.RecoverySpeed => 3f,
            _ => 0.1f,
        };

        public static string Label(RecoilParam p) => p switch
        {
            RecoilParam.Pitch => "縦の基準角",
            RecoilParam.Yaw => "横の基準角",
            RecoilParam.Ads => "ADS反動倍率",
            RecoilParam.RecoveryAmount => "回復量",
            _ => "回復速度",
        };

        /// <summary>Unit of the shown number: 0.70°, 0.55倍, 50%, 9.0°/秒.</summary>
        public static string Unit(RecoilParam p) => p switch
        {
            RecoilParam.Pitch or RecoilParam.Yaw => "°",
            RecoilParam.Ads => "倍",
            RecoilParam.RecoveryAmount => "%",
            _ => "°/秒",
        };

        /// <summary>Shown number per stored value: the recovery amount (a fraction) is shown and typed in percent.</summary>
        public static float Scale(RecoilParam p) => p == RecoilParam.RecoveryAmount ? 100f : 1f;

        public static string Format(RecoilParam p) => p switch
        {
            RecoilParam.RecoverySpeed => "0.0",
            RecoilParam.RecoveryAmount => "0.#",
            _ => "0.00",
        };

        /// <summary>The number of a value as shown and typed (no unit).</summary>
        public static string Text(RecoilParam p, float v) => (v * Scale(p)).ToString(Format(p), CultureInfo.InvariantCulture);

        /// <summary>The number with its unit.</summary>
        public static string Shown(RecoilParam p, float v) => Text(p, v) + Unit(p);

        /// <summary>A typed number (shown units) as a stored value; false when it is not a number.</summary>
        public static bool TryParse(RecoilParam p, string text, out float value)
        {
            bool ok = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
            value = ok ? v / Scale(p) : 0f;
            return ok;
        }

        public static float Get(RecoilProfile r, RecoilParam p) => p switch
        {
            RecoilParam.Pitch => r.pitchDegrees,
            RecoilParam.Yaw => r.yawDegrees,
            RecoilParam.Ads => r.adsMultiplier,
            RecoilParam.RecoveryAmount => r.recoveryFraction,
            _ => r.recoverySpeed,
        };

        public static void Set(RecoilProfile r, RecoilParam p, float v)
        {
            switch (p)
            {
                case RecoilParam.Pitch: r.pitchDegrees = v; break;
                case RecoilParam.Yaw: r.yawDegrees = v; break;
                case RecoilParam.Ads: r.adsMultiplier = v; break;
                case RecoilParam.RecoveryAmount: r.recoveryFraction = v; break;
                default: r.recoverySpeed = v; break;
            }
        }

        /// <summary>Inside the range (a typed number outside it is refused, never clamped silently).</summary>
        public static bool InRange(RecoilParam p, float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= Min(p) - 1e-4f && v <= Max(p) + 1e-4f;

        /// <summary>
        /// The slider's window: start value and number of steps. Coarse: the whole range. Fine: 2 × <see cref="FineSpan"/>
        /// around the value; an earlier window start (<paramref name="keepLo"/>) is kept while the value stays inside it, so
        /// the handle does not jump under the finger.
        /// </summary>
        public static (float lo, int steps) Window(RecoilParam p, bool fine, float value, float? keepLo = null)
        {
            float step = Step(p, fine), min = Min(p), max = Max(p);
            if (!fine)
                return (min, Mathf.RoundToInt((max - min) / step));
            float span = FineSpan(p);
            int steps = Mathf.RoundToInt(2f * span / step);
            if (keepLo is float k && value >= k - 1e-4f && value <= k + 2f * span + 1e-4f)
                return (k, steps);
            float lo = Mathf.Clamp((float)Math.Round(value - span, 2), min, max - 2f * span);
            return (lo, steps);
        }

        /// <summary>The value of slider step <paramref name="index"/> in a window.</summary>
        public static float ValueAt(RecoilParam p, bool fine, float lo, int index) =>
            Mathf.Clamp((float)Math.Round(lo + index * Step(p, fine), 3), Min(p), Max(p));

        public static int IndexOf(RecoilParam p, bool fine, float lo, float value) => Mathf.RoundToInt((value - lo) / Step(p, fine));

        static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

        static string StrengthText(RecoilProfile r) => !r.enabled ? "従来" : r.strength switch { RecoilStrength.Weak => "弱", RecoilStrength.Strong => "強", _ => "中" };

        /// <summary>
        /// Text of one editor row for a profile (A or B column; equal texts = no difference). A disabled profile is the
        /// earlier recoil: up at the pitch, random sideways within the yaw, half recovering at <paramref name="legacySpeed"/>.
        /// </summary>
        public static string RowText(RecoilProfile r, RecoilRow row, float legacySpeed)
        {
            switch (row)
            {
                case RecoilRow.Vertical:
                    if (!r.enabled)
                        return $"上 {F(r.pitchDegrees)}°";
                    return r.vertical == RecoilVertical.None ? "なし" : $"{(r.vertical == RecoilVertical.Up ? "上" : "下")} {F(r.pitchDegrees)}°";
                case RecoilRow.Horizontal:
                    if (!r.enabled)
                        return $"ランダム ±{F(r.yawDegrees)}°";
                    return r.horizontal switch
                    {
                        RecoilHorizontal.None => "なし",
                        RecoilHorizontal.Left => $"左 {F(r.yawDegrees)}°",
                        RecoilHorizontal.Right => $"右 {F(r.yawDegrees)}°",
                        _ => $"交互 {F(r.yawDegrees)}°",
                    };
                case RecoilRow.Strength:
                    return StrengthText(r);
                case RecoilRow.Ads:
                    return Shown(RecoilParam.Ads, r.adsMultiplier);
                case RecoilRow.RecoveryAmount:
                    return Shown(RecoilParam.RecoveryAmount, r.enabled ? r.recoveryFraction : Deadlock.Player.FirstPersonMotor.DefaultRecoveryFraction);
                default:
                    return Shown(RecoilParam.RecoverySpeed, r.enabled ? r.recoverySpeed : legacySpeed);
            }
        }

        /// <summary>
        /// The A / B comparison items (direction, strength, vertical / horizontal base angle, ADS factor, recovery amount and
        /// speed): item name, A text, B text. Texts differ exactly when the values that matter differ.
        /// </summary>
        public static List<(string item, string a, string b)> Compare(RecoilProfile a, RecoilProfile b, float legacySpeed)
        {
            string Dir(RecoilProfile r) => r.enabled ? RecoilCalculator.DirectionName(r) : "従来（上・左右ランダム）";
            return new List<(string, string, string)>
            {
                ("反動方向", Dir(a), Dir(b)),
                ("反動強度", StrengthText(a), StrengthText(b)),
                ("縦の基準角", F(a.pitchDegrees) + "°", F(b.pitchDegrees) + "°"),
                ("横の基準角", F(a.yawDegrees) + "°", F(b.yawDegrees) + "°"),
                ("ADS反動倍率", RowText(a, RecoilRow.Ads, legacySpeed), RowText(b, RecoilRow.Ads, legacySpeed)),
                ("回復量", RowText(a, RecoilRow.RecoveryAmount, legacySpeed), RowText(b, RecoilRow.RecoveryAmount, legacySpeed)),
                ("回復速度", RowText(a, RecoilRow.RecoverySpeed, legacySpeed), RowText(b, RecoilRow.RecoverySpeed, legacySpeed)),
            };
        }

        /// <summary>Number of comparison items where A and B differ.</summary>
        public static int DiffCount(RecoilProfile a, RecoilProfile b, float legacySpeed)
        {
            int n = 0;
            foreach (var (_, x, y) in Compare(a, b, legacySpeed))
                if (x != y)
                    n++;
            return n;
        }
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Layout helpers of the lab's screens: a part of a row is placed from the left edge, from the right
    /// edge or stretched between two insets, so rows follow the width they get. A touch control is drawn smaller than its
    /// cell and takes touches on the whole cell (a transparent "Hit" child); cells of neighbours never overlap.
    /// </summary>
    public static class TouchUi
    {
        public enum Edge
        {
            Left,
            Right,
            Stretch,
        }

        /// <summary>Horizontal place: Left (x, width), Right (inset from the right edge, width) or Stretch (left inset, right inset).</summary>
        public readonly struct Span
        {
            public readonly Edge Edge;
            public readonly float A, B;

            Span(Edge edge, float a, float b)
            {
                Edge = edge;
                A = a;
                B = b;
            }

            public static Span L(float x, float width) => new Span(Edge.Left, x, width);
            public static Span R(float right, float width) => new Span(Edge.Right, right, width);
            public static Span S(float left, float right) => new Span(Edge.Stretch, left, right);

            /// <summary>The same place made narrower by <paramref name="d"/> on both sides.</summary>
            public Span Inset(float d) => Edge == Edge.Stretch ? new Span(Edge, A + d, B + d) : new Span(Edge, A + d, B - 2f * d);
        }

        /// <summary>Puts <paramref name="r"/> on a row whose top is <paramref name="y"/> (negative downwards from the parent's top).</summary>
        public static RectTransform Put(RectTransform r, Span s, float y, float h)
        {
            r.pivot = new Vector2(0f, 1f);
            switch (s.Edge)
            {
                case Edge.Left:
                    r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                    r.offsetMin = new Vector2(s.A, y - h);
                    r.offsetMax = new Vector2(s.A + s.B, y);
                    break;
                case Edge.Right:
                    r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
                    r.offsetMin = new Vector2(-s.A - s.B, y - h);
                    r.offsetMax = new Vector2(-s.A, y);
                    break;
                default:
                    r.anchorMin = new Vector2(0f, 1f);
                    r.anchorMax = new Vector2(1f, 1f);
                    r.offsetMin = new Vector2(s.A, y - h);
                    r.offsetMax = new Vector2(-s.B, y);
                    break;
            }
            return r;
        }

        /// <summary>Touch area of a control drawn inside its cell: a transparent child reaching the cell's edges.</summary>
        public static RectTransform AddTouchArea(RectTransform control, float padX, float padY)
        {
            var hit = UiBuild.Rect("Hit", control);
            hit.anchorMin = Vector2.zero;
            hit.anchorMax = Vector2.one;
            hit.offsetMin = new Vector2(-padX, -padY);
            hit.offsetMax = new Vector2(padX, padY);
            UiBuild.Image(hit, new Color(0f, 0f, 0f, 0f), true);
            hit.SetAsFirstSibling();
            return hit;
        }

        /// <summary>The rect that takes the control's touches (its touch area, or the control itself).</summary>
        public static RectTransform TouchAreaOf(Component control)
        {
            var hit = control.transform.Find("Hit");
            return hit != null ? (RectTransform)hit : (RectTransform)control.transform;
        }

        /// <summary>
        /// Button drawn inset in its cell (<paramref name="insetX"/>, <paramref name="insetY"/>); the whole cell takes the touch.
        /// </summary>
        public static Button CellButton(RectTransform parent, string name, string label, Span cell, float y, float h, Action onClick, int font, Color color,
            float insetX, float insetY)
        {
            var b = UiBuild.Button(parent, name, label, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, onClick, font);
            var r = Put((RectTransform)b.transform, cell.Inset(insetX), y - insetY, h - 2f * insetY);
            ((Image)b.targetGraphic).color = color;
            b.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
            if (insetX > 0f || insetY > 0f)
                AddTouchArea(r, insetX, insetY);
            return b;
        }

        /// <summary><paramref name="s"/> cut with "…" to fit <paramref name="width"/> units of <paramref name="t"/>'s font.</summary>
        public static string Ellipsize(Text t, string s, float width)
        {
            var settings = t.GetGenerationSettings(Vector2.zero);
            float W(string x) => t.cachedTextGeneratorForLayout.GetPreferredWidth(x, settings) / t.pixelsPerUnit;
            if (string.IsNullOrEmpty(s) || W(s) <= width)
                return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string cut = s.Substring(0, n).TrimEnd() + "…";
                if (W(cut) <= width)
                    return cut;
            }
            return "…";
        }

        public static float TextWidth(Text t, string s) =>
            t.cachedTextGeneratorForLayout.GetPreferredWidth(s, t.GetGenerationSettings(Vector2.zero)) / t.pixelsPerUnit;
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Slider for a scrolling list: a tap sets the value under the finger when it lifts; a drag moves
    /// the slider only when it starts sideways, an upward or downward drag scrolls the list (no change of the value).
    /// Without a list around it, every drag moves the slider.
    /// </summary>
    public sealed class TouchSlider : Slider, IBeginDragHandler, IEndDragHandler
    {
        ScrollRect scroll;
        bool pressed, sliding, scrolling;

        /// <summary>The last drag scrolled the list instead of moving the slider.</summary>
        public bool LastDragScrolled { get; private set; }

        public override void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable())
                return;
            pressed = true;
            sliding = scrolling = false;
        }

        public override void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = true; // the direction is known before the drag starts

        public void OnBeginDrag(PointerEventData e)
        {
            if (!pressed)
                return;
            var d = e.position - e.pressPosition;
            if (scroll == null)
                scroll = GetComponentInParent<ScrollRect>();
            LastDragScrolled = scroll != null && scroll.vertical && Mathf.Abs(d.y) > Mathf.Abs(d.x);
            if (LastDragScrolled)
            {
                scrolling = true;
                scroll.OnInitializePotentialDrag(e);
                scroll.OnBeginDrag(e);
                return;
            }
            sliding = true;
            base.OnPointerDown(e); // jumps to the finger, or holds the handle where it was grabbed
        }

        public override void OnDrag(PointerEventData e)
        {
            if (scrolling)
                scroll.OnDrag(e);
            else if (sliding)
                base.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (scrolling)
                scroll.OnEndDrag(e);
            scrolling = sliding = false;
        }

        public override void OnPointerUp(PointerEventData e)
        {
            if (pressed && !sliding && !scrolling && e.dragging == false)
                base.OnPointerDown(e); // a tap: the value under the finger
            pressed = false;
            base.OnPointerUp(e);
        }
    }

    /// <summary>DEVELOPMENT ONLY. Number field in a scrolling list: a drag on it scrolls the list unless the field is being typed in.</summary>
    public sealed class TouchInputField : InputField
    {
        ScrollRect scroll;
        bool scrolling;

        public override void OnBeginDrag(PointerEventData e)
        {
            if (scroll == null)
                scroll = GetComponentInParent<ScrollRect>();
            scrolling = !isFocused && scroll != null;
            if (scrolling)
            {
                scroll.OnInitializePotentialDrag(e);
                scroll.OnBeginDrag(e);
                return;
            }
            base.OnBeginDrag(e);
        }

        public override void OnDrag(PointerEventData e)
        {
            if (scrolling)
                scroll.OnDrag(e);
            else
                base.OnDrag(e);
        }

        public override void OnEndDrag(PointerEventData e)
        {
            if (scrolling)
                scroll.OnEndDrag(e);
            else
                base.OnEndDrag(e);
            scrolling = false;
        }
    }

    /// <summary>
    /// DEVELOPMENT ONLY. The recoil editor rows, built the same way in the full lab and in the in-game panel: B (edited,
    /// editable) with its slider and number, then A (applied, read only) in a column of its own, tinted where they differ.
    /// Rows stretch with the width they get; the slider takes the room left.
    /// <code>
    /// Inline (the full lab, one line a row):
    /// 上下  [上|下|なし] ──────────●────────── [0.70°]  A 上 0.70°
    /// 強度  [弱|中|強]  [左右交互：OFF]                  A 中
    /// ADS   ────────────────●───────────────── [0.55倍] A 0.55倍
    /// Touch (the in-game panel, 44 pt cells; a direction takes two lines):
    /// [上][下][なし] [0.70°]
    /// 上下 ──────●────────  A 上 0.70°
    /// [弱][中][強][左右交互]  A 中
    /// ADS ───●───  [0.55倍]  A 0.55倍
    /// </code>
    /// A direction of "なし" disables its slider and number (the angle is kept). Coarse / fine switches the slider's
    /// range and step; the number field takes a typed value (outside the range: refused with a message).
    /// </summary>
    public sealed class WeaponRecoilEditor
    {
        /// <summary>
        /// Inline: one line per item (the full lab's recoil tab). Touch: cells of a finger's size (44 pt ≈ 104 units on an
        /// iPhone 16 Pro in landscape), drawn smaller inside them; a direction puts its slider on a second line (the in-game panel).
        /// </summary>
        public enum EditorLayout
        {
            Inline,
            Touch,
        }

        /// <summary>Touch cell size of the touch layout: 44 pt on an iPhone 16 Pro in landscape (941 units for 402 pt).</summary>
        public const float TouchSize = 104f;
        /// <summary>A touch control is drawn this much inside its cell, top and bottom (84 units ≈ 36 pt high).</summary>
        public const float TouchInsetY = 10f;
        /// <summary>… and left and right (neighbouring cells keep a visible gap).</summary>
        public const float TouchInsetX = 4f;

        // Inline columns: label, direction buttons, slider (stretching), number, A.
        const float InHeader = 30f, InGap = 4f, InSegW = 46f, InSegPitch = 49f;
        // From the style (InlineStyle.Lab gives the full lab's columns: 54 / 204 / 58 / 108 / 100).
        float InSegX => style.LabelWidth + 6f;
        float InDirSliderX => InSegX + 3f * InSegPitch + 3f;
        float InScalarSliderX => style.OneLineLabels ? InSegX : 58f;
        float InAW => style.AWidth;
        float InFieldW => style.FieldWidth;
        float InFieldRight => 4f + InAW + 6f;
        float InSliderRight => InFieldRight + InFieldW + 6f;

        /// <summary>
        /// Look of the inline rows. <see cref="Lab"/>: the full lab's (two-line labels for the scalar rows, full-height
        /// sliders). The in-game panel uses a wider label column with every label on one line, thin slider tracks (the
        /// slider's touch area stays the whole cell) and roomier A / number columns.
        /// </summary>
        public sealed class InlineStyle
        {
            public float LabelWidth = 48f;
            public bool OneLineLabels;
            public bool ThinSliders;
            public float AWidth = 108f;
            public float FieldWidth = 100f;
            /// <summary>The coarse / fine switch takes the header from the label column to the number column (its full text on one line).</summary>
            public bool WideFineToggle;

            public static InlineStyle Lab => new InlineStyle();
        }

        /// <summary>Drawn height of a thin slider's track (units); the slider still takes touches over its whole row.</summary>
        public const float ThinTrack = 6f;
        /// <summary>Drawn width of a thin slider's handle (units).</summary>
        public const float ThinHandle = 14f;
        // Touch cells.
        const float TA = 112f, TFieldW = 116f;

        static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        static readonly Color Chosen = new Color(0.62f, 0.42f, 0.08f, 1f);
        static readonly Color EditedColor = new Color(1f, 0.75f, 0.3f, 1f);
        static readonly Color RowColor = new Color(1f, 1f, 1f, 0.03f);
        static readonly Color RowDiffColor = new Color(0.55f, 0.35f, 0.05f, 0.45f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);

        readonly WeaponBalanceLab lab;
        readonly string prefix;
        readonly float rowHeight;
        readonly int font;
        readonly EditorLayout layout;
        readonly InlineStyle style;
        readonly Dictionary<string, Button> buttons;
        readonly Dictionary<RecoilParam, Slider> sliders = new Dictionary<RecoilParam, Slider>();
        readonly Dictionary<RecoilParam, InputField> inputs = new Dictionary<RecoilParam, InputField>();
        readonly Dictionary<RecoilParam, float> windowLo = new Dictionary<RecoilParam, float>();
        readonly Dictionary<RecoilRow, Text> aTexts = new Dictionary<RecoilRow, Text>();
        readonly Dictionary<RecoilRow, Image> rowBacks = new Dictionary<RecoilRow, Image>();
        bool refreshing;
        bool windowFine;
        string windowWeapon;

        public RectTransform Root { get; private set; }
        public EditorLayout Layout => layout;
        public InlineStyle Style => style;
        bool Touch => layout == EditorLayout.Touch;
        /// <summary>Touch: eight cell rows, no header (the panel has the coarse / fine switch). Inline: a header and six rows.</summary>
        public float Height => Touch ? 8f * rowHeight : InHeader + InGap + 6f * (rowHeight + InGap);
        public Slider SliderOf(RecoilParam p) => sliders[p];
        public InputField InputOf(RecoilParam p) => inputs[p];
        public Text AText(RecoilRow row) => aTexts[row];
        /// <summary>Row tinted: A and B differ in it.</summary>
        public bool RowDiffers(RecoilRow row) => rowBacks[row].color == RowDiffColor;

        WeaponRecoilEditor(WeaponBalanceLab lab, string prefix, float rowHeight, int font, EditorLayout layout, Dictionary<string, Button> buttons,
            InlineStyle style)
        {
            this.style = style ?? InlineStyle.Lab;
            this.lab = lab;
            this.prefix = prefix;
            this.rowHeight = rowHeight;
            this.font = font;
            this.layout = layout;
            this.buttons = buttons;
        }

        /// <summary>
        /// Builds the rows from <paramref name="pos"/> (top left) of <paramref name="parent"/> to <paramref name="rightInset"/>
        /// from its right edge (the rows follow the parent's width); buttons go into <paramref name="registry"/> by name.
        /// </summary>
        public static WeaponRecoilEditor Build(WeaponBalanceLab lab, RectTransform parent, Vector2 pos, string prefix, float rowHeight, int font, Dictionary<string, Button> registry,
            EditorLayout layout = EditorLayout.Inline, float rightInset = 0f, InlineStyle style = null)
        {
            var e = new WeaponRecoilEditor(lab, prefix, rowHeight, font, layout, registry ?? new Dictionary<string, Button>(), style);
            e.Root = TouchUi.Put(UiBuild.Rect(prefix + "RecoilEditor", parent), TouchUi.Span.S(pos.x, rightInset), pos.y, e.Height);
            if (e.Touch)
                e.BuildTouch();
            else
                e.BuildInline();
            return e;
        }

        Text Label(string name, string text, TouchUi.Span span, float y, float h, int size, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = UiBuild.Text(TouchUi.Put(UiBuild.Rect(name, Root), span, y, h), text, size, align);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        /// <summary>A row's name in the label column (one line in the in-game panel's style).</summary>
        Text RowLabel(string name, string text, float width, float y, float h, int size)
        {
            var t = Label(name, style.OneLineLabels ? text.Replace("\n", "") : text, TouchUi.Span.L(4f, width), y, h, size);
            if (style.OneLineLabels)
                t.horizontalOverflow = HorizontalWrapMode.Overflow; // never wraps (the column is sized for the longest)
            return t;
        }

        /// <summary>A button in a cell: drawn inset in the touch layout (the cell takes the touch), filling it inline.</summary>
        Button Button(string name, string label, TouchUi.Span cell, float y, float h, Action onClick, int size = 0)
        {
            var b = TouchUi.CellButton(Root, prefix + name, label, cell, y, h, onClick, size > 0 ? size : font, ButtonColor,
                Touch ? TouchInsetX : 0f, Touch ? TouchInsetY : 0f);
            buttons[prefix + name] = b;
            return b;
        }

        void RowBack(RecoilRow row, float y, float h) =>
            rowBacks[row] = UiBuild.Image(TouchUi.Put(UiBuild.Rect("Row_" + row, Root), TouchUi.Span.S(0f, 0f), y, h), RowColor, false);

        void AColumn(RecoilRow row, TouchUi.Span span, float y, float h, int size) =>
            aTexts[row] = Label(prefix + "A_" + row, "", span, y, h, size, TextAnchor.MiddleCenter);

        void Segment(float y, float h, float x0, float pitch, float w, (string name, string text, Action pick)[] items)
        {
            float x = x0;
            foreach (var (name, text, pick) in items)
            {
                Button(name, text, TouchUi.Span.L(x, w), y, h, pick);
                x += pitch;
            }
        }

        static (string, string, Action)[] Directions(WeaponBalanceLab lab, bool vertical) => vertical
            ? new (string, string, Action)[]
            {
                ("DirV_Up", "上", () => lab.SetVertical(RecoilVertical.Up)),
                ("DirV_Down", "下", () => lab.SetVertical(RecoilVertical.Down)),
                ("DirV_None", "なし", () => lab.SetVertical(RecoilVertical.None)),
            }
            : new (string, string, Action)[]
            {
                ("DirH_Left", "左", () => lab.SetHorizontal(RecoilHorizontal.Left)),
                ("DirH_Right", "右", () => lab.SetHorizontal(RecoilHorizontal.Right)),
                ("DirH_None", "なし", () => lab.SetHorizontal(RecoilHorizontal.None)),
            };

        (string, string, Action)[] Strengths() => new (string, string, Action)[]
        {
            ("Str_Weak", "弱", () => lab.SetStrength(RecoilStrength.Weak)),
            ("Str_Medium", "中", () => lab.SetStrength(RecoilStrength.Medium)),
            ("Str_Strong", "強", () => lab.SetStrength(RecoilStrength.Strong)),
        };

        void ToggleAlternating()
        {
            var r = lab.Edited(lab.SelectedId).recoil;
            lab.SetAlternating(!(r.enabled && r.horizontal == RecoilHorizontal.Alternating));
        }

        static readonly (RecoilParam p, RecoilRow row, string inline, string touch)[] Scalars =
        {
            (RecoilParam.Ads, RecoilRow.Ads, "ADS\n倍率", "ADS\n倍率"),
            (RecoilParam.RecoveryAmount, RecoilRow.RecoveryAmount, "回復量", "回復\n量"),
            (RecoilParam.RecoverySpeed, RecoilRow.RecoverySpeed, "回復\n速度", "回復\n速度"),
        };

        void BuildInline()
        {
            float h = rowHeight;
            float RowY(int i) => -(InHeader + InGap + i * (h + InGap));
            if (style.WideFineToggle)
                Label("HeaderB", "B：", TouchUi.Span.L(4f, InSegX - 8f), 0f, InHeader, font - 2);
            else
                Label("HeaderB", "B：編集（変更可）", TouchUi.Span.L(4f, InDirSliderX - 8f), 0f, InHeader, font - 2);
            Button("FineToggle", "", TouchUi.Span.S(style.WideFineToggle ? InSegX : InDirSliderX, InSliderRight), 0f, InHeader, () => lab.SetFineAdjust(!lab.FineAdjust));
            Label("HeaderBValue", "B", TouchUi.Span.R(InFieldRight, InFieldW), 0f, InHeader, font - 2, TextAnchor.MiddleCenter);
            Label("HeaderA", "A：適用中", TouchUi.Span.R(4f, InAW), 0f, InHeader, font - 2, TextAnchor.MiddleCenter);
            foreach (RecoilRow row in Enum.GetValues(typeof(RecoilRow)))
            {
                RowBack(row, RowY((int)row), h);
                AColumn(row, TouchUi.Span.R(4f, InAW), RowY((int)row), h, font - 1);
            }
            foreach (var (row, p, label, vertical) in new[] { (RecoilRow.Vertical, RecoilParam.Pitch, "上下", true), (RecoilRow.Horizontal, RecoilParam.Yaw, "左右", false) })
            {
                float y = RowY((int)row);
                RowLabel("L_" + (vertical ? "V" : "H"), label, InSegX - 6f, y, h, font);
                Segment(y, h, InSegX, InSegPitch, InSegW, Directions(lab, vertical));
                Value(p, TouchUi.Span.S(InDirSliderX, InSliderRight), y, TouchUi.Span.R(InFieldRight, InFieldW), y, h);
            }
            float sy = RowY((int)RecoilRow.Strength);
            RowLabel("L_S", "強度", InSegX - 6f, sy, h, font);
            Segment(sy, h, InSegX, InSegPitch, InSegW, Strengths());
            Button("AltToggle", "", TouchUi.Span.L(InDirSliderX, 200f), sy, h, ToggleAlternating);
            foreach (var (p, row, label, _) in Scalars)
            {
                float y = RowY((int)row);
                RowLabel("L_" + p, label, InScalarSliderX - 6f, y, h, style.OneLineLabels ? font : font - 2);
                Value(p, TouchUi.Span.S(InScalarSliderX, InSliderRight), y, TouchUi.Span.R(InFieldRight, InFieldW), y, h);
            }
        }

        void BuildTouch()
        {
            float c = rowHeight; // one cell row
            float CellY(int slot) => -slot * c;
            int small = font - 4; // labels and A: about 13 % smaller than before
            foreach (var (row, p, label, vertical, slot) in new[]
            {
                (RecoilRow.Vertical, RecoilParam.Pitch, "上下", true, 0),
                (RecoilRow.Horizontal, RecoilParam.Yaw, "左右", false, 2),
            })
            {
                float y1 = CellY(slot), y2 = CellY(slot + 1);
                RowBack(row, y1, 2f * c);
                Segment(y1, c, 0f, c, c, Directions(lab, vertical));
                Label("L_" + (vertical ? "V" : "H"), label, TouchUi.Span.L(2f, 54f), y2, c, small);
                AColumn(row, TouchUi.Span.R(0f, TA), y2, c, small);
                Value(p, TouchUi.Span.S(56f, TA + 4f), y2, TouchUi.Span.L(3f * c + 8f, TFieldW), y1, c);
            }
            float sy = CellY(4);
            RowBack(RecoilRow.Strength, sy, c);
            Segment(sy, c, 0f, c, c, Strengths());
            Button("AltToggle", "", TouchUi.Span.L(3f * c + 4f, 108f), sy, c, ToggleAlternating, font - 6);
            AColumn(RecoilRow.Strength, TouchUi.Span.R(0f, 64f), sy, c, small);
            int slotOf = 5;
            foreach (var (p, row, _, label) in Scalars)
            {
                float y = CellY(slotOf++);
                RowBack(row, y, c);
                Label("L_" + p, label, TouchUi.Span.L(2f, 60f), y, c, small);
                AColumn(row, TouchUi.Span.R(0f, TA), y, c, small);
                Value(p, TouchUi.Span.S(64f, TA + 4f + TFieldW + 4f), y, TouchUi.Span.R(TA + 4f, TFieldW), y, c);
            }
        }

        /// <summary>Slider in <paramref name="sliderSpan"/> on the row at <paramref name="sliderY"/>, number field (with its unit) in <paramref name="fieldSpan"/> at <paramref name="fieldY"/>.</summary>
        void Value(RecoilParam p, TouchUi.Span sliderSpan, float sliderY, TouchUi.Span fieldSpan, float fieldY, float h)
        {
            var s = UiBuild.Slider<TouchSlider>(Root, prefix + "Slider_" + p, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, 0f, 1f, v =>
            {
                if (refreshing)
                    return;
                bool fine = lab.FineAdjust;
                lab.TrySetValue(p, RecoilParams.ValueAt(p, fine, windowLo.TryGetValue(p, out var lo) ? lo : RecoilParams.Min(p), Mathf.RoundToInt(v)));
            });
            var sr = TouchUi.Put((RectTransform)s.transform, sliderSpan, sliderY, h);
            if (Touch)
            {
                // The whole cell takes the finger; the track is drawn thin, the handle narrower than the cell.
                UiBuild.Image(sr, new Color(0f, 0f, 0f, 0f), true);
                float inset = h * 0.36f;
                var bg = (RectTransform)sr.Find("Background");
                bg.offsetMin = new Vector2(0f, inset);
                bg.offsetMax = new Vector2(0f, -inset);
                var fillArea = (RectTransform)sr.Find("Fill Area");
                fillArea.offsetMin = new Vector2(6f, inset + 6f);
                fillArea.offsetMax = new Vector2(-6f, -inset - 6f);
                var handleArea = (RectTransform)sr.Find("Handle Slide Area");
                handleArea.offsetMin = new Vector2(22f, TouchInsetY + 4f);
                handleArea.offsetMax = new Vector2(-22f, -TouchInsetY - 4f);
                ((RectTransform)handleArea.Find("Handle")).sizeDelta = new Vector2(44f, 0f);
            }
            else if (style.ThinSliders)
            {
                // Same touch area (the whole cell), a thin track and a slim handle.
                UiBuild.Image(sr, new Color(0f, 0f, 0f, 0f), true);
                float track = (h - ThinTrack) * 0.5f;
                var bg = (RectTransform)sr.Find("Background");
                bg.offsetMin = new Vector2(0f, track);
                bg.offsetMax = new Vector2(0f, -track);
                bg.GetComponent<Image>().raycastTarget = false;
                var fillArea = (RectTransform)sr.Find("Fill Area");
                fillArea.offsetMin = new Vector2(0f, track);
                fillArea.offsetMax = new Vector2(0f, -track);
                var handleArea = (RectTransform)sr.Find("Handle Slide Area");
                handleArea.offsetMin = new Vector2(ThinHandle * 0.5f, h * 0.2f);
                handleArea.offsetMax = new Vector2(-ThinHandle * 0.5f, -h * 0.2f);
                ((RectTransform)handleArea.Find("Handle")).sizeDelta = new Vector2(ThinHandle, 0f);
            }
            sliders[p] = s;

            float insetY = Touch ? TouchInsetY : 0f;
            var field = UiBuild.InputField<TouchInputField>(Root, prefix + "Input_" + p, "", new Vector2(0f, 1f), Vector2.zero, Vector2.zero,
                font, UnityEngine.UI.InputField.ContentType.DecimalNumber);
            var fr = TouchUi.Put((RectTransform)field.transform, fieldSpan, fieldY - insetY, h - 2f * insetY);
            if (Touch)
                TouchUi.AddTouchArea(fr, 0f, insetY);
            field.shouldHideMobileInput = false; // iOS shows the typed text above the keyboard, wherever the field is
            // The unit inside the field, right of the number.
            float unitW = p == RecoilParam.RecoverySpeed ? (Touch ? 54f : 40f) : (Touch ? 28f : 22f);
            foreach (var child in new[] { "Text", "Placeholder" })
            {
                var r = (RectTransform)field.transform.Find(child);
                r.offsetMin = new Vector2(4f, 0f);
                r.offsetMax = new Vector2(-unitW - 4f, 0f);
            }
            field.textComponent.alignment = TextAnchor.MiddleRight;
            var unit = UiBuild.Text(TouchUi.Put(UiBuild.Rect("Unit", field.transform), TouchUi.Span.R(2f, unitW), 0f, h - 2f * insetY), RecoilParams.Unit(p),
                Touch ? font - 6 : font - 4, TextAnchor.MiddleLeft);
            ((RectTransform)unit.transform).anchorMin = new Vector2(1f, 0f); // full height of the field
            ((RectTransform)unit.transform).offsetMin = new Vector2(-2f - unitW, 0f);
            ((RectTransform)unit.transform).offsetMax = new Vector2(-2f, 0f);
            field.onEndEdit.AddListener(text => Typed(p, text));
            inputs[p] = field;
        }

        /// <summary>A number typed into a field, in the shown unit (also what a test or the device check sends).</summary>
        public void Typed(RecoilParam p, string text)
        {
            if (refreshing)
                return;
            var r = lab.Edited(lab.SelectedId).recoil;
            if (!RecoilParams.TryParse(p, text, out float v))
            {
                lab.RefuseInput($"{RecoilParams.Label(p)}：数値を入力してください");
                return;
            }
            if (Mathf.Abs(v - RecoilParams.Get(r, p)) < 1e-5f && r.enabled)
            {
                Refresh();
                return;
            }
            lab.TrySetValue(p, v);
        }

        /// <summary>Disabled while its direction is "なし" (an enabled profile; the angle is kept).</summary>
        static bool Usable(RecoilProfile r, RecoilParam p) =>
            !r.enabled || p == RecoilParam.Pitch && r.vertical != RecoilVertical.None || p == RecoilParam.Yaw && r.horizontal != RecoilHorizontal.None
            || p != RecoilParam.Pitch && p != RecoilParam.Yaw;

        public void Refresh()
        {
            if (Root == null)
                return;
            refreshing = true;
            var b = lab.Edited(lab.SelectedId).recoil;
            var a = lab.Applied(lab.SelectedId).recoil;
            float legacy = lab.LegacyRecoverySpeed;
            bool en = b.enabled;
            void Pick(string name, bool on)
            {
                if (buttons.TryGetValue(prefix + name, out var btn))
                    ((Image)btn.targetGraphic).color = on && en ? Chosen : ButtonColor;
            }
            bool alt = b.horizontal == RecoilHorizontal.Alternating;
            Pick("DirV_Up", b.vertical == RecoilVertical.Up);
            Pick("DirV_Down", b.vertical == RecoilVertical.Down);
            Pick("DirV_None", b.vertical == RecoilVertical.None);
            Pick("DirH_Left", b.horizontal == RecoilHorizontal.Left);
            Pick("DirH_Right", b.horizontal == RecoilHorizontal.Right);
            Pick("DirH_None", b.horizontal == RecoilHorizontal.None);
            Pick("AltToggle", alt);
            Pick("Str_Weak", b.strength == RecoilStrength.Weak);
            Pick("Str_Medium", b.strength == RecoilStrength.Medium);
            Pick("Str_Strong", b.strength == RecoilStrength.Strong);
            buttons[prefix + "AltToggle"].GetComponentInChildren<Text>().text = Touch
                ? (en && alt ? "左右交互\nON" : "左右交互\nOFF")
                : (en && alt ? "左右交互：ON" : "左右交互：OFF");
            bool fine = lab.FineAdjust;
            if (buttons.TryGetValue(prefix + "FineToggle", out var fineButton)) // touch: the panel's own switch
            {
                fineButton.GetComponentInChildren<Text>().text = Touch
                    ? (fine ? "スライダー\n微調整" : "スライダー\n粗調整")
                    : (fine ? "スライダー：微調整（押すと粗）" : "スライダー：粗調整（押すと微）");
                ((Image)fineButton.targetGraphic).color = fine ? Chosen : ButtonColor;
            }
            if (fine != windowFine || windowWeapon != lab.SelectedId)
                windowLo.Clear(); // a new window around the value
            windowFine = fine;
            windowWeapon = lab.SelectedId;

            foreach (var pair in sliders)
            {
                var p = pair.Key;
                float v = RecoilParams.Get(b, p);
                var (lo, steps) = RecoilParams.Window(p, fine, v, windowLo.TryGetValue(p, out var keep) ? keep : (float?)null);
                windowLo[p] = lo;
                var s = pair.Value;
                s.minValue = 0f;
                s.maxValue = steps;
                s.SetValueWithoutNotify(Mathf.Clamp(RecoilParams.IndexOf(p, fine, lo, v), 0, steps));
                bool usable = Usable(b, p);
                s.interactable = usable;
                var field = inputs[p];
                field.interactable = usable;
                if (!field.isFocused)
                    field.SetTextWithoutNotify(RecoilParams.Text(p, v));
            }
            foreach (var pair in aTexts)
            {
                string at = RecoilParams.RowText(a, pair.Key, legacy), bt = RecoilParams.RowText(b, pair.Key, legacy);
                bool differs = at != bt;
                pair.Value.text = Touch ? "A " + at : at;
                pair.Value.color = differs ? EditedColor : Color.white;
                rowBacks[pair.Key].color = differs ? RowDiffColor : RowColor;
            }
            foreach (var pair in inputs)
            {
                bool differs = pair.Key switch
                {
                    RecoilParam.Pitch => RowDiff(a, b, RecoilRow.Vertical, legacy),
                    RecoilParam.Yaw => RowDiff(a, b, RecoilRow.Horizontal, legacy),
                    RecoilParam.Ads => RowDiff(a, b, RecoilRow.Ads, legacy),
                    RecoilParam.RecoveryAmount => RowDiff(a, b, RecoilRow.RecoveryAmount, legacy),
                    _ => RowDiff(a, b, RecoilRow.RecoverySpeed, legacy),
                };
                var color = !pair.Value.interactable ? Dim : differs ? EditedColor : Color.white;
                pair.Value.textComponent.color = color;
                pair.Value.transform.Find("Unit").GetComponent<Text>().color = color;
            }
            refreshing = false;
        }

        static bool RowDiff(RecoilProfile a, RecoilProfile b, RecoilRow row, float legacy) =>
            RecoilParams.RowText(a, row, legacy) != RecoilParams.RowText(b, row, legacy);
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Weapon drop-down of the lab and the in-game panel: the list is made from the lab's registered weapon
    /// definitions each time it opens (a new gun in the scene's list shows up without UI changes). The header shows the
    /// selected name cut with "…" to its width; the list shows full names. Picking one shows that weapon's A / B.
    /// </summary>
    public sealed class WeaponPicker
    {
        static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        static readonly Color Chosen = new Color(0.55f, 0.38f, 0.1f, 1f);

        readonly WeaponBalanceLab lab;
        readonly RectTransform parent;
        readonly string prefix;
        readonly Dictionary<string, Button> registry;
        readonly int font;
        float insetY;
        RectTransform list;
        readonly List<string> listed = new List<string>();

        public Button Header { get; private set; }
        public bool IsListOpen => list != null && list.gameObject.activeSelf;
        /// <summary>Weapon ids in the list as last opened.</summary>
        public IReadOnlyList<string> Listed => listed;
        /// <summary>The list (open or not).</summary>
        public RectTransform List => list;
        /// <summary>Width of the open list (0: the header's).</summary>
        public float ListWidth { get; set; }
        /// <summary>Row pitch of the open list (0: the header's cell height).</summary>
        public float ItemHeight { get; set; }

        WeaponPicker(WeaponBalanceLab lab, RectTransform parent, string prefix, Dictionary<string, Button> registry, int font)
        {
            this.lab = lab;
            this.parent = parent;
            this.prefix = prefix;
            this.registry = registry;
            this.font = font;
        }

        /// <summary>
        /// Builds the drop-down in its cell on <paramref name="parent"/>; drawn <paramref name="touchInsetY"/> inside the
        /// cell top and bottom (the whole cell takes the touch).
        /// </summary>
        public static WeaponPicker Build(WeaponBalanceLab lab, RectTransform parent, string prefix, TouchUi.Span cell, float y, float h, int font,
            Dictionary<string, Button> registry, float listWidth = 0f, float touchInsetY = 0f)
        {
            var p = new WeaponPicker(lab, parent, prefix, registry ?? new Dictionary<string, Button>(), font) { ListWidth = listWidth, insetY = touchInsetY };
            p.Header = TouchUi.CellButton(parent, prefix + "WeaponPick", "", cell, y, h, p.Toggle, font, new Color(0.22f, 0.2f, 0.12f, 1f), 0f, touchInsetY);
            p.Header.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow; // one line, cut with "…"
            p.registry[prefix + "WeaponPick"] = p.Header;
            p.list = UiBuild.Rect(prefix + "WeaponList", parent);
            p.list.anchorMin = p.list.anchorMax = p.list.pivot = new Vector2(0f, 1f);
            UiBuild.Image(p.list, new Color(0.02f, 0.02f, 0.03f, 0.98f), true);
            p.list.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 0.75f, 0.3f, 0.8f);
            p.list.gameObject.SetActive(false);
            p.Refresh();
            return p;
        }

        /// <summary>Moves the header to another cell (the lab places it clear of the title and the FPS read-out).</summary>
        public void Place(TouchUi.Span cell, float y, float h)
        {
            TouchUi.Put((RectTransform)Header.transform, cell, y - insetY, h - 2f * insetY);
            Refresh();
        }

        public void Toggle()
        {
            if (IsListOpen)
                Close();
            else
                Open();
        }

        /// <summary>The header's cell in the parent (x from its left, y downwards from its top).</summary>
        Rect HeaderCell()
        {
            var c = new Vector3[4];
            ((RectTransform)Header.transform).GetWorldCorners(c);
            var r = parent.rect;
            Vector2 a = parent.InverseTransformPoint(c[1]), b = parent.InverseTransformPoint(c[3]);
            return Rect.MinMaxRect(a.x - r.xMin, r.yMax - a.y - insetY, b.x - r.xMin, r.yMax - b.y + insetY);
        }

        public void Open()
        {
            foreach (Transform child in list)
                UnityEngine.Object.Destroy(child.gameObject);
            listed.Clear();
            var cell = HeaderCell();
            float w = ListWidth > 0f ? ListWidth : cell.width, h = ItemHeight > 0f ? ItemHeight : cell.height;
            float y = -4f;
            foreach (var id in lab.WeaponIds)
            {
                string captured = id;
                var item = TouchUi.CellButton(list, prefix + "WeaponItem_" + id, lab.WeaponLabel(id), TouchUi.Span.S(4f, 4f), y, h, () =>
                {
                    Close();
                    lab.Select(captured);
                }, font, id == lab.SelectedId ? Chosen : ButtonColor, 0f, insetY);
                registry[prefix + "WeaponItem_" + id] = item;
                listed.Add(id);
                y -= h;
            }
            // Under the header, kept inside the parent's width.
            float x = Mathf.Clamp(cell.xMin, 4f, Mathf.Max(4f, parent.rect.width - w - 4f));
            list.anchoredPosition = new Vector2(x, -cell.yMax - 2f);
            list.sizeDelta = new Vector2(w, -y + 4f);
            list.gameObject.SetActive(true);
            list.SetAsLastSibling(); // over the rows below it
        }

        public void Close()
        {
            if (list != null)
                list.gameObject.SetActive(false);
            foreach (var id in listed)
                registry.Remove(prefix + "WeaponItem_" + id);
        }

        /// <summary>Full text of the header before cutting.</summary>
        public string FullHeaderText => $"武器：{lab.WeaponLabel(lab.SelectedId)} ▼";

        public void Refresh()
        {
            if (Header == null)
                return;
            var t = Header.GetComponentInChildren<Text>();
            float room = ((RectTransform)Header.transform).rect.width - 16f;
            string name = lab.WeaponLabel(lab.SelectedId);
            if (room <= 0f || TouchUi.TextWidth(t, FullHeaderText) <= room)
                t.text = FullHeaderText;
            else
                t.text = TouchUi.Ellipsize(t, name, room - TouchUi.TextWidth(t, " ▼")) + " ▼";
        }
    }
}
