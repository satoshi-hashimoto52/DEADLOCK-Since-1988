using System;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.View;
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
    /// and a panel with Resume / Retry (training) / scene switch / Settings → Control layout / Equipment & skins (test).
    /// Opening it blocks the local player's input. Pausing the game is a separate, offline-only concern
    /// (<see cref="OfflineSessionPause"/>). Built at runtime; Tab opens / closes it on desktop.
    /// </summary>
    public sealed class GameMenu : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [Tooltip("Optional: present in the training scene (enables Retry).")]
        [SerializeField] TrainingSession session;
        [Tooltip("Menu / settings icons and the title logo (falls back to text when unset).")]
        [SerializeField] ControlIconSet icons;
        [Tooltip("Optional: skins offered on the test skin screen (no entry when unset).")]
        [SerializeField] SkinCatalog skins;

        RectTransform layer;
        GameObject panel;
        GameObject controlsPanel;
        GameObject skinPanel;
        readonly System.Collections.Generic.List<Action> skinRefresh = new System.Collections.Generic.List<Action>();
        Text touchAdsText;
        Text desktopAdsText;
        Text dragAimText;
        Text fireModeText;
        Text transparencyText;
        Slider transparencySlider;
        TouchLayoutEditor layoutEditor;

        public bool IsOpen { get; private set; }
        public bool IsControlSettingsOpen => controlsPanel != null && controlsPanel.activeSelf;
        public bool IsSkinSettingsOpen => skinPanel != null && skinPanel.activeSelf;
        public bool IsEditingLayout => layoutEditor != null && layoutEditor.IsOpen;
        public Button MenuButton { get; private set; }

        public event Action Opened;
        public event Action Closed;
        public event Action SkinSettingsOpened;
        public event Action SkinSettingsClosed;

        void Awake() => Build();

        // The skin choice can change from elsewhere too (reset, another screen): the labels follow the saved choice.
        void OnEnable() => SkinSelection.Changed += RefreshSkinSettings;

        void OnDisable() => SkinSelection.Changed -= RefreshSkinSettings;

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
            bool skinOpen = IsSkinSettingsOpen;
            if (skinPanel != null)
                skinPanel.SetActive(false);
            input.Unblock("menu");
            if (skinOpen)
                SkinSettingsClosed?.Invoke();
            Closed?.Invoke();
        }

        public void OpenLayoutEditor()
        {
            if (!IsOpen)
                Open();
            panel.SetActive(false);
            layoutEditor.Open(() => panel.SetActive(true));
        }

        /// <summary>
        /// Menu → Settings → Controls: ADS hold / toggle (touch and PC separately), aiming with the ADS button and the
        /// FIRE behaviour (touch). Saved on this device; the touch layout is not touched.
        /// </summary>
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

        /// <summary>
        /// Menu → Settings → Equipment & skins (test): the player (front, full body) and the rifle (side view) shown with
        /// the chosen skins, next to the choices. The weapon skin of each weapon and the hand / arm skin are chosen
        /// separately, applied at once (here and in the first-person view) and saved on this device. The same screen as
        /// the earlier skin test screen (one screen, one save), not the final equipment screen.
        /// </summary>
        public void OpenSkinSettings()
        {
            if (skinPanel == null)
                return;
            if (!IsOpen)
                Open();
            panel.SetActive(false);
            RefreshSkinSettings();
            skinPanel.SetActive(true);
            SkinSettingsOpened?.Invoke();
        }

        public void CloseSkinSettings()
        {
            if (skinPanel == null)
                return;
            bool wasOpen = skinPanel.activeSelf;
            skinPanel.SetActive(false);
            if (IsOpen)
                panel.SetActive(true);
            if (wasOpen)
                SkinSettingsClosed?.Invoke();
        }

        void RefreshSkinSettings()
        {
            foreach (var refresh in skinRefresh)
                refresh();
        }

        void BuildSkinSettings()
        {
            skinPanel = UiBuild.Stretch(UiBuild.Rect("SkinSettingsPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)skinPanel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            // Inside the safe area (notch / home indicator); the window fits 4:3 (1440 x 1080) and iPhone safe areas.
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", skinPanel.transform));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var window = UiBuild.Place(UiBuild.Rect("Window", safe), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1392f, 1000f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 1f), true); // opaque: the touch controls behind do not show through the previews
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(1300f, 52f)),
                "設定 → 装備・スキン（テスト用）", 38, TextAnchor.MiddleCenter);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Hint", window), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(1300f, 34f)),
                "見た目だけが変わります（性能・判定・操作は変わりません）。武器と手・腕は別々に選べ、この端末に保存されます", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));

            // Previews: separate images (the player does not hold the rifle), aspect kept, whole image visible.
            RectTransform Preview(string name, string caption, Vector2 position, Vector2 size, Sprite sprite, Texture2D regions, string weaponId, bool player)
            {
                var box = UiBuild.Place(UiBuild.Rect(name, window), new Vector2(0f, 1f), position, size);
                UiBuild.Image(box, new Color(0.16f, 0.17f, 0.21f, 1f), false);
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("Caption", box), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(size.x - 20f, 32f)),
                    sprite != null ? caption : caption + "（画像なし）", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
                if (sprite == null)
                    return box;
                var art = UiBuild.Rect("Image", box);
                art.anchorMin = Vector2.zero;
                art.anchorMax = Vector2.one;
                art.offsetMin = new Vector2(12f, 12f);
                art.offsetMax = new Vector2(-12f, -44f);
                art.gameObject.AddComponent<Image>();
                art.gameObject.AddComponent<SkinnedImage>().Init(skins, sprite, regions, weaponId, player);
                return box;
            }

            var weaponIds = new System.Collections.Generic.List<string>();
            WeaponSkinSet shown = null;
            foreach (var set in skins.weapons)
                if (set != null && !string.IsNullOrEmpty(set.weaponId) && set.sideView != null && shown == null)
                    shown = set;
            Preview("PlayerPreview", "プレイヤー（正面）", new Vector2(24f, -116f), new Vector2(520f, 860f), skins.playerFront, skins.playerFrontRegions, null, true);
            Preview("WeaponPreview", shown != null ? $"{shown.label}（横）" : "武器（横）", new Vector2(568f, -116f), new Vector2(800f, 400f),
                shown?.sideView, shown?.sideViewRegions, shown?.weaponId, false);

            // Choices (right column, below the weapon).
            var choices = UiBuild.Place(UiBuild.Rect("Choices", window), new Vector2(0f, 1f), new Vector2(568f, -532f), new Vector2(800f, 444f));
            float y = 0f;
            void Choices(string prefix, Func<string> current, (string id, string label)[] options, Action<string> choose)
            {
                var label = UiBuild.Text(UiBuild.Place(UiBuild.Rect(prefix + "Label", choices), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(780f, 40f)),
                    "", 26, TextAnchor.MiddleCenter);
                float width = 280f, gap = 20f;
                float left = -(options.Length * width + (options.Length - 1) * gap) * 0.5f + width * 0.5f;
                for (int i = 0; i < options.Length; i++)
                {
                    string id = options[i].id;
                    UiBuild.Button(choices, $"{prefix}_{id}", options[i].label, new Vector2(0.5f, 1f), new Vector2(left + i * (width + gap), y - 48f), new Vector2(width, 66f),
                        () => { choose(id); RefreshSkinSettings(); }, 26);
                }
                skinRefresh.Add(() => label.text = current());
                y -= 136f;
            }

            var notes = new System.Collections.Generic.List<string>();
            foreach (var set in skins.weapons)
            {
                if (set == null || string.IsNullOrEmpty(set.weaponId))
                    continue;
                weaponIds.Add(set.weaponId);
                if (set.skins.Length == 0)
                {
                    notes.Add($"{set.label}：標準のみ（本体の色替えは未対応）");
                    continue;
                }
                var options = new System.Collections.Generic.List<(string, string)> { (SkinCatalog.StandardId, SkinCatalog.StandardLabel) };
                foreach (var skin in set.skins)
                    options.Add((skin.skinId, skin.label));
                string weaponId = set.weaponId, weaponLabel = set.label;
                Choices("WeaponSkin_" + weaponId, () => $"武器：{weaponLabel}　現在：{skins.WeaponSkinLabel(weaponId, SkinSelection.GetWeaponSkinId(weaponId))}",
                    options.ToArray(), id => SkinSelection.SetWeaponSkinId(weaponId, id));
            }
            var playerOptions = new System.Collections.Generic.List<(string, string)> { (SkinCatalog.StandardId, SkinCatalog.StandardLabel) };
            foreach (var skin in skins.playerSkins)
                playerOptions.Add((skin.skinId, skin.label));
            Choices("PlayerSkin", () => $"手・腕（全武器共通）　現在：{skins.PlayerSkinLabel(SkinSelection.PlayerSkinId)}", playerOptions.ToArray(),
                id => SkinSelection.PlayerSkinId = id);
            if (notes.Count > 0)
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("SkinNotes", choices), new Vector2(0.5f, 1f), new Vector2(0f, y + 10f), new Vector2(780f, 34f)),
                    string.Join("　", notes), 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f));
            string[] ids = weaponIds.ToArray();
            UiBuild.Button(choices, "SkinResetButton", "すべて標準に戻す", new Vector2(0.5f, 0f), new Vector2(-160f, 0f), new Vector2(300f, 70f),
                () => { SkinSelection.ResetToStandard(ids); RefreshSkinSettings(); }, 28);
            UiBuild.Button(choices, "SkinBackButton", "戻る", new Vector2(0.5f, 0f), new Vector2(160f, 0f), new Vector2(300f, 70f), CloseSkinSettings, 28);
            skinPanel.SetActive(false);
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
            dragAimText.text = $"ADSボタンで照準操作（タッチ）　現在：{(ControlSettings.TouchAdsDragAim ? "ON" : "OFF")}";
            fireModeText.text = $"FIREの動作（タッチ）　現在：{ControlSettings.Label(ControlSettings.TouchFire)}";
            int transparency = ControlSettings.TouchControlTransparency;
            transparencyText.text = $"操作ボタンの透過率：{transparency}%　（100%でも操作は有効）";
            transparencySlider.SetValueWithoutNotify(transparency);
        }

        void BuildControlSettings()
        {
            controlsPanel = UiBuild.Stretch(UiBuild.Rect("ControlSettingsPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)controlsPanel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", controlsPanel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 1000f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Title", window), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(840f, 60f)),
                "設定 → 操作設定", 40, TextAnchor.MiddleCenter);
            UiBuild.Text(UiBuild.Place(UiBuild.Rect("Hint", window), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(840f, 64f)),
                "ADS 長押し：押している間だけ／切り替え：押すたびにON・OFF　　ADS連動射撃：FIREで覗いてから撃つ", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));

            float y = -170f;
            Text Row(string prefix, string left, Action onLeft, string right, Action onRight)
            {
                var label = UiBuild.Text(UiBuild.Place(UiBuild.Rect(prefix + "Label", window), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 40f)),
                    "", 26, TextAnchor.MiddleCenter);
                UiBuild.Button(window, prefix + "Left", left, new Vector2(0.5f, 1f), new Vector2(-150f, y - 50f), new Vector2(280f, 70f),
                    () => { onLeft(); RefreshControlSettings(); }, 26);
                UiBuild.Button(window, prefix + "Right", right, new Vector2(0.5f, 1f), new Vector2(150f, y - 50f), new Vector2(280f, 70f),
                    () => { onRight(); RefreshControlSettings(); }, 26);
                y -= 150f;
                return label;
            }
            touchAdsText = Row("TouchAds", "長押し", () => SetAdsMode(InputMode.Touch, AdsMode.Hold), "切り替え", () => SetAdsMode(InputMode.Touch, AdsMode.Toggle));
            dragAimText = Row("DragAim", "ON", () => ControlSettings.TouchAdsDragAim = true, "OFF", () => ControlSettings.TouchAdsDragAim = false);
            fireModeText = Row("FireMode", "通常射撃", () => ControlSettings.TouchFire = TouchFireMode.Normal,
                "ADS連動射撃", () => ControlSettings.TouchFire = TouchFireMode.AdsLinked);
            // Transparency of the on-screen controls: applied as it changes, saved on the device.
            transparencyText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("TransparencyLabel", window), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 40f)),
                "", 26, TextAnchor.MiddleCenter);
            transparencySlider = UiBuild.Slider(window, "TransparencySlider", new Vector2(0.5f, 1f), new Vector2(0f, y - 54f), new Vector2(640f, 56f), 0f, 100f,
                v => { ControlSettings.TouchControlTransparency = Mathf.RoundToInt(v); RefreshControlSettings(); });
            y -= 150f;
            // The PC setting is only meaningful where a mouse can be used (Editor / desktop).
            desktopAdsText = Row("DesktopAds", "長押し", () => SetAdsMode(InputMode.Desktop, AdsMode.Hold), "切り替え", () => SetAdsMode(InputMode.Desktop, AdsMode.Toggle));
            if (Application.isMobilePlatform)
            {
                foreach (var name in new[] { "DesktopAdsLabel", "DesktopAdsLeft", "DesktopAdsRight" })
                    window.Find(name).gameObject.SetActive(false);
            }
            UiBuild.Button(window, "BackButton", "戻る", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(300f, 70f), CloseControlSettings, 28);
            controlsPanel.SetActive(false);
        }

        void Build()
        {
            layer = UiBuild.Stretch(UiBuild.Rect("MenuLayer", transform));
            layer.SetAsLastSibling();
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", layer));
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Size / position shared with the layout editor, which keeps this spot (and a small margin) free.
            bool iconMenu = icons != null && icons.menu != null;
            MenuButton = UiBuild.Button(safe, "MenuButton", iconMenu ? "" : "メニュー", new Vector2(0.5f, 1f), new Vector2(0f, -TouchLayout.MenuButtonTopOffset),
                TouchLayout.MenuButtonSize, () => { if (IsOpen) Close(); else Open(); }, 28);
            if (iconMenu)
                UiBuild.Icon((RectTransform)MenuButton.transform, icons.menu, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));

            panel = UiBuild.Stretch(UiBuild.Rect("MenuPanel", layer)).gameObject;
            UiBuild.Image((RectTransform)panel.transform, new Color(0f, 0f, 0f, 0.6f), true);
            var window = UiBuild.Place(UiBuild.Rect("Window", panel.transform), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 940f));
            UiBuild.Image(window, new Color(0.08f, 0.09f, 0.12f, 0.95f), true);
            if (icons != null && icons.titleLogo != null)
                UiBuild.Icon(window, icons.titleLogo, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(300f, 100f)).name = "TitleLogo";
            else
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
            var header = UiBuild.Text(UiBuild.Place(UiBuild.Rect("SettingsHeader", window), new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(480f, 40f)),
                "設定", 26, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            if (icons != null && icons.settings != null)
            {
                header.rectTransform.offsetMin += new Vector2(46f, 0f);
                UiBuild.Icon((RectTransform)window, icons.settings, new Vector2(0.5f, 1f), new Vector2(-222f, y - 6f), new Vector2(36f, 36f)).color = new Color(1f, 1f, 1f, 0.6f);
            }
            y -= 52f;
            Add("ControlSettingsButton", "操作設定", OpenControlSettings);
            if (icons != null && icons.settings != null)
                UiBuild.Icon((RectTransform)window.Find("ControlSettingsButton"), icons.settings, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(44f, 44f));
            Add("LayoutButton", "操作レイアウト", OpenLayoutEditor);
            if (skins != null)
                Add("SkinButton", "装備・スキン（テスト用）", OpenSkinSettings);
            panel.SetActive(false);
            BuildControlSettings();
            if (skins != null)
            {
                BuildSkinSettings();
                if (EquipmentScreenDiagnostics.Enabled)
                    gameObject.AddComponent<EquipmentScreenDiagnostics>().Init(this);
            }

            layoutEditor = gameObject.AddComponent<TouchLayoutEditor>();
            layoutEditor.Init(input, (RectTransform)transform);
        }
    }
}
