using System;
using System.Collections.Generic;
using Deadlock.Game;
using Deadlock.UI;
using Deadlock.UI.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Home
{
    /// <summary>Availability of a menu destination, shown on its row.</summary>
    public enum HomeEntryStatus
    {
        /// <summary>A prototype / test scene that exists (labelled "テスト").</summary>
        Test,
        /// <summary>Not made yet: the row says so and starts nothing.</summary>
        ComingSoon,
    }

    /// <summary>One row of a home sub-screen (Survival / Training / Event / Gear tab).</summary>
    public struct HomeEntry
    {
        public string name;
        public string description;
        public HomeEntryStatus status;
        public Action open; // null for ComingSoon

        public HomeEntry(string name, string description, HomeEntryStatus status, Action open = null)
        {
            this.name = name;
            this.description = description;
            this.status = status;
            this.open = open;
        }
    }

    /// <summary>
    /// Home screen menu after the approved design (docs/review/home-background/home_menu_design_approved.png, D-049):
    /// logo top left; Survival / Training / Event stacked on the left with equal gaps and Gear further down (all four the
    /// same size: icon, label and "›" as separate parts on a dark metal panel with a brass frame and corner rivets);
    /// points and settings top right; Crate bottom right. Every element is a freely placed item of the full-screen
    /// "home" layout (<see cref="UiLayoutRoot"/>, D-047): the defaults follow the design relative to the height-matched
    /// background, kept inside the safe area. Sub-screens list their destinations with a status: existing prototype
    /// scenes are labelled as tests, the rest "準備中" and start nothing; a tap outside a sub-screen's window closes it
    /// (<see cref="HomePanelBackdrop"/>; no back button). A fixed layout-edit button inside the safe area
    /// stays as the way back from any layout.
    /// </summary>
    public sealed class HomeMenu : MonoBehaviour
    {
        public const string LayoutId = "home";
        /// <summary>2: the approved-design menu (v1 was the first text-only menu; a saved v1 layout is kept, not used).</summary>
        public const int LayoutVersion = 2;

        public const string Logo = "logo";
        public const string Survival = "survival";
        public const string Training = "training";
        public const string Event = "event";
        public const string Gear = "gear";
        public const string Points = "points";
        public const string Settings = "settings";
        public const string Crate = "crate";

        public static readonly string[] MainButtons = { Survival, Training, Event, Gear };

        public static readonly Color PanelColor = new Color(0.055f, 0.05f, 0.045f, 0.84f);
        public static readonly Color Brass = new Color(0.78f, 0.6f, 0.3f, 1f);
        public static readonly Color Amber = new Color(1f, 0.74f, 0.36f, 1f);
        public static readonly Color Cream = new Color(0.94f, 0.88f, 0.74f, 1f);

        /// <summary>Main button size in the design (reference px of the 1672x941 screen).</summary>
        public static readonly Vector2 MainButtonSize = new Vector2(490f, 136f);
        public const float MainButtonGap = 18f;
        public const float GearGap = 59f;

        public static readonly UiLayoutSpec[] Specs =
        {
            new UiLayoutSpec(Logo, new Vector2(342f, 114f), 0.5f, 1.6f),
            new UiLayoutSpec(Survival, MainButtonSize, 0.5f, 1.4f),
            new UiLayoutSpec(Training, MainButtonSize, 0.5f, 1.4f),
            new UiLayoutSpec(Event, MainButtonSize, 0.5f, 1.4f),
            new UiLayoutSpec(Gear, MainButtonSize, 0.5f, 1.4f),
            new UiLayoutSpec(Points, new Vector2(210f, 70f), 0.5f, 1.6f),
            new UiLayoutSpec(Settings, new Vector2(74f, 70f), 0.5f, 1.8f),
            new UiLayoutSpec(Crate, new Vector2(186f, 182f), 0.5f, 1.6f),
        };

        /// <summary>Design rectangles (x, y from the top, w, h) on the 1672x941 reference.</summary>
        public static readonly Dictionary<string, Rect> DesignRects = new Dictionary<string, Rect>
        {
            { Logo, new Rect(64f, 76f, 342f, 114f) },
            { Survival, new Rect(62f, 228f, 490f, 136f) },
            { Training, new Rect(62f, 228f + 136f + MainButtonGap, 490f, 136f) },
            { Event, new Rect(62f, 228f + 2f * (136f + MainButtonGap), 490f, 136f) },
            { Gear, new Rect(62f, 228f + 2f * (136f + MainButtonGap) + 136f + GearGap, 490f, 136f) },
            { Points, new Rect(1318f, 71f, 210f, 70f) },
            { Settings, new Rect(1546f, 71f, 74f, 70f) },
            { Crate, new Rect(1430f, 702f, 186f, 182f) },
        };

        public static string DisplayName(string id) => id switch
        {
            Logo => "ロゴ",
            Survival => "Survival",
            Training => "Training",
            Event => "Event",
            Gear => "Gear",
            Points => "ポイント表示",
            Settings => "設定",
            Crate => "Crate",
            _ => id,
        };

        [Tooltip("Existing UI art: used only where the home art below is unset (logo, settings, crosshair for Training).")]
        public ControlIconSet icons;
        [Tooltip("Approved home pixel art (Assets/Game/Art/Home/UI, HOME_MENU_UI_ASSET_SPEC §2); an unset slot stays empty.")]
        public Sprite survivalIcon, trainingIcon, eventIcon, gearIcon, settingsIcon, crateIcon;
        [Tooltip("Approved \"›\" of the main buttons (the text \"›\" is shown while unset).")]
        public Sprite arrowSprite;
        [Tooltip("Approved home title logo (§3); the existing logo of the icon set is shown while unset.")]
        public Sprite homeLogo;
        [Tooltip("Approved panel images (frame with rivets / centre fill, 9-slice, derived from home_menu_panel.png; Assets/Game/Art/Home/UI/Frames). Unset: the earlier Unity-drawn panels, with an error logged.")]
        public Sprite panelFrame, panelFill;

        RectTransform canvasRect;
        RectTransform panelLayer;
        readonly Dictionary<string, GameObject> panels = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> gearPages = new Dictionary<string, GameObject>();
        readonly Dictionary<string, Button> gearTabs = new Dictionary<string, Button>();
        Text messageText;

        public UiLayoutRoot Layout { get; private set; }
        public UiLayoutEditor Editor { get; private set; }
        public Button LayoutEditButton { get; private set; }
        public string OpenPanelName { get; private set; }
        public string GearTab { get; private set; }
        /// <summary>Last "準備中" / status message shown in a sub-screen.</summary>
        public string Message => messageText != null ? messageText.text : "";

        public static readonly string[] GearTabs = { "Loadout", "Skin", "Skill" };

        /// <summary>Survival: match types (D-004 / D-005); only the LAN test exists.</summary>
        public static HomeEntry[] SurvivalEntries() => new[]
        {
            new HomeEntry("Deadline（デッドライン）", "時間制の生存戦。順位で報酬。", HomeEntryStatus.ComingSoon),
            new HomeEntry("Last Stand（ラストスタンド）", "最後の1人を目指すイベント戦。", HomeEntryStatus.ComingSoon),
            new HomeEntry("オンライン検証（LAN）", "同じWi-Fiの2人で試す検証用の対戦（試作）。", HomeEntryStatus.Test, () => HomeScreen.Load(GameScenes.Online)),
        };

        public static HomeEntry[] TrainingEntries() => new[]
        {
            new HomeEntry("射撃場", "的と移動・射撃の練習（試作）。", HomeEntryStatus.Test, () => HomeScreen.Load(GameScenes.FiringRange)),
            new HomeEntry("トレーニング", "ゾンビと戦う練習（試作）。", HomeEntryStatus.Test, () => HomeScreen.Load(GameScenes.Training)),
        };

        public static HomeEntry[] EventEntries() => new[]
        {
            new HomeEntry("Ruins（廃墟）", "石造りの2階建ての遺跡を探索する（遺跡テストフィールド、試作）。", HomeEntryStatus.Test, () => HomeScreen.Load(GameScenes.Ruins)),
            new HomeEntry("Assault（急襲）", "準備中のイベントです。", HomeEntryStatus.ComingSoon),
        };

        /// <summary>
        /// Initial layout: the design rectangles placed on the background as it is framed on this canvas (Fit,
        /// height-matched on iPhone), scaled with it, then moved inside the safe area if needed.
        /// </summary>
        public static UiLayoutData Defaults(Vector2 canvas, Rect safe)
        {
            float s = Mathf.Min(canvas.x / HomeFraming.RefWidth, canvas.y / HomeFraming.RefHeight);
            float x0 = (canvas.x - HomeFraming.RefWidth * s) / 2f, top = (canvas.y - HomeFraming.RefHeight * s) / 2f;
            var data = new UiLayoutData { layoutVersion = LayoutVersion };
            foreach (var spec in Specs)
            {
                Rect d = DesignRects[spec.id];
                float scale = UiLayoutMath.ClampScale(s, spec);
                Vector2 size = spec.baseSize * scale;
                var c = new Vector2(x0 + (d.x + d.width / 2f) * s, canvas.y - top - (d.y + d.height / 2f) * s);
                c.x = Mathf.Clamp(c.x, safe.xMin + size.x / 2f, Mathf.Max(safe.xMin + size.x / 2f, safe.xMax - size.x / 2f));
                c.y = Mathf.Clamp(c.y, safe.yMin + size.y / 2f, Mathf.Max(safe.yMin + size.y / 2f, safe.yMax - size.y / 2f));
                data.items.Add(new UiLayoutItem { id = spec.id, center = UiLayoutMath.ToNormalized(c, canvas), scale = scale, visible = true });
            }
            return data;
        }

        static UiLayoutSpec SpecOf(string id) => Array.Find(Specs, s => s.id == id);

        void Awake()
        {
            Build();
            if (panelFrame != null && panelFill != null)
                SetPanelSkin(panelFrame, panelFill);
            else
                Debug.LogError($"[DEADLOCK][home] panel images missing (panelFrame={(panelFrame != null ? panelFrame.name : "null")}, panelFill={(panelFill != null ? panelFill.name : "null")}): showing the Unity-drawn panels instead. Rebuild the home scene (HomeSceneBuilder).");
        }

        void OnEnable() => HomePanelLook.Changed += RefreshPanels;

        void OnDisable() => HomePanelLook.Changed -= RefreshPanels;

        /// <summary>Every home panel (buttons, windows, rows), shown or not.</summary>
        public HomePanelPart[] PanelParts => GetComponentsInChildren<HomePanelPart>(true);

        /// <summary>Shows the 9-slice panel images on every panel (both set), or the Unity-drawn panels (either null).</summary>
        public void SetPanelSkin(Sprite frame, Sprite fill)
        {
            foreach (var part in PanelParts)
                part.SetSkin(frame, fill);
        }

        void RefreshPanels()
        {
            foreach (var part in PanelParts)
                part.Refresh();
        }

        void Start() => Layout.Load();

        void Build()
        {
            canvasRect = (RectTransform)transform;
            var layoutRect = UiBuild.Stretch(UiBuild.Rect("MenuLayout", canvasRect)); // whole screen, not the background
            Layout = layoutRect.gameObject.AddComponent<UiLayoutRoot>();
            Layout.layoutId = LayoutId;
            Layout.layoutVersion = LayoutVersion;
            Layout.SetDefaults(Defaults);

            Layout.Register(LogoElement(), SpecOf(Logo));
            Layout.Register(MainButton(Survival, survivalIcon, () => OpenPanel("SurvivalPanel")), SpecOf(Survival));
            Layout.Register(MainButton(Training, trainingIcon != null ? trainingIcon : icons != null ? icons.ads : null, () => OpenPanel("TrainingPanel")), SpecOf(Training));
            Layout.Register(MainButton(Event, eventIcon, () => OpenPanel("EventPanel")), SpecOf(Event));
            Layout.Register(MainButton(Gear, gearIcon, () => OpenGear("Loadout")), SpecOf(Gear));
            Layout.Register(PointsElement(), SpecOf(Points));
            Layout.Register(SettingsElement(), SpecOf(Settings));
            Layout.Register(CrateElement(), SpecOf(Crate));

            // Fixed (not movable) entry to the layout editor inside the safe area: the way back if an element was put
            // where it cannot be pressed.
            var fixedSafe = UiBuild.Stretch(UiBuild.Rect("FixedSafeArea", canvasRect));
            fixedSafe.gameObject.AddComponent<HomeSafeAreaPanel>();
            LayoutEditButton = UiBuild.Button(fixedSafe, "LayoutEditButton", "レイアウト編集", new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(170f, 38f), OpenLayoutEditor, 16);
            StyleSmall(LayoutEditButton, 0.55f);

            panelLayer = UiBuild.Stretch(UiBuild.Rect("Panels", canvasRect));
            BuildPanels();

            Editor = gameObject.AddComponent<UiLayoutEditor>();
            Editor.DisplayName = DisplayName;
            Editor.Init(Layout, canvasRect);
        }

        // ------------------------------------------------------------------------------------------- elements

        /// <summary>
        /// Dark metal panel with a brass frame and four corner rivets, built in Unity UI; shows the panel images instead
        /// once they are set (<see cref="SetPanelSkin"/>, <see cref="HomePanelPart"/>).
        /// </summary>
        static Image MetalPanel(RectTransform rect, bool raycast, HomePanelTarget target)
        {
            var image = UiBuild.Image(rect, PanelColor, raycast);
            rect.gameObject.AddComponent<HomePanelPart>().target = target;
            // Brass frame: four thin lines just inside the edge (follow any size).
            var frame = UiBuild.Stretch(UiBuild.Rect("Frame", rect));
            frame.offsetMin = new Vector2(4f, 4f);
            frame.offsetMax = new Vector2(-4f, -4f);
            void Edge(string name, Vector2 min, Vector2 max, Vector2 size)
            {
                var e = UiBuild.Rect(name, frame);
                e.anchorMin = min;
                e.anchorMax = max;
                e.pivot = new Vector2(0.5f, 0.5f);
                e.sizeDelta = size;
                e.anchoredPosition = Vector2.zero;
                UiBuild.Image(e, Brass, false);
            }
            Edge("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 3f));
            Edge("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 3f));
            Edge("Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 0f));
            Edge("Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(3f, 0f));
            var outer = rect.gameObject.AddComponent<Outline>();
            outer.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outer.effectDistance = new Vector2(2f, -2f);
            foreach (var corner in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) })
            {
                var rivet = UiBuild.Rect("Rivet", rect);
                rivet.anchorMin = rivet.anchorMax = rivet.pivot = corner;
                rivet.anchoredPosition = new Vector2(corner.x > 0f ? -5f : 5f, corner.y > 0f ? -5f : 5f);
                rivet.sizeDelta = new Vector2(5f, 5f);
                UiBuild.Image(rivet, Brass, false);
            }
            return image;
        }

        static Button MakeButton(RectTransform rect, Image target, Action onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            // normal: calm dark metal; pressed: a little lighter; selected: a light amber cast; disabled: darker / greyer.
            var colors = button.colors;
            colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(1f, 1f, 1f, 1f);
            colors.selectedColor = new Color(1f, 0.86f, 0.62f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
            colors.colorMultiplier = 1.15f;
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            return button;
        }

        RectTransform MainButton(string id, Sprite icon, Action onClick)
        {
            var rect = UiBuild.Rect(id, transform);
            var panel = MetalPanel(rect, true, HomePanelTarget.MainButton);
            MakeButton(rect, panel, onClick);
            // Icon box, label and arrow are separate parts placed in proportion to the button (they follow a resize).
            var iconBox = UiBuild.Rect("Icon", rect);
            Box(iconBox, MainButtonSize, 26f, 12f, 120f, 112f); // §2.3: 120x112, 26 from the left, centred
            var iconImage = iconBox.gameObject.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.color = IsHomeArt(icon) ? Color.white : Cream; // pixel art as painted; the old white icons in cream
            iconImage.enabled = icon != null; // missing art: empty slot (listed as missing, no stand-in drawing)
            var label = UiBuild.Rect("Label", rect);
            label.anchorMin = new Vector2(0.33f, 0f);
            label.anchorMax = new Vector2(0.86f, 1f);
            label.offsetMin = label.offsetMax = Vector2.zero;
            FitText(UiBuild.Text(label, DisplayName(id), 46, TextAnchor.MiddleLeft, Cream), 46, FontStyle.Bold);
            var arrow = UiBuild.Rect("Arrow", rect);
            if (arrowSprite != null)
            {
                Box(arrow, MainButtonSize, 490f - 30f - 24f, 50f, 24f, 36f); // §2.3: 24x36, 30 from the right, centred
                var arrowImage = arrow.gameObject.AddComponent<Image>();
                arrowImage.sprite = arrowSprite;
                arrowImage.preserveAspect = true;
                arrowImage.raycastTarget = false;
            }
            else
            {
                arrow.anchorMin = new Vector2(0.88f, 0f);
                arrow.anchorMax = new Vector2(0.97f, 1f);
                arrow.offsetMin = arrow.offsetMax = Vector2.zero;
                FitText(UiBuild.Text(arrow, "›", 56, TextAnchor.MiddleCenter, Cream), 56, FontStyle.Bold);
            }
            return rect;
        }

        bool IsHomeArt(Sprite sprite) => sprite != null && (sprite == survivalIcon || sprite == trainingIcon || sprite == eventIcon || sprite == gearIcon || sprite == settingsIcon || sprite == crateIcon);

        /// <summary>Anchors a part to a box given in design px inside its parent's design size (x, y from the top left).</summary>
        static void Box(RectTransform r, Vector2 parent, float x, float y, float w, float h)
        {
            r.anchorMin = new Vector2(x / parent.x, 1f - (y + h) / parent.y);
            r.anchorMax = new Vector2((x + w) / parent.x, 1f - y / parent.y);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void FitText(Text text, int max, FontStyle style)
        {
            text.fontStyle = style;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 8;
            text.resizeTextMaxSize = max;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        RectTransform LogoElement()
        {
            var rect = UiBuild.Rect(Logo, transform);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            if (homeLogo != null)
                image.sprite = homeLogo;
            else if (icons != null && icons.titleLogo != null)
                image.sprite = icons.titleLogo;
            else
            {
                // Interim lettering only when the logo art is missing (not the official logo).
                image.color = new Color(0f, 0f, 0f, 0f);
                FitText(UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("InterimLogoText", rect)), "DEADLOCK\nSince 1988", 40, TextAnchor.MiddleLeft, Cream), 40, FontStyle.Bold);
            }
            return rect;
        }

        RectTransform PointsElement()
        {
            var rect = UiBuild.Rect(Points, transform);
            var panel = MetalPanel(rect, true, HomePanelTarget.Points);
            MakeButton(rect, panel, () => OpenPanel("PointsPanel"));
            var value = UiBuild.Rect("Value", rect);
            value.anchorMin = new Vector2(0.08f, 0.12f);
            value.anchorMax = new Vector2(0.92f, 0.88f);
            value.offsetMin = value.offsetMax = Vector2.zero;
            // No point balance exists yet: no number is shown (the design's "100 PT" is an example).
            FitText(UiBuild.Text(value, "－ PT", 34, TextAnchor.MiddleRight, Cream), 34, FontStyle.Bold);
            return rect;
        }

        RectTransform SettingsElement()
        {
            var rect = UiBuild.Rect(Settings, transform);
            var panel = MetalPanel(rect, true, HomePanelTarget.Settings);
            MakeButton(rect, panel, () => OpenPanel("SettingsPanel"));
            var icon = UiBuild.Rect("Icon", rect);
            var image = icon.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (settingsIcon != null)
            {
                Box(icon, new Vector2(74f, 70f), 13f, 11f, 48f, 48f); // §2.3: 48x48 in the middle of 74x70
                image.sprite = settingsIcon;
            }
            else if (icons != null && icons.settings != null)
            {
                icon.anchorMin = new Vector2(0.18f, 0.18f);
                icon.anchorMax = new Vector2(0.82f, 0.82f);
                icon.offsetMin = icon.offsetMax = Vector2.zero;
                image.color = Cream;
                image.sprite = icons.settings;
            }
            else
                image.enabled = false;
            return rect;
        }

        RectTransform CrateElement()
        {
            var rect = UiBuild.Rect(Crate, transform);
            var panel = MetalPanel(rect, true, HomePanelTarget.Crate);
            MakeButton(rect, panel, () => OpenPanel("CratePanel"));
            var icon = UiBuild.Rect("Icon", rect);
            Box(icon, new Vector2(186f, 182f), 29f, 16f, 128f, 104f); // §2.3: 128x104, 16 from the top, centred
            var image = icon.gameObject.AddComponent<Image>();
            image.sprite = crateIcon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = crateIcon != null;
            var label = UiBuild.Rect("Label", rect);
            label.anchorMin = new Vector2(0.05f, 0.04f);
            label.anchorMax = new Vector2(0.95f, 0.34f);
            label.offsetMin = label.offsetMax = Vector2.zero;
            FitText(UiBuild.Text(label, "Crate", 36, TextAnchor.MiddleCenter, Cream), 36, FontStyle.Bold);
            return rect;
        }

        static void StyleSmall(Button button, float alpha)
        {
            ((Image)button.targetGraphic).color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, alpha);
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = Brass;
            button.GetComponentInChildren<Text>().color = Cream;
        }

        // ------------------------------------------------------------------------------------------- sub-screens

        RectTransform Window(string name, string title, Vector2 size, out GameObject panelObject)
        {
            var panel = UiBuild.Stretch(UiBuild.Rect(name, panelLayer));
            // Dimmed layer over the whole screen: a tap outside the window closes the sub-screen (no back button).
            var backdrop = UiBuild.Stretch(UiBuild.Rect("Backdrop", panel));
            UiBuild.Image(backdrop, new Color(0f, 0f, 0f, 0.6f), true);
            backdrop.gameObject.AddComponent<HomePanelBackdrop>().Tapped = ClosePanel;
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", panel));
            safe.gameObject.AddComponent<HomeSafeAreaPanel>();
            var window = UiBuild.Place(UiBuild.Rect("Window", safe), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            MetalPanel(window, true, HomePanelTarget.Window).color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.96f);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(size.x - 80f, 48f)),
                title, 34, TextAnchor.MiddleCenter, Cream).fontStyle = FontStyle.Bold;
            panel.gameObject.SetActive(false);
            panelObject = panel.gameObject;
            panels[name] = panelObject;
            return window;
        }

        void Rows(RectTransform parent, float top, HomeEntry[] entries, float width)
        {
            float y = top;
            foreach (var entry in entries)
            {
                var row = UiBuild.Place(UiBuild.Rect(entry.name, parent), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, 92f));
                var panel = MetalPanel(row, true, HomePanelTarget.Row);
                var captured = entry;
                var button = MakeButton(row, panel, () => OpenEntry(captured));
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("Name", row), new Vector2(0f, 1f), new Vector2(24f, -10f), new Vector2(width - 220f, 40f)),
                    entry.name, 26, TextAnchor.MiddleLeft, Cream).fontStyle = FontStyle.Bold;
                var desc = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Description", row), new Vector2(0f, 0f), new Vector2(24f, 10f), new Vector2(width - 220f, 34f)),
                    entry.description, 17, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.72f));
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                var status = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Status", row), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(170f, 40f)),
                    StatusLabel(entry.status), 20, TextAnchor.MiddleRight, entry.status == HomeEntryStatus.ComingSoon ? new Color(0.75f, 0.72f, 0.66f, 0.8f) : Amber);
                ((RectTransform)status.transform).pivot = new Vector2(1f, 0.5f);
                if (entry.status == HomeEntryStatus.ComingSoon)
                {
                    panel.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.6f);
                    row.GetComponent<HomePanelPart>().dimmed = true;
                }
                y -= 104f;
            }
        }

        public static string StatusLabel(HomeEntryStatus status) => status == HomeEntryStatus.ComingSoon ? "準備中" : "テスト ›";

        void OpenEntry(HomeEntry entry)
        {
            if (entry.status == HomeEntryStatus.ComingSoon || entry.open == null)
            {
                messageText.text = $"{entry.name} は準備中です";
                return;
            }
            entry.open();
        }

        void BuildPanels()
        {
            var w = Window("SurvivalPanel", "Survival", new Vector2(820f, 520f), out _);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Note", w), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(740f, 30f)),
                "対戦モードは開発中です。いま遊べるのは検証用の試作だけです。", 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            Rows(w, -112f, SurvivalEntries(), 740f);

            w = Window("TrainingPanel", "Training", new Vector2(820f, 410f), out _);
            Rows(w, -90f, TrainingEntries(), 740f);

            w = Window("EventPanel", "Event", new Vector2(820f, 410f), out _);
            Rows(w, -90f, EventEntries(), 740f);

            BuildGear();

            w = Window("CratePanel", "Crate", new Vector2(760f, 330f), out _);
            Body(w, "準備中です。\nCrate（ガチャ：スキン・武器・スキル）は未実装です。ポイントの消費や抽選は行いません。");

            w = Window("PointsPanel", "ポイント", new Vector2(760f, 300f), out _);
            Body(w, "ポイントの獲得・保存はまだありません（未実装）。\n表示は「－ PT」です。");

            w = Window("SettingsPanel", "設定", new Vector2(760f, 470f), out _);
            Body(w, "ゲーム中の操作設定・操作レイアウトは、射撃場などのゲーム中のメニュー → 設定 から変更できます。");
            Style(UiBuild.Button(w, "ホームのレイアウト編集", "ホームのレイアウト編集", new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(460f, 64f), OpenLayoutEditor, 24));
            Style(UiBuild.Button(w, "ホームの配置を初期配置に戻す", "ホームの配置を初期配置に戻す", new Vector2(0.5f, 1f), new Vector2(0f, -270f), new Vector2(460f, 64f), ResetLayoutAsk, 22));

            // Status line shared by the sub-screens ("… は準備中です", reset confirmation).
            var msgRect = UiBuild.Place(UiBuild.Rect("Message", panelLayer), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(900f, 44f));
            messageText = UiBuild.Text(msgRect, "", 22, TextAnchor.MiddleCenter, Amber);
        }

        static void Style(Button b)
        {
            ((Image)b.targetGraphic).color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.95f);
            b.gameObject.AddComponent<Outline>().effectColor = Brass;
            b.GetComponentInChildren<Text>().color = Cream;
        }

        static void Body(RectTransform window, string text)
        {
            var t = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Body", window), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(680f, 90f)),
                text, 20, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.82f));
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void BuildGear()
        {
            var w = Window("GearPanel", "Gear", new Vector2(900f, 560f), out _);
            float x = -260f;
            foreach (var tab in GearTabs)
            {
                string captured = tab;
                var b = UiBuild.Button(w, tab + "Tab", tab, new Vector2(0.5f, 1f), new Vector2(x, -86f), new Vector2(240f, 58f), () => SelectGearTab(captured), 24);
                Style(b);
                gearTabs[tab] = b;
                x += 260f;
            }
            HomeEntry[] Page(string tab) => tab switch
            {
                "Loadout" => new[]
                {
                    new HomeEntry("武器・装備の編集", "銃・近接武器・カスタムを選ぶ画面は準備中です。銃の試し撃ち切り替え（テスト用）は射撃場のメニューにあります。", HomeEntryStatus.ComingSoon),
                },
                "Skin" => new[]
                {
                    new HomeEntry("スキンの確認（テスト用）", "射撃場で、プレイヤー・武器・手の見た目を切り替えて確認します。", HomeEntryStatus.Test, OpenSkinTest),
                },
                _ => new[]
                {
                    new HomeEntry("パッシブスキル", "常に効果があるスキル（1枠）。準備中です。", HomeEntryStatus.ComingSoon),
                    new HomeEntry("アクティブスキル", "使うと効果が出るスキル（1枠）。準備中です。", HomeEntryStatus.ComingSoon),
                },
            };
            foreach (var tab in GearTabs)
            {
                var page = UiBuild.Stretch(UiBuild.Rect(tab + "Page", w));
                Rows(page, -168f, Page(tab), 800f);
                gearPages[tab] = page.gameObject;
            }
        }

        static void OpenSkinTest()
        {
            GameMenu.OpenSkinSettingsOnLoad = true;
            HomeScreen.Load(GameScenes.FiringRange);
        }

        public void SelectGearTab(string tab)
        {
            GearTab = tab;
            foreach (var pair in gearPages)
                pair.Value.SetActive(pair.Key == tab);
            foreach (var pair in gearTabs)
            {
                var image = (Image)pair.Value.targetGraphic;
                image.color = pair.Key == tab ? new Color(0.3f, 0.21f, 0.08f, 0.95f) : new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.95f);
            }
            messageText.text = "";
        }

        public void OpenGear(string tab)
        {
            OpenPanel("GearPanel");
            SelectGearTab(tab);
        }

        bool resetArmed;

        void ResetLayoutAsk()
        {
            if (!resetArmed)
            {
                resetArmed = true;
                messageText.text = "もう一度押すと、ホームの配置を初期配置に戻して保存します";
                return;
            }
            resetArmed = false;
            Layout.Save(Layout.Defaults());
            messageText.text = "初期配置に戻しました";
        }

        public void OpenPanel(string name)
        {
            ClosePanel();
            panels[name].SetActive(true);
            panels[name].transform.SetAsLastSibling();
            messageText.transform.SetAsLastSibling();
            OpenPanelName = name;
        }

        public void ClosePanel()
        {
            foreach (var p in panels.Values)
                p.SetActive(false);
            OpenPanelName = null;
            resetArmed = false;
            if (messageText != null)
                messageText.text = "";
        }

        public GameObject PanelObject(string name) => panels[name];

        public void OpenLayoutEditor()
        {
            ClosePanel();
            LayoutEditButton.gameObject.SetActive(false);
            Editor.Open(() => LayoutEditButton.gameObject.SetActive(true));
        }
    }
}
