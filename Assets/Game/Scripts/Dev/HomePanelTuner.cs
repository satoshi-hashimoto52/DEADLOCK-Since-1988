using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Deadlock.Game;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Home
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: compiled only in the Editor and development builds, never in a release
    /// build). Tuner for the fill transparency of the home panels (<see cref="HomePanelLook"/>), on its own canvas above
    /// the home: a "開発：パネル" button opens a window with, from the top, the comparison switch (the approved panel
    /// images, or the earlier Unity-drawn panels for comparison), a list of the 6 targets (each with its value, the
    /// selected one marked), and for the selected target its value, a slider, −1 % / ＋1 % and "初期値に戻す" (plus
    /// "すべて初期値"). The values are development overrides kept on the device; release builds use the defaults.
    /// Sized for fingers on an iPhone in landscape and kept inside the safe area.
    /// Started on the home in development builds; in the Editor only after DEADLOCK/Dev/Home Panel Tuner is switched on.
    /// </summary>
    public sealed class HomePanelTuner : MonoBehaviour
    {
        /// <summary>1: show the earlier Unity-drawn panels instead of the approved images (comparison only).</summary>
        public const string CompareKey = "deadlock.dev.home.panelCompareLegacy";
        /// <summary>EditorPrefs key of the Editor switch (DEADLOCK/Dev/Home Panel Tuner).</summary>
        public const string EditorToggleKey = "DEADLOCK.HomePanelTuner";
        static readonly Color Normal = new Color(0.16f, 0.13f, 0.09f, 1f);
        static readonly Color Chosen = new Color(0.55f, 0.38f, 0.1f, 1f);

        HomeMenu menu;
        readonly Dictionary<HomePanelTarget, Button> targets = new Dictionary<HomePanelTarget, Button>();
        bool updating;

        public HomePanelTarget Selected { get; private set; } = HomePanelTarget.MainButton;
        public RectTransform Window { get; private set; }
        public Button OpenButton { get; private set; }
        public Button CompareButton { get; private set; }
        public Button MinusButton { get; private set; }
        public Button PlusButton { get; private set; }
        public Button ResetButton { get; private set; }
        public Button ResetAllButton { get; private set; }
        public Button CloseButton { get; private set; }
        public Slider Slider { get; private set; }
        public Text ValueText { get; private set; }
        public Text StatusText { get; private set; }
        public bool IsOpen => Window != null && Window.gameObject.activeSelf;
        public static bool ComparingLegacy => PlayerPrefs.GetInt(CompareKey, 0) == 1;
        public Button TargetButton(HomePanelTarget t) => targets[t];

        // ------------------------------------------------------------------------------------------- start-up

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorPrefs.GetBool(EditorToggleKey, false))
                return;
#else
            if (!Debug.isDebugBuild)
                return;
#endif
            var go = new GameObject("HomePanelTunerBoot");
            DontDestroyOnLoad(go);
            go.AddComponent<Starter>();
        }

        /// <summary>Puts a tuner on every home that loads.</summary>
        sealed class Starter : MonoBehaviour
        {
            void Awake()
            {
                SceneManager.sceneLoaded += (s, m) => { if (s.name == GameScenes.Home) StartCoroutine(Attach()); };
                if (SceneManager.GetActiveScene().name == GameScenes.Home)
                    StartCoroutine(Attach());
            }

            static IEnumerator Attach()
            {
                yield return null;
                var menu = FindAnyObjectByType<HomeMenu>();
                if (menu != null && FindAnyObjectByType<HomePanelTuner>() == null)
                    Create(menu);
            }
        }

        /// <summary>Builds the tuner over this home (its canvas goes with the home scene) and applies the saved switch.</summary>
        public static HomePanelTuner Create(HomeMenu menu)
        {
            var canvasGo = new GameObject("HomePanelTunerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasGo, menu.gameObject.scene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // above the home menu, its sub-screens and the layout editor
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(HomeFraming.RefWidth, HomeFraming.RefHeight);
            scaler.matchWidthOrHeight = 1f;
            var tuner = canvasGo.AddComponent<HomePanelTuner>();
            tuner.menu = menu;
            tuner.Build((RectTransform)canvasGo.transform);
            tuner.ApplyLook();
            tuner.Refresh();
            return tuner;
        }

        void OnEnable() => HomePanelLook.Changed += Refresh;

        void OnDisable() => HomePanelLook.Changed -= Refresh;

        // ------------------------------------------------------------------------------------------- UI

        Button Make(RectTransform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, System.Action onClick, int font)
        {
            var b = UiBuild.Button(parent, name, label, anchor, pos, size, onClick, font);
            ((Image)b.targetGraphic).color = Normal;
            b.GetComponentInChildren<Text>().color = Color.white;
            return b;
        }

        void Build(RectTransform canvas)
        {
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", canvas));
            safe.gameObject.AddComponent<HomeSafeAreaPanel>();

            // Bottom centre, right of the fixed "レイアウト編集" button (170 wide at the centre): clear of the home buttons.
            OpenButton = Make(safe, "PanelTunerButton", "開発：パネル", new Vector2(0.5f, 0f), new Vector2(200f, 8f), new Vector2(190f, 50f), () => SetOpen(!IsOpen), 20);
            ((RectTransform)OpenButton.transform).pivot = new Vector2(0.5f, 0f);
            ((Image)OpenButton.targetGraphic).color = new Color(0.35f, 0.08f, 0.08f, 0.85f);

            const float W = 760f, H = 776f, inner = 690f;
            Window = UiBuild.Place(UiBuild.Rect("PanelTunerWindow", safe), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            UiBuild.Image(Window, new Color(0.04f, 0.035f, 0.03f, 0.96f), true);
            Window.gameObject.AddComponent<Outline>().effectColor = new Color(0.9f, 0.35f, 0.3f, 1f);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", Window), new Vector2(0.5f, 1f), new Vector2(-70f, -12f), new Vector2(560f, 40f)),
                "パネル中央の透明度（開発用）", 24, TextAnchor.MiddleLeft, Color.white);
            CloseButton = Make(Window, "Close", "閉じる", new Vector2(1f, 1f), new Vector2(-14f, -8f), new Vector2(140f, 52f), () => SetOpen(false), 22);
            ((RectTransform)CloseButton.transform).pivot = new Vector2(1f, 1f);

            // Top: the comparison switch, and what is drawn now.
            CompareButton = Make(Window, "CompareToggle", "", new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(inner, 60f), ToggleCompare, 22);
            StatusText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Status", Window), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(inner, 44f)), "", 17, TextAnchor.MiddleCenter, Color.white);
            StatusText.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Middle: the 6 targets, one per row.
            float y = -182f;
            foreach (var t in HomePanelLook.Targets)
            {
                var captured = t;
                var b = Make(Window, "Target_" + t, "", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(inner, 56f), () => Select(captured), 22);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                var label = (RectTransform)b.GetComponentInChildren<Text>().transform;
                label.offsetMin = new Vector2(20f, label.offsetMin.y);
                targets[t] = b;
                y -= 63f;
            }

            // Bottom: value, slider, steps and resets for the selected target.
            ValueText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("Value", Window), new Vector2(0.5f, 1f), new Vector2(0f, -566f), new Vector2(inner, 40f)), "", 24, TextAnchor.MiddleCenter, Color.white);
            var sliderGo = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderGo.name = "Slider";
            var sr = (RectTransform)sliderGo.transform;
            sr.SetParent(Window, false);
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.anchoredPosition = new Vector2(0f, -612f);
            sr.sizeDelta = new Vector2(inner - 40f, 56f);
            var handle = (RectTransform)sr.Find("Handle Slide Area/Handle");
            handle.sizeDelta = new Vector2(52f, 0f); // a finger-sized knob over the full height
            Slider = sliderGo.GetComponent<Slider>();
            Slider.minValue = HomePanelLook.MinPercent;
            Slider.maxValue = HomePanelLook.MaxPercent;
            Slider.wholeNumbers = true;
            Slider.onValueChanged.AddListener(v =>
            {
                if (!updating)
                    HomePanelLook.Set(Selected, Mathf.RoundToInt(v));
            });

            MinusButton = Make(Window, "Minus", "－1%", new Vector2(0.5f, 1f), new Vector2(-268f, -686f), new Vector2(150f, 62f), () => Step(-1), 24);
            PlusButton = Make(Window, "Plus", "＋1%", new Vector2(0.5f, 1f), new Vector2(-108f, -686f), new Vector2(150f, 62f), () => Step(1), 24);
            ResetButton = Make(Window, "Reset", "初期値に戻す", new Vector2(0.5f, 1f), new Vector2(68f, -686f), new Vector2(186f, 62f), ResetSelected, 20);
            ResetAllButton = Make(Window, "ResetAll", "すべて初期値", new Vector2(0.5f, 1f), new Vector2(255f, -686f), new Vector2(172f, 62f), () => HomePanelLook.ResetAll(), 20);
            Window.gameObject.SetActive(false);
        }

        public void SetOpen(bool open)
        {
            Window.gameObject.SetActive(open);
            if (open)
            {
                Window.SetAsLastSibling();
                Refresh();
            }
        }

        /// <summary>The approved panel images (the home's own), or the Unity-drawn panels while comparing.</summary>
        void ApplyLook() => menu.SetPanelSkin(ComparingLegacy ? null : menu.panelFrame, ComparingLegacy ? null : menu.panelFill);

        public void ToggleCompare()
        {
            PlayerPrefs.SetInt(CompareKey, ComparingLegacy ? 0 : 1);
            PlayerPrefs.Save();
            ApplyLook();
            Refresh();
        }

        public void Select(HomePanelTarget t)
        {
            Selected = t;
            Refresh();
        }

        public void Step(int d) => HomePanelLook.Set(Selected, HomePanelLook.Get(Selected) + d);

        public void ResetSelected() => HomePanelLook.Reset(Selected);

        /// <summary>A panel of this target to read the drawn alpha from (a shown one if there is one).</summary>
        HomePanelPart PartOf(HomePanelTarget t)
        {
            HomePanelPart any = null;
            foreach (var p in menu.PanelParts)
            {
                if (p.target != t)
                    continue;
                if (p.gameObject.activeInHierarchy && !p.dimmed)
                    return p;
                any ??= p;
            }
            return any;
        }

        void Refresh()
        {
            if (Window == null)
                return;
            updating = true;
            int v = HomePanelLook.Get(Selected);
            ValueText.text = $"{HomePanelLook.DisplayName(Selected)}：{v}%（初期値 {HomePanelLook.Default(Selected)}%）";
            Slider.value = v;
            foreach (var pair in targets)
            {
                bool chosen = pair.Key == Selected;
                ((Image)pair.Value.targetGraphic).color = chosen ? Chosen : Normal;
                pair.Value.GetComponentInChildren<Text>().text = $"{(chosen ? "▶ " : "　 ")}{HomePanelLook.DisplayName(pair.Key)}　{HomePanelLook.Get(pair.Key)}%";
            }
            CompareButton.GetComponentInChildren<Text>().text = ComparingLegacy ? "従来表示との比較：ON（従来の描画を表示中）" : "従来表示との比較：OFF（正式パネルを表示中）";
            ((Image)CompareButton.targetGraphic).color = ComparingLegacy ? new Color(0.4f, 0.25f, 0.05f, 1f) : new Color(0.15f, 0.35f, 0.15f, 1f);
            var part = PartOf(Selected);
            if (ComparingLegacy || part == null || !part.Skinned)
            {
                StatusText.color = new Color(1f, 0.75f, 0.3f, 1f);
                StatusText.text = ComparingLegacy
                    ? "比較のため従来の描画を表示中です。値は変えられますが、見た目は変わりません。"
                    : "正式パネルの画像がありません（ログを確認してください）。従来の描画を表示中です。";
            }
            else
            {
                StatusText.color = Color.white;
                StatusText.text = string.Format(CultureInfo.InvariantCulture,
                    "描画中：地のアルファ {0:0.00}（透明度 {1:0}%）／枠とリベットのアルファ {2:0.00}",
                    part.ShownAlpha, (1f - part.ShownAlpha) * 100f, part.FrameImage != null ? part.FrameImage.color.a : 0f);
            }
            updating = false;
        }
    }
}
