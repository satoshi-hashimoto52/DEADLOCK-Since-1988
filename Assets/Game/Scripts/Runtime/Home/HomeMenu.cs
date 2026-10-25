using System;
using System.Collections.Generic;
using Deadlock.Game;
using Deadlock.UI;
using Deadlock.UI.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.Home
{
    /// <summary>
    /// Home screen menu (built at runtime on the home UI canvas). The menu elements live on a full-screen
    /// <see cref="UiLayoutRoot"/> ("home" layout, saved on this device): they are independent of the background framing
    /// and may sit in the black margins left / right of the height-matched background. Entries: game mode select
    /// (existing prototype scenes), equipment, skills, gacha, points, settings; entries without a real screen yet open a
    /// panel that says so. Layout editing opens from Settings and from a fixed button inside the safe area, which is not
    /// movable, so the editor (and with it every element) can always be reached again.
    /// </summary>
    public sealed class HomeMenu : MonoBehaviour
    {
        public const string LayoutId = "home";
        public const int LayoutVersion = 1;

        public const string Mode = "mode";
        public const string Equipment = "equipment";
        public const string Skill = "skill";
        public const string Points = "points";
        public const string Gacha = "gacha";
        public const string Settings = "settings";

        static readonly Color Plate = new Color(0.06f, 0.06f, 0.07f, 0.78f);
        static readonly Color Brass = new Color(0.72f, 0.58f, 0.32f, 0.95f);
        static readonly Color Ivory = new Color(0.9f, 0.86f, 0.74f, 1f);

        public static readonly UiLayoutSpec[] Specs =
        {
            new UiLayoutSpec(Mode, new Vector2(300f, 76f)),
            new UiLayoutSpec(Equipment, new Vector2(300f, 76f)),
            new UiLayoutSpec(Skill, new Vector2(300f, 76f)),
            new UiLayoutSpec(Points, new Vector2(230f, 60f)),
            new UiLayoutSpec(Gacha, new Vector2(170f, 170f)),
            new UiLayoutSpec(Settings, new Vector2(84f, 84f)),
        };

        public static string DisplayName(string id) => id switch
        {
            Mode => "ゲームモード",
            Equipment => "装備",
            Skill => "スキル",
            Points => "ポイント表示",
            Gacha => "ガチャ",
            Settings => "設定",
            _ => id,
        };

        RectTransform canvasRect;
        RectTransform panelLayer;
        readonly Dictionary<string, GameObject> panels = new Dictionary<string, GameObject>();

        public UiLayoutRoot Layout { get; private set; }
        public UiLayoutEditor Editor { get; private set; }
        public Button LayoutEditButton { get; private set; }
        /// <summary>Name of the open panel, or null.</summary>
        public string OpenPanelName { get; private set; }

        /// <summary>
        /// Initial layout for a canvas size and safe area (canvas units, origin bottom left): mode / equipment / skills
        /// on the left, points + settings top right, gacha bottom right, all inside the safe area. The right-hand
        /// entries stay clear of the window and the candle of the height-matched background.
        /// </summary>
        public static UiLayoutData Defaults(Vector2 canvas, Rect safe)
        {
            const float m = 16f;
            var data = new UiLayoutData { layoutVersion = LayoutVersion };
            void Add(string id, float cx, float cy) =>
                data.items.Add(new UiLayoutItem { id = id, center = UiLayoutMath.ToNormalized(new Vector2(cx, cy), canvas), scale = 1f, visible = true });
            float leftX = safe.xMin + m + 150f;
            Add(Mode, leftX, canvas.y * 0.64f);
            Add(Equipment, leftX, canvas.y * 0.52f);
            Add(Skill, leftX, canvas.y * 0.40f);
            float settingsX = safe.xMax - m - 42f, topY = safe.yMax - m - 42f;
            Add(Points, settingsX - 42f - 12f - 115f, topY);
            Add(Gacha, safe.xMax - m - 85f, safe.yMin + m + 85f);
            Add(Settings, settingsX, topY);
            return data;
        }

        void Awake() => Build();

        void Start() => Layout.Load();

        void Build()
        {
            canvasRect = (RectTransform)transform;
            var layoutRect = UiBuild.Stretch(UiBuild.Rect("MenuLayout", canvasRect)); // whole screen, not the background
            Layout = layoutRect.gameObject.AddComponent<UiLayoutRoot>();
            Layout.layoutId = LayoutId;
            Layout.layoutVersion = LayoutVersion;
            Layout.SetDefaults(Defaults);
            Layout.Register(Entry(Mode, "ゲームモード", null, () => OpenPanel("ModePanel")), Specs[0]);
            Layout.Register(Entry(Equipment, "装備", null, () => OpenPanel("EquipmentPanel")), Specs[1]);
            Layout.Register(Entry(Skill, "スキル", null, () => OpenPanel("SkillPanel")), Specs[2]);
            Layout.Register(Entry(Points, "－ PT", "未実装", () => OpenPanel("PointsPanel")), Specs[3]);
            Layout.Register(Entry(Gacha, "ガチャ", null, () => OpenPanel("GachaPanel")), Specs[4]);
            Layout.Register(Entry(Settings, "設定", null, () => OpenPanel("SettingsPanel")), Specs[5]);

            // Fixed (not movable) entry to the layout editor inside the safe area: the way back if an element was put
            // where it cannot be pressed.
            var fixedSafe = UiBuild.Stretch(UiBuild.Rect("FixedSafeArea", canvasRect));
            fixedSafe.gameObject.AddComponent<HomeSafeAreaPanel>();
            LayoutEditButton = UiBuild.Button(fixedSafe, "LayoutEditButton", "レイアウト編集", new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(190f, 44f), OpenLayoutEditor, 18);
            Style(LayoutEditButton, 0.6f);

            panelLayer = UiBuild.Stretch(UiBuild.Rect("Panels", canvasRect));
            BuildPanels();

            Editor = gameObject.AddComponent<UiLayoutEditor>();
            Editor.DisplayName = DisplayName;
            Editor.Init(Layout, canvasRect);
        }

        RectTransform Entry(string id, string label, string sub, Action onClick)
        {
            var rect = UiBuild.Rect(id, transform);
            var image = UiBuild.Image(rect, Plate, true);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Brass;
            outline.effectDistance = new Vector2(2f, -2f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
            var text = UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Label", rect)), label, 26, TextAnchor.MiddleCenter, Ivory);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = 40;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            if (sub != null)
            {
                var subRect = UiBuild.Rect("Sub", rect);
                subRect.anchorMin = new Vector2(0f, 0f);
                subRect.anchorMax = new Vector2(1f, 0.32f);
                subRect.offsetMin = subRect.offsetMax = Vector2.zero;
                var subText = UiBuild.Text(subRect, sub, 12, TextAnchor.MiddleCenter, new Color(1f, 0.7f, 0.5f, 0.9f));
                subText.resizeTextForBestFit = true;
                subText.resizeTextMinSize = 8;
                subText.resizeTextMaxSize = 20;
                ((RectTransform)text.transform).anchorMin = new Vector2(0f, 0.28f);
            }
            return rect;
        }

        static void Style(Button button, float alpha)
        {
            var image = (Image)button.targetGraphic;
            image.color = new Color(Plate.r, Plate.g, Plate.b, alpha);
            var outline = button.gameObject.AddComponent<Outline>();
            outline.effectColor = Brass;
            button.GetComponentInChildren<Text>().color = Ivory;
        }

        void BuildPanels()
        {
            Panel("ModePanel", "ゲームモード選択",
                "開発中のモード（試作）です。デッドライン・ラストスタンドなどの正式なモードは未実装です。",
                ("射撃場", () => HomeScreen.Load(GameScenes.FiringRange)),
                ("トレーニング（試作）", () => HomeScreen.Load(GameScenes.Training)),
                ("遺跡（テスト）", () => HomeScreen.Load(GameScenes.Ruins)),
                ("オンライン検証（LAN）", () => HomeScreen.Load(GameScenes.Online)));
            Panel("EquipmentPanel", "装備（未実装）",
                "正式な装備画面はまだありません。\n見た目のテスト（装備・スキン）は、射撃場などのゲーム中のメニュー → 設定 →「装備・スキン（テスト用）」で確認できます。");
            Panel("SkillPanel", "スキル（未実装）", "スキルの効果と、スキルを選ぶ画面はまだありません。");
            Panel("GachaPanel", "ガチャ（未実装）", "ガチャと、ポイントの獲得・消費はまだありません。");
            Panel("PointsPanel", "ポイント（未実装）", "ポイントの獲得・保存はまだありません。表示は仮です。");
            Panel("SettingsPanel", "設定",
                "ゲーム中の操作設定・操作レイアウトは、射撃場などのゲーム中のメニュー → 設定 から変更できます。",
                ("ホームのレイアウト編集", OpenLayoutEditor));
        }

        void Panel(string name, string title, string body, params (string label, Action action)[] buttons)
        {
            var panel = UiBuild.Stretch(UiBuild.Rect(name, panelLayer));
            UiBuild.Image(panel, new Color(0f, 0f, 0f, 0.55f), true);
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", panel));
            safe.gameObject.AddComponent<HomeSafeAreaPanel>();
            float height = 250f + buttons.Length * 70f;
            var window = UiBuild.Place(UiBuild.Rect("Window", safe), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, height));
            UiBuild.Image(window, new Color(0.07f, 0.07f, 0.09f, 0.96f), true);
            window.gameObject.AddComponent<Outline>().effectColor = Brass;
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(700f, 44f)),
                title, 30, TextAnchor.MiddleCenter, Ivory);
            var bodyText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Body", window), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(680f, 90f)),
                body, 18, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.8f));
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float y = -166f;
            foreach (var (label, action) in buttons)
            {
                var b = UiBuild.Button(window, label, label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(420f, 58f), action, 22);
                Style(b, 0.92f);
                y -= 70f;
            }
            Style(UiBuild.Button(window, "CloseButton", "閉じる", new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(240f, 54f), ClosePanel, 22), 0.92f);
            panel.gameObject.SetActive(false);
            panels[name] = panel.gameObject;
        }

        public void OpenPanel(string name)
        {
            ClosePanel();
            panels[name].SetActive(true);
            panels[name].transform.SetAsLastSibling();
            OpenPanelName = name;
        }

        public void ClosePanel()
        {
            foreach (var p in panels.Values)
                p.SetActive(false);
            OpenPanelName = null;
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
