using System;
using Deadlock.Game;
using Deadlock.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>Adds scene-specific entries to the in-game menu (e.g. online: leave). Put it on the menu's GameObject.</summary>
    public interface IGameMenuItems
    {
        /// <param name="add">(object name, label, action)</param>
        void AddMenuItems(Action<string, string, Action> add);
    }

    /// <summary>
    /// In-game menu: a menu button that is always on screen (top centre, never covered by movable touch widgets),
    /// and a panel with Resume / Retry (training) / scene switch / Settings → Control layout.
    /// Opening it blocks the local player's input. Pausing the game is a separate, offline-only concern
    /// (<see cref="OfflineSessionPause"/>). Built at runtime; Tab opens / closes it on desktop.
    /// </summary>
    public sealed class GameMenu : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [Tooltip("Optional: present in the training scene (enables Retry).")]
        [SerializeField] TrainingSession session;

        RectTransform layer;
        GameObject panel;
        GameObject controlsPanel;
        Text touchAdsText;
        Text desktopAdsText;
        TouchLayoutEditor layoutEditor;

        public bool IsOpen { get; private set; }
        public bool IsControlSettingsOpen => controlsPanel != null && controlsPanel.activeSelf;
        public bool IsEditingLayout => layoutEditor != null && layoutEditor.IsOpen;
        public Button MenuButton { get; private set; }

        public event Action Opened;
        public event Action Closed;

        void Awake() => Build();

        // Other runtime UI (training HUD) is built in Awake too: keep the menu above all of it.
        void Start() => layer.SetAsLastSibling();

        void Update()
        {
            if (input.MenuKeyPressed && !IsEditingLayout)
            {
                if (IsOpen)
                    Close();
                else
                    Open();
            }
        }

        public void Open()
        {
            if (IsOpen)
                return;
            IsOpen = true;
            input.Block("menu");
            panel.SetActive(true);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen || IsEditingLayout)
                return;
            IsOpen = false;
            panel.SetActive(false);
            controlsPanel.SetActive(false);
            input.Unblock("menu");
            Closed?.Invoke();
        }

        public void OpenLayoutEditor()
        {
            if (!IsOpen)
                Open();
            panel.SetActive(false);
            layoutEditor.Open(() => panel.SetActive(true));
        }

        /// <summary>Menu → Settings → Controls: ADS hold / toggle, separately for touch and PC (saved on this device).</summary>
        public void OpenControlSettings()
        {
            if (!IsOpen)
                Open();
            panel.SetActive(false);
            RefreshControlSettings();
            controlsPanel.SetActive(true);
        }

        public void CloseControlSettings()
        {
            controlsPanel.SetActive(false);
            if (IsOpen)
                panel.SetActive(true);
        }

        void SetAdsMode(InputMode mode, AdsMode value)
        {
            ControlSettings.SetAdsMode(mode, value);
            RefreshControlSettings();
        }

        void RefreshControlSettings()
        {
            touchAdsText.text = $"ADS操作方式（タッチ：ADSボタン）　現在：{ControlSettings.Label(ControlSettings.GetAdsMode(InputMode.Touch))}";
            desktopAdsText.text = $"ADS操作方式（PC：右クリック）　現在：{ControlSettings.Label(ControlSettings.GetAdsMode(InputMode.Desktop))}";
        }

        void BuildControlSettings()
        {
            controlsPanel = UiBuild.Stretch(UiBuild.Rect("ControlSettingsPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)controlsPanel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", controlsPanel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 640f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(840f, 60f)),
                "設定 → 操作設定", 40, TextAnchor.MiddleCenter);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Hint", window), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(840f, 64f)),
                "長押し：押している間だけADS　／　切り替え：押すたびにON・OFF", 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));

            float y = -180f;
            void Row(InputMode mode, string prefix, out Text label)
            {
                label = UiBuild.Text(UiBuild.Place(UiBuild.Rect(prefix + "AdsLabel", window), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(840f, 40f)),
                    "", 26, TextAnchor.MiddleCenter);
                UiBuild.Button(window, prefix + "AdsHold", "長押し", new Vector2(0.5f, 1f), new Vector2(-130f, y - 52f), new Vector2(240f, 72f),
                    () => SetAdsMode(mode, AdsMode.Hold), 28);
                UiBuild.Button(window, prefix + "AdsToggle", "切り替え", new Vector2(0.5f, 1f), new Vector2(130f, y - 52f), new Vector2(240f, 72f),
                    () => SetAdsMode(mode, AdsMode.Toggle), 28);
                y -= 150f;
            }
            Row(InputMode.Touch, "Touch", out touchAdsText);
            // The PC setting is only meaningful where a mouse can be used (Editor / desktop).
            Row(InputMode.Desktop, "Desktop", out desktopAdsText);
            if (Application.isMobilePlatform)
            {
                foreach (var name in new[] { "DesktopAdsLabel", "DesktopAdsHold", "DesktopAdsToggle" })
                    window.Find(name).gameObject.SetActive(false);
            }
            UiBuild.Button(window, "BackButton", "戻る", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(300f, 72f), CloseControlSettings, 28);
            controlsPanel.SetActive(false);
        }

        void Build()
        {
            layer = UiBuild.Stretch(UiBuild.Rect("MenuLayer", transform));
            layer.SetAsLastSibling();
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            MenuButton = UiBuild.Button(safe, "MenuButton", "メニュー", new Vector2(0.5f, 1f), new Vector2(0f, -10f),
                new Vector2(220f, 64f), () => { if (IsOpen) Close(); else Open(); }, 28);

            panel = UiBuild.Stretch(UiBuild.Rect("MenuPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)panel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", panel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 800f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(560f, 60f)),
                "メニュー", 44, TextAnchor.MiddleCenter);

            float y = -120f;
            Vector2 size = new Vector2(480f, 76f);
            void Add(string name, string label, Action action)
            {
                UiBuild.Button(window, name, label, new Vector2(0.5f, 1f), new Vector2(0f, y), size, action);
                y -= 92f;
            }

            Add("ResumeButton", "再開", Close);
            if (session != null)
                Add("RetryButton", "再挑戦（最初から）", () => session.Retry());
            foreach (var extra in GetComponents<IGameMenuItems>())
                extra.AddMenuItems((name, label, action) => Add(name, label, action));
            string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (current != GameScenes.FiringRange)
                Add("SceneButton", "射撃場へ", () => GameScenes.Load(GameScenes.FiringRange));
            if (current != GameScenes.Training)
                Add("TrainingButton", "トレーニングへ", () => GameScenes.Load(GameScenes.Training));
            if (current != GameScenes.Online)
                Add("OnlineButton", "オンライン検証（LAN）へ", () => GameScenes.Load(GameScenes.Online));
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("SettingsHeader", window), new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(480f, 40f)),
                "設定", 26, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            y -= 52f;
            Add("ControlSettingsButton", "操作設定", OpenControlSettings);
            Add("LayoutButton", "操作レイアウト", OpenLayoutEditor);
            panel.SetActive(false);
            BuildControlSettings();

            layoutEditor = gameObject.AddComponent<TouchLayoutEditor>();
            layoutEditor.Init(input, (RectTransform)transform);
        }
    }
}
