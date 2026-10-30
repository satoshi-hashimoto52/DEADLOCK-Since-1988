using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). Weapon balance
    /// lab of the firing range, opened from the menu ("開発 武器調整"). Top: weapon drop-down and the weapon table
    /// (weapons are rows, parameters columns; edited cells show "official→B"). Below, tabs for the selected weapon:
    /// performance values, recoil (the A / B editor rows shared with the in-game panel, preview of the kick pattern),
    /// damage falloff (control points, graph, damage by distance) and test / save. B is the edited copy of each weapon,
    /// A what its test runs with (the official values without a test); only "Bを試射に適用" changes A, by putting B on a
    /// runtime clone of the weapon definition; "試射を終了" goes back to the weapon asset. Weapon assets and game
    /// settings are never written. While open, the player's input is blocked (nothing reaches shooting or looking, and
    /// nothing stays pressed after closing).
    /// </summary>
    public sealed class WeaponBalanceLab : MonoBehaviour
    {
        /// <summary>EditorPrefs key of the Editor switch (DEADLOCK/Dev/Weapon Balance Lab).</summary>
        public const string EditorToggleKey = "DEADLOCK.WeaponBalanceLab";
        public const string InputBlockReason = "weaponLab";
        /// <summary>Distances (m) of the damage-by-distance preview.</summary>
        public static readonly float[] PreviewDistances = { 10f, 25f, 40f, 60f, 80f };
        public enum Tab { Performance, Recoil, Falloff, Test }

        static readonly Color Bg = new Color(0.05f, 0.055f, 0.07f, 0.97f);
        static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.22f, 1f);
        static readonly Color Chosen = new Color(0.55f, 0.38f, 0.1f, 1f);
        static readonly Color EditedColor = new Color(1f, 0.75f, 0.3f, 1f);

        GunController gun;
        DevGunSelection devGun;
        ProjectileSystem projectiles;
        PlayerInputRouter input;
        GameMenu menu;
        WeaponBalanceStore store;

        readonly Dictionary<string, WeaponDefinition> originals = new Dictionary<string, WeaponDefinition>();
        readonly Dictionary<string, WeaponBalanceProfile> current = new Dictionary<string, WeaponBalanceProfile>();
        readonly Dictionary<string, WeaponBalanceProfile> edited = new Dictionary<string, WeaponBalanceProfile>();
        // A per weapon under test: its clone (what the gun fires while that weapon is in hand) and the values it was
        // given (B at the moment of applying). Kept while the lab switches weapons; a switch from elsewhere ends them.
        readonly Dictionary<string, (WeaponDefinition clone, WeaponBalanceProfile profile)> tests =
            new Dictionary<string, (WeaponDefinition, WeaponBalanceProfile)>();
        bool switching; // the lab itself is swapping the gun
        Deadlock.Player.FirstPersonMotor motor;
        Vector2 aimReference; // yaw, pitch the aim reset goes back to
        WeaponRecoilEditor recoilEditor;
        WeaponPicker picker;
        readonly Dictionary<string, Button> recoilButtons = new Dictionary<string, Button>();
        int selectedPoint;

        RectTransform root, window, tableContent, tabArea;
        readonly Dictionary<Tab, RectTransform> tabPages = new Dictionary<Tab, RectTransform>();
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        readonly Dictionary<string, Text> valueTexts = new Dictionary<string, Text>();
        readonly List<GameObject> tableCells = new List<GameObject>();
        readonly List<GameObject> pointRows = new List<GameObject>();
        readonly List<GameObject> graphParts = new List<GameObject>();
        readonly List<GameObject> patternDots = new List<GameObject>();
        RectTransform pointList, graph, pattern;
        Text statusText, recoilText, recoilError, falloffText, falloffError, testText, messageText;
        Slider multiplierSlider;
        bool refreshing;
        RectTransform testHud;
        Text testHudText;
        int lastShotIndex = -1;

        void OnFired() => lastShotIndex = gun.ShotIndex - 1;

        /// <summary>The small read-out shown over the game during a test session (null text when not testing).</summary>
        public Text TestHudText => testHudText;
        public bool TestHudVisible => testHud != null && testHud.gameObject.activeSelf;

        public bool IsOpen => root != null && root.gameObject.activeSelf;
        public string SelectedId { get; private set; }
        public Tab CurrentTab { get; private set; } = Tab.Performance;
        public WeaponBalanceStore Store => store;
        /// <summary>The clone with the test values the gun in hand fires with (null: the official weapon).</summary>
        public WeaponDefinition AppliedClone => tests.TryGetValue(gun.Definition.weaponId, out var t) && t.clone == gun.Definition ? t.clone : null;
        public bool Testing => AppliedClone != null;
        /// <summary>This weapon has test values (A ≠ official until the test ends), in hand or not.</summary>
        public bool IsTesting(string weaponId) => tests.ContainsKey(weaponId);
        /// <summary>Weapon id of the gun in hand (a test clone counts as its weapon).</summary>
        public string EquippedId => gun.Definition.weaponId;
        public WeaponBalanceProfile Edited(string weaponId) => edited[weaponId];
        public WeaponBalanceProfile Current(string weaponId) => current[weaponId];
        public int SelectedPoint => selectedPoint;
        public Button OpenButton { get; private set; }
        public Text Message => messageText;
        public string LastHitText { get; private set; } = "";
        public RectTransform Window => window;
        /// <summary>The expected-recoil pattern of the recoil tab (fixed scale).</summary>
        public RectTransform Pattern => pattern;
        public RectTransform Graph => graph;
        public int GraphSegmentCount { get; private set; }
        public Slider PitchSlider => recoilEditor.SliderOf(RecoilParam.Pitch);
        public WeaponRecoilEditor RecoilEditor => recoilEditor;
        public WeaponPicker Picker => picker;

        /// <summary>Registered weapons (the scene's gun choices), in order: the drop-downs are made from this.</summary>
        public IReadOnlyList<string> WeaponIds => new List<string>(originals.Keys);
        /// <summary>Name shown in the drop-downs: the definition's display name and type.</summary>
        public string WeaponLabel(string weaponId) =>
            originals.TryGetValue(weaponId, out var d) ? $"{d.displayName}（{WeaponBalanceProfile.TypeOf(weaponId)}）" : weaponId;

        /// <summary>
        /// A: the values the test of this weapon runs with (read only). Without a test of it, the official values. Editing
        /// B never changes it; "Bを試射に適用" does.
        /// </summary>
        public WeaponBalanceProfile Applied(string weaponId) =>
            tests.TryGetValue(weaponId, out var t) ? t.profile : current[weaponId];
        /// <summary>The earlier recoil's recovery speed (the scene's, °/s).</summary>
        public float LegacyRecoverySpeed => motor != null ? motor.RecoilRecovery : 9f;
        /// <summary>Recoil comparison items where A and B of the selected weapon differ.</summary>
        public int RecoilDiffCount => RecoilParams.DiffCount(Applied(SelectedId).recoil, E.recoil, LegacyRecoverySpeed);
        /// <summary>Sliders in fine mode (small window, small step), shared by both screens.</summary>
        public bool FineAdjust { get; private set; }
        public bool MessageIsError { get; private set; }
        public Vector2 AimReference => aimReference;
        public Slider MultiplierSlider => multiplierSlider;

        /// <summary>A named control of the lab window (tests press them by their screen position).</summary>
        public Button ControlButton(string name)
        {
            foreach (var b in window.GetComponentsInChildren<Button>(true))
                if (b.name == name)
                    return b;
            return null;
        }

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
            var go = new GameObject("WeaponBalanceLabBoot");
            DontDestroyOnLoad(go);
            go.AddComponent<Starter>();
        }

        /// <summary>Puts the lab on every firing range that loads.</summary>
        sealed class Starter : MonoBehaviour
        {
            void Awake()
            {
                SceneManager.sceneLoaded += (s, m) => { if (s.name == GameScenes.FiringRange) StartCoroutine(Attach()); };
                if (SceneManager.GetActiveScene().name == GameScenes.FiringRange)
                    StartCoroutine(Attach());
            }

            static IEnumerator Attach()
            {
                yield return null;
                var g = FindAnyObjectByType<GunController>();
                if (g != null && FindAnyObjectByType<WeaponBalanceLab>() == null)
                    Create(g, null);
            }
        }

        /// <summary>Builds the lab for this gun (hidden) and the menu entry that opens it.</summary>
        public static WeaponBalanceLab Create(GunController gun, WeaponBalanceStore store)
        {
            var canvasGo = new GameObject("WeaponBalanceLabCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasGo, gun.gameObject.scene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900; // above the HUD, the touch controls and the menu
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1672f, 941f);
            scaler.matchWidthOrHeight = 1f;
            var lab = canvasGo.AddComponent<WeaponBalanceLab>();
            lab.Init(gun, store ?? new WeaponBalanceStore());
            return lab;
        }

        void Init(GunController g, WeaponBalanceStore s)
        {
            gun = g;
            store = s;
            input = gun.GetComponent<PlayerInputRouter>();
            devGun = gun.GetComponent<DevGunSelection>();
            projectiles = FindAnyObjectByType<ProjectileSystem>();
            menu = FindAnyObjectByType<GameMenu>();
            motor = gun.GetComponent<Deadlock.Player.FirstPersonMotor>();
            if (motor != null)
                aimReference = new Vector2(motor.Yaw, motor.Pitch); // the spawn direction (facing the targets)
            var options = devGun != null && devGun.Options.Count > 0 ? devGun.Options : (IReadOnlyList<WeaponDefinition>)new[] { gun.Definition };
            foreach (var d in options)
            {
                if (d == null || originals.ContainsKey(d.weaponId))
                    continue;
                originals[d.weaponId] = d;
                current[d.weaponId] = WeaponBalanceProfile.FromDefinition(d);
                edited[d.weaponId] = current[d.weaponId].Clone();
            }
            SelectedId = OriginalOf(gun.Definition).weaponId;
            if (projectiles != null)
                projectiles.Hit += OnHit;
            gun.DefinitionChanged += OnDefinitionChanged;
            gun.Fired += OnFired;
            Build();
            AddMenuEntry();
            root.gameObject.SetActive(false);
            Quick = WeaponBalanceQuickPanel.Create(this, gun, (RectTransform)transform);
        }

        void OnDestroy()
        {
            if (projectiles != null)
                projectiles.Hit -= OnHit;
            if (gun != null)
            {
                gun.DefinitionChanged -= OnDefinitionChanged;
                gun.Fired -= OnFired;
            }
            if (IsOpen && input != null)
                input.Unblock(InputBlockReason);
            foreach (var t in tests.Values)
                Destroy(t.clone);
            tests.Clear();
        }

        WeaponDefinition OriginalOf(WeaponDefinition d) => originals.TryGetValue(d.weaponId, out var o) ? o : d;

        /// <summary>The menu's window gets "開発 武器調整" at its top right (opposite "ホームへ").</summary>
        void AddMenuEntry()
        {
            if (menu == null)
                return;
            var menuWindow = menu.transform.Find("MenuLayer/MenuPanel/Window") as RectTransform;
            if (menuWindow == null)
                return;
            // 140 wide in two lines: clear of the title logo (300 wide at the centre of the 620-wide window).
            OpenButton = UiBuild.Button(menuWindow, "WeaponLabButton", "開発\n武器調整", new Vector2(1f, 1f), new Vector2(-10f, -14f), new Vector2(140f, 64f), () =>
            {
                menu.Close();
                Open();
            }, 19);
            ((RectTransform)OpenButton.transform).pivot = new Vector2(1f, 1f);
            ((Image)OpenButton.targetGraphic).color = new Color(0.4f, 0.1f, 0.1f, 0.95f);
        }

        public void Open()
        {
            if (IsOpen)
                return;
            input?.Block(InputBlockReason); // touches and keys stop reaching the player; released again on close
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            PlaceHeader();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            root.gameObject.SetActive(false);
            input?.Unblock(InputBlockReason);
        }

        // ------------------------------------------------------------------------------------------- building

        Button Make(RectTransform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, int font = 20)
        {
            var b = UiBuild.Button(parent, name, label, anchor, pos, size, onClick, font);
            ((Image)b.targetGraphic).color = ButtonColor;
            return b;
        }

        Text Label(RectTransform parent, string name, string text, Vector2 anchor, Vector2 pos, Vector2 size, int font = 18, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = UiBuild.Text(UiBuild.Place(UiBuild.Rect(name, parent), anchor, pos, size), text, font, align);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        /// <summary>
        /// Read-out over the game while a test session runs (development only): profile, direction, strength and the
        /// kick of the last shot. Display only (takes no touches), top left inside the safe area, under the HUD buttons.
        /// </summary>
        void BuildTestHud()
        {
            var safe = UiBuild.Stretch(UiBuild.Rect("TestHudSafeArea", transform));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            testHud = UiBuild.Place(UiBuild.Rect("TestHud", safe), new Vector2(0f, 1f), new Vector2(12f, -250f), new Vector2(470f, 110f)); // below the top-left HUD button
            testHud.pivot = new Vector2(0f, 1f);
            UiBuild.Image(testHud, new Color(0f, 0f, 0f, 0.55f), false);
            testHudText = UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Text", testHud)), "", 17, TextAnchor.UpperLeft, EditedColor);
            testHudText.rectTransform.offsetMin = new Vector2(10f, 6f);
            testHudText.rectTransform.offsetMax = new Vector2(-10f, -6f);
            testHud.gameObject.SetActive(false);
        }

        void Update()
        {
            if (testHud == null)
                return;
            bool show = Testing && !IsOpen && (Quick == null || !Quick.IsOpen);
            if (testHud.gameObject.activeSelf != show)
                testHud.gameObject.SetActive(show);
            if (!show)
                return;
            var appliedClone = AppliedClone;
            var r = appliedClone.recoilProfile;
            string dir = r != null && r.enabled ? RecoilCalculator.DirectionName(r) : "従来（上・左右ランダム）";
            string str = r != null && r.enabled ? (r.strength == RecoilStrength.Weak ? "弱" : r.strength == RecoilStrength.Strong ? "強" : "中") : "従来";
            testHudText.text = string.Format(CultureInfo.InvariantCulture,
                "開発：試射中 {0}（編集値）\n反動 {1}・{2}　ADS {3:0.00}\n最後の発射（{4}発目）：縦 {5:+0.00;-0.00;0.00}°　横 {6:+0.00;-0.00;0.00}°",
                appliedClone.weaponId, dir, str, gun.AdsBlend, Mathf.Max(0, lastShotIndex) + 1, gun.LastRecoil.x, gun.LastRecoil.y);
        }

        /// <summary>Room the recoil rows leave at the right of the recoil tab: the pattern (420) and its margins.</summary>
        public const float RecoilEditorRightInset = 16f + 420f + 8f;
        const float TitleWidth = 230f, CloseWidth = 150f, PickerMaxWidth = 360f;
        Text title;

        /// <summary>The title (top left) of the window.</summary>
        public Text Title => title;

        /// <summary>
        /// The FPS read-out (a development overlay drawn over everything at the top centre) in window units, x from the
        /// window's left: its text's extent with a margin; null when it is not shown.
        /// </summary>
        public (float xMin, float xMax, float yMax)? FpsSpan()
        {
            var monitor = FrameRateMonitor.Instance;
            var t = monitor != null && monitor.isActiveAndEnabled ? monitor.GetComponentInChildren<Text>() : null;
            if (t == null || !t.isActiveAndEnabled || string.IsNullOrEmpty(t.text))
                return null;
            var c = new Vector3[4];
            t.rectTransform.GetWorldCorners(c);
            float half = t.preferredWidth * 0.5f * t.rectTransform.lossyScale.x + 24f * window.lossyScale.x, mid = (c[0].x + c[2].x) * 0.5f;
            Vector2 a = window.InverseTransformPoint(new Vector3(mid - half, c[1].y)), b = window.InverseTransformPoint(new Vector3(mid + half, c[1].y - t.preferredHeight * t.rectTransform.lossyScale.y));
            var r = window.rect;
            return (a.x - r.xMin, b.x - r.xMin, r.yMax - b.y);
        }

        /// <summary>
        /// The weapon drop-down between the title and 閉じる, in the widest room clear of the FPS read-out (its name is cut
        /// with "…" when the room is narrow; the open list shows full names).
        /// </summary>
        void PlaceHeader()
        {
            float left = TitleWidth + 24f, right = window.rect.width - 10f - CloseWidth - 12f;
            var rooms = new List<(float a, float b)>();
            if (FpsSpan() is var (f0, f1, _))
            {
                rooms.Add((left, Mathf.Min(right, f0)));
                rooms.Add((Mathf.Max(left, f1), right));
            }
            else
                rooms.Add((left, right));
            var best = rooms[0];
            foreach (var room in rooms)
                if (room.b - room.a > best.b - best.a)
                    best = room;
            picker.Place(TouchUi.Span.L(best.a, Mathf.Clamp(best.b - best.a, 120f, PickerMaxWidth)), -6f, 52f);
        }

        void Build()
        {
            BuildTestHud();
            root = UiBuild.Stretch(UiBuild.Rect("WeaponLab", transform));
            UiBuild.Image(root, new Color(0f, 0f, 0f, 0.55f), true); // takes every touch outside the window too
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            window = UiBuild.Stretch(UiBuild.Rect("Window", safe));
            window.offsetMin = new Vector2(8f, 8f);
            window.offsetMax = new Vector2(-8f, -8f);
            UiBuild.Image(window, Bg, true);

            title = Label(window, "Title", "武器調整（開発用）\n<size=14>試験値は正式な武器に反映しない</size>", new Vector2(0f, 1f), new Vector2(16f, -4f), new Vector2(TitleWidth, 56f), 21);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.verticalOverflow = VerticalWrapMode.Truncate;
            var close = Make(window, "Close", "閉じる", new Vector2(1f, 1f), new Vector2(-10f, -6f), new Vector2(CloseWidth, 56f), Close, 22);
            ((RectTransform)close.transform).pivot = new Vector2(1f, 1f);
            picker = WeaponPicker.Build(this, window, "", TouchUi.Span.L(TitleWidth + 24f, PickerMaxWidth), -6f, 52f, 18, null, 420f);

            BuildTable();

            float x = 16f;
            foreach (var (tab, label) in new[] { (Tab.Performance, "性能"), (Tab.Recoil, "反動"), (Tab.Falloff, "距離減衰"), (Tab.Test, "試射・保存") })
            {
                var captured = tab;
                var b = Make(window, "Tab_" + tab, label, new Vector2(0f, 1f), new Vector2(x, -262f), new Vector2(150f, 58f), () => ShowTab(captured), 22);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
                tabButtons[tab] = b;
                x += 160f;
            }
            // Status and message: right of the tabs, up to the window's right edge (fits a 4:3 screen too).
            statusText = Label(window, "Status", "", new Vector2(0f, 1f), Vector2.zero, Vector2.zero, 16);
            statusText.rectTransform.anchorMax = new Vector2(1f, 1f);
            statusText.rectTransform.offsetMin = new Vector2(x + 10f, -262f - 28f);
            statusText.rectTransform.offsetMax = new Vector2(-12f, -262f);
            messageText = Label(window, "Message", "", new Vector2(0f, 1f), Vector2.zero, Vector2.zero, 16);
            messageText.rectTransform.anchorMax = new Vector2(1f, 1f);
            messageText.rectTransform.offsetMin = new Vector2(x + 10f, -262f - 60f);
            messageText.rectTransform.offsetMax = new Vector2(-12f, -262f - 28f);
            messageText.color = EditedColor;

            tabArea = UiBuild.Stretch(UiBuild.Rect("TabArea", window));
            tabArea.offsetMin = new Vector2(12f, 12f);
            tabArea.offsetMax = new Vector2(-12f, -330f);
            UiBuild.Image(tabArea, new Color(1f, 1f, 1f, 0.04f), false);
            BuildPerformance();
            BuildRecoil();
            BuildFalloff();
            BuildTest();
            ShowTab(Tab.Performance);
        }

        void BuildTable()
        {
            var view = UiBuild.Place(UiBuild.Rect("Table", window), new Vector2(0f, 1f), new Vector2(12f, -60f), Vector2.zero);
            view.anchorMax = new Vector2(1f, 1f);
            view.offsetMin = new Vector2(12f, -252f);
            view.offsetMax = new Vector2(-12f, -60f);
            UiBuild.Image(view, new Color(0f, 0f, 0f, 0.35f), true);
            view.gameObject.AddComponent<RectMask2D>();
            tableContent = UiBuild.Rect("Content", view);
            tableContent.anchorMin = tableContent.anchorMax = tableContent.pivot = new Vector2(0f, 1f);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = tableContent;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

        static float ColumnWidth(int i) => i == WeaponBalanceProfile.Columns.Length - 1 ? 420f : i == 14 ? 220f : i == 18 ? 190f : 150f; // last: falloff, 14: recoil direction

        void RefreshTable()
        {
            foreach (var go in tableCells)
                Destroy(go);
            tableCells.Clear();
            var cols = WeaponBalanceProfile.Columns;
            float total = 0f;
            for (int i = 0; i < cols.Length; i++)
                total += ColumnWidth(i);
            tableContent.sizeDelta = new Vector2(total, 192f);
            float x = 0f;
            for (int c = 0; c < cols.Length; c++)
            {
                float w = ColumnWidth(c);
                var head = UiBuild.Place(UiBuild.Rect("Head_" + c, tableContent), new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(w - 2f, 44f));
                head.pivot = new Vector2(0f, 1f);
                UiBuild.Image(head, new Color(0.2f, 0.22f, 0.28f, 1f), false);
                UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("T", head)), cols[c].Header, 17, TextAnchor.MiddleCenter);
                tableCells.Add(head.gameObject);
                int r = 0;
                foreach (var id in originals.Keys)
                {
                    string now = cols[c].Cell(current[id]), next = cols[c].Cell(edited[id]);
                    string capturedId = id;
                    var cell = UiBuild.Button(tableContent, $"Cell_{id}_{c}", now == next ? now : $"{now}→{next}", new Vector2(0f, 1f),
                        new Vector2(x, -46f - r * 72f), new Vector2(w - 2f, 70f), () => Select(capturedId), 16);
                    ((RectTransform)cell.transform).pivot = new Vector2(0f, 1f);
                    ((Image)cell.targetGraphic).color = id == SelectedId ? new Color(0.3f, 0.24f, 0.12f, 1f) : new Color(0.1f, 0.11f, 0.14f, 1f);
                    var t = cell.GetComponentInChildren<Text>();
                    t.horizontalOverflow = HorizontalWrapMode.Wrap;
                    if (now != next)
                        t.color = EditedColor;
                    tableCells.Add(cell.gameObject);
                    r++;
                }
                x += w;
            }
        }

        RectTransform Page(Tab tab)
        {
            var p = UiBuild.Stretch(UiBuild.Rect("Page_" + tab, tabArea));
            tabPages[tab] = p;
            return p;
        }

        /// <summary>A value with −/+ buttons; <paramref name="step"/> applied to the edited profile through <paramref name="change"/>.</summary>
        void Stepper(RectTransform page, string key, string label, Vector2 pos, Action<int> change)
        {
            Label(page, "L_" + key, label, new Vector2(0f, 1f), pos, new Vector2(230f, 64f), 20).rectTransform.pivot = new Vector2(0f, 1f);
            var minus = Make(page, "Minus_" + key, "－", new Vector2(0f, 1f), pos + new Vector2(235f, 0f), new Vector2(80f, 64f), () => { change(-1); Changed(); }, 28);
            ((RectTransform)minus.transform).pivot = new Vector2(0f, 1f);
            var value = Label(page, "V_" + key, "", new Vector2(0f, 1f), pos + new Vector2(320f, 0f), new Vector2(170f, 64f), 20, TextAnchor.MiddleCenter);
            value.rectTransform.pivot = new Vector2(0f, 1f);
            valueTexts[key] = value;
            var plus = Make(page, "Plus_" + key, "＋", new Vector2(0f, 1f), pos + new Vector2(495f, 0f), new Vector2(80f, 64f), () => { change(1); Changed(); }, 28);
            ((RectTransform)plus.transform).pivot = new Vector2(0f, 1f);
        }

        WeaponBalanceProfile E => edited[SelectedId];

        void BuildPerformance()
        {
            var p = Page(Tab.Performance);
            var items = new (string key, string label, Action<int> change)[]
            {
                ("damage", "基本威力", d => E.damage = Mathf.Max(0f, E.damage + d)),
                ("rpm", "RPM", d => E.rpm = Mathf.Clamp(E.rpm + d * 10f, 10f, 3000f)),
                ("automatic", "射撃方式", d => E.automatic = !E.automatic),
                ("mag", "装弾数", d => E.magazineSize = Mathf.Max(1, E.magazineSize + d)),
                ("startReserve", "開始予備弾", d => E.startReserve = Mathf.Clamp(E.startReserve + d * 10, 0, E.reserveMax)),
                ("reserveMax", "予備弾上限", d => { E.reserveMax = Mathf.Max(0, E.reserveMax + d * 10); E.startReserve = Mathf.Min(E.startReserve, E.reserveMax); }),
                ("reload", "リロード時間(秒)", d => E.reloadTime = Mathf.Max(0.05f, Round(E.reloadTime + d * 0.1f, 2))),
                ("speed", "弾速(m/s)", d => E.projectileSpeed = Mathf.Max(10f, E.projectileSpeed + d * 10f)),
                ("range", "最大射程(m)", d => E.maxRange = Mathf.Max(10f, E.maxRange + d * 10f)),
                ("hip", "腰だめ拡散(°)", d => E.hipSpreadDegrees = Mathf.Max(0f, Round(E.hipSpreadDegrees + d * 0.1f, 2))),
                ("ads", "ADS拡散(°)", d => E.adsSpreadDegrees = Mathf.Max(0f, Round(E.adsSpreadDegrees + d * 0.05f, 2))),
                ("adsTime", "ADS移行時間(秒)", d => E.adsTime = Mathf.Max(0.01f, Round(E.adsTime + d * 0.02f, 2))),
            };
            for (int i = 0; i < items.Length; i++)
                Stepper(p, items[i].key, items[i].label, new Vector2(16f + (i / 6) * 620f, -12f - (i % 6) * 76f), items[i].change);
            Label(p, "Note", "1回の押下：威力±1、RPM±10、予備弾±10、時間±0.1／0.02秒、弾速・射程±10、拡散±0.1／0.05°", new Vector2(0f, 0f), new Vector2(16f, 8f), new Vector2(1240f, 40f), 16)
                .rectTransform.pivot = Vector2.zero;
        }

        void BuildRecoil()
        {
            var p = Page(Tab.Recoil);
            // The same editor rows as the in-game panel: B (edited) with direction + slider + number, A (applied) beside.
            // Rows from the left up to the pattern (420 at the right): the sliders take the room left.
            recoilEditor = WeaponRecoilEditor.Build(this, p, new Vector2(16f, -8f), "", 50f, 19, recoilButtons, WeaponRecoilEditor.EditorLayout.Inline, RecoilEditorRightInset);
            float y = -8f - recoilEditor.Height - 8f;
            float bx = 16f;
            foreach (var (name, label, action) in new (string, string, Action)[]
            {
                ("RApply", "Bを試射に適用", ApplyToTest),
                ("RRevert", "BをAに戻す", RevertEditedToApplied),
                ("REnd", "試射を終了", EndTest),
                ("RAimReset", "照準リセット", ResetAim),
                ("RecoilLegacy", "Bの反動を\n正式値に戻す", ResetEditedRecoil),
            })
            {
                var b = Make(p, name, label, new Vector2(0f, 1f), new Vector2(bx, y), new Vector2(148f, 56f), action, 18);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
                bx += 152f;
            }
            recoilError = Label(p, "RecoilError", "", new Vector2(0f, 1f), new Vector2(16f, y - 60f), new Vector2(756f, 26f), 15);
            recoilError.rectTransform.pivot = new Vector2(0f, 1f);
            recoilError.color = new Color(1f, 0.45f, 0.4f, 1f);
            recoilText = Label(p, "RecoilPreview", "", new Vector2(0f, 1f), new Vector2(16f, y - 84f), new Vector2(756f, 70f), 14);
            recoilText.rectTransform.pivot = new Vector2(0f, 1f);
            recoilText.verticalOverflow = VerticalWrapMode.Truncate;

            Label(p, "PatternTitle", "予想（1マス2°、8発、B）灰＝A 橙＝腰だめ 水色＝ADS（累積・回復なし）\n緑＝視点の動き（腰だめ・回復込み、各発の直後と停止1秒後）", new Vector2(1f, 1f), new Vector2(-16f, -2f), new Vector2(420f, 50f), 14).rectTransform.pivot = new Vector2(1f, 1f);
            pattern = UiBuild.Place(UiBuild.Rect("Pattern", p), new Vector2(1f, 1f), new Vector2(-16f, -54f), new Vector2(420f, 420f));
            UiBuild.Image(pattern, new Color(0f, 0f, 0f, 0.5f), false);
            // Grid: a line every 2° (fixed scale), the centre lines brighter.
            for (int g = -10; g <= 10; g++)
            {
                float a = g == 0 ? 0.35f : 0.08f;
                var hLine = UiBuild.Place(UiBuild.Rect("GridH", pattern), new Vector2(0.5f, 0.5f), new Vector2(0f, g * 2f * PreviewPixelsPerDegree), new Vector2(420f, 1f));
                UiBuild.Image(hLine, new Color(1f, 1f, 1f, a), false);
                var vLine = UiBuild.Place(UiBuild.Rect("GridV", pattern), new Vector2(0.5f, 0.5f), new Vector2(g * 2f * PreviewPixelsPerDegree, 0f), new Vector2(1f, 420f));
                UiBuild.Image(vLine, new Color(1f, 1f, 1f, a), false);
            }
            foreach (var (txt, pos) in new[] { ("上 +20°", new Vector2(0f, 196f)), ("下 −20°", new Vector2(0f, -196f)), ("右 +20°", new Vector2(180f, 10f)), ("左 −20°", new Vector2(-180f, 10f)) })
                UiBuild.Text(UiBuild.Place(UiBuild.Rect("GridLabel", pattern), new Vector2(0.5f, 0.5f), pos, new Vector2(90f, 22f)), txt, 14, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
        }

        // ------------------------------------------------------------------------------------------- recoil editing
        // Shared by the full lab and the in-game panel (same edited profile).

        /// <summary>Raised after the edited profile of any weapon changes (from either screen).</summary>
        public event Action EditedChanged;
        /// <summary>Raised after every refresh of the lab's state (edits, test start / end, load, reset).</summary>
        public event Action Refreshed;
        /// <summary>The compact in-game panel (same edited profile).</summary>
        public WeaponBalanceQuickPanel Quick { get; private set; }

        void EnableRecoil()
        {
            if (!E.recoil.enabled)
            {
                E.recoil.enabled = true;
                if (!E.recoil.IsValid(out _))
                    E.recoil.SetDirections(RecoilVertical.Up, RecoilHorizontal.None);
            }
        }

        public void SetVertical(RecoilVertical v)
        {
            EnableRecoil();
            if (!E.recoil.SetDirections(v, E.recoil.horizontal))
            {
                Refuse("上下と左右の両方を「なし」にはできません（反動の向きがなくなるため）");
                return;
            }
            Changed();
        }

        /// <summary>Left / right / none (turns the alternating mode off).</summary>
        public void SetHorizontal(RecoilHorizontal h)
        {
            EnableRecoil();
            if (!E.recoil.SetDirections(E.recoil.vertical, h))
            {
                Refuse("上下と左右の両方を「なし」にはできません（反動の向きがなくなるため）");
                return;
            }
            Changed();
        }

        /// <summary>Alternating left / right mode on or off (off: no sideways kick, or right when there is no up / down).</summary>
        public void SetAlternating(bool on)
        {
            EnableRecoil();
            var r = E.recoil;
            if (on)
                r.SetDirections(r.vertical, RecoilHorizontal.Alternating);
            else
                r.SetDirections(r.vertical, r.vertical == RecoilVertical.None ? RecoilHorizontal.Right : RecoilHorizontal.None);
            Changed();
        }

        public void SetStrength(RecoilStrength s)
        {
            EnableRecoil();
            E.recoil.strength = s;
            Changed();
        }

        /// <summary>
        /// Sets one recoil number of B (slider, typed number, test). Outside its range, or an angle whose direction is
        /// "なし", it is refused with the reason (nothing changes). A only changes through "Bを試射に適用".
        /// </summary>
        public bool TrySetValue(RecoilParam p, float v)
        {
            if (!RecoilParams.InRange(p, v))
            {
                Refuse($"{RecoilParams.Label(p)}は {RecoilParams.Shown(p, RecoilParams.Min(p))}〜{RecoilParams.Shown(p, RecoilParams.Max(p))} の範囲で入力してください");
                return false;
            }
            var r = E.recoil;
            if (r.enabled && (p == RecoilParam.Pitch && r.vertical == RecoilVertical.None || p == RecoilParam.Yaw && r.horizontal == RecoilHorizontal.None))
            {
                Refuse($"方向が「なし」の間は{RecoilParams.Label(p)}を変えられません（値は保持しています）");
                return false;
            }
            EnableRecoil();
            RecoilParams.Set(E.recoil, p, Mathf.Clamp((float)Math.Round(v, 3), RecoilParams.Min(p), RecoilParams.Max(p)));
            Changed();
            return true;
        }

        /// <summary>One step (coarse or fine) up or down.</summary>
        public void Step(RecoilParam p, int direction) =>
            TrySetValue(p, Mathf.Clamp((float)Math.Round(RecoilParams.Get(E.recoil, p) + direction * RecoilParams.Step(p, FineAdjust), 3), RecoilParams.Min(p), RecoilParams.Max(p)));

        public void SetFineAdjust(bool fine)
        {
            FineAdjust = fine;
            Refresh();
            EditedChanged?.Invoke();
        }

        /// <summary>A typed text that is not a number.</summary>
        public void RefuseInput(string why) => Refuse(why);

        void Refuse(string why)
        {
            messageText.text = why;
            messageText.color = new Color(1f, 0.45f, 0.4f, 1f);
            MessageIsError = true;
            Refresh();
            EditedChanged?.Invoke();
        }

        void Say(string text)
        {
            messageText.text = text;
            messageText.color = EditedColor;
            MessageIsError = false;
        }

        /// <summary>A (the running test) and B match for the selected weapon.</summary>
        public bool AppliedMatchesEdited
        {
            get
            {
                if (!tests.TryGetValue(SelectedId, out var t))
                    return false;
                return WeaponBalanceProfile.Diff(t.profile, E).Count == 0;
            }
        }

        /// <summary>B back to A (the applied values; without a test, the official ones). Only B changes.</summary>
        public void RevertEditedToApplied()
        {
            edited[SelectedId] = Applied(SelectedId).Clone();
            selectedPoint = 0;
            Say("BをA（適用中の値）に戻しました");
            Refresh();
            EditedChanged?.Invoke();
        }

        /// <summary>Only B's recoil back to the official values.</summary>
        void ResetEditedRecoil()
        {
            E.recoil = current[SelectedId].recoil.Clone();
            Say("Bの反動を正式な武器の値に戻しました");
            Refresh();
            EditedChanged?.Invoke();
        }

        /// <summary>
        /// Aim reset for comparing shots: the view goes back to the reference direction, the kick still to come and the
        /// recovery still running are cleared, and the next shot is the first of the pattern. Parameters, ADS, ammo and
        /// the gun are left as they are.
        /// </summary>
        public void ResetAim()
        {
            if (motor == null)
                return;
            motor.SetYawPitch(aimReference.x, aimReference.y);
            gun.ResetShotSequence();
            Say("照準を基準の向きに戻しました（反動の蓄積と回復中の分を消去）");
            Refresh();
        }

        /// <summary>The current view becomes the aim reset's direction.</summary>
        public void SetAimReference()
        {
            if (motor == null)
                return;
            aimReference = new Vector2(motor.Yaw, motor.Pitch);
            Say("今の向きを照準リセットの基準にしました");
            Refresh();
        }

        void BuildFalloff()
        {
            var p = Page(Tab.Falloff);
            Label(p, "PointsTitle", "制御点（距離 m, 倍率）", new Vector2(0f, 1f), new Vector2(16f, -6f), new Vector2(380f, 36f), 18).rectTransform.pivot = new Vector2(0f, 1f);
            pointList = UiBuild.Place(UiBuild.Rect("Points", p), new Vector2(0f, 1f), new Vector2(16f, -44f), new Vector2(300f, 470f));
            pointList.pivot = new Vector2(0f, 1f);
            Vector2 at = new Vector2(330f, -40f); // the column fits the tab area (about 530 high on an iPhone)
            Button B(string name, string label, Action a, Vector2 pos, float w = 190f)
            {
                var b = Make(p, name, label, new Vector2(0f, 1f), pos, new Vector2(w, 60f), () => { a(); Changed(); }, 19);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
                return b;
            }
            B("PointAdd", "制御点を追加", AddPoint, at);
            B("PointRemove", "制御点を削除", RemovePoint, at + new Vector2(0f, -64f));
            B("DistMinus", "距離 −5m", () => MovePoint(-5f, 0f), at + new Vector2(0f, -128f), 92f);
            B("DistPlus", "距離 ＋5m", () => MovePoint(5f, 0f), at + new Vector2(98f, -128f), 92f);
            B("MulMinus", "倍率 −0.05", () => MovePoint(0f, -0.05f), at + new Vector2(0f, -192f), 92f);
            B("MulPlus", "倍率 ＋0.05", () => MovePoint(0f, 0.05f), at + new Vector2(98f, -192f), 92f);
            Label(p, "L_mulSlider", "選択点の倍率", new Vector2(0f, 1f), at + new Vector2(0f, -256f), new Vector2(190f, 30f), 16).rectTransform.pivot = new Vector2(0f, 1f);
            multiplierSlider = UiBuild.Slider(p, "MultiplierSlider", new Vector2(0f, 1f), at + new Vector2(0f, -286f), new Vector2(190f, 60f), 0f, 100f, v =>
            {
                if (refreshing || E.falloff.IsEmpty)
                    return;
                var pt = E.falloff.points[selectedPoint];
                pt.multiplier = v / 100f;
                E.falloff.points[selectedPoint] = pt;
                Changed();
            });
            ((RectTransform)multiplierSlider.transform).pivot = new Vector2(0f, 1f);
            B("FalloffExample", "試験例を入れる", () => { E.falloff = WeaponBalanceProfile.ExampleFalloff(); selectedPoint = 0; }, at + new Vector2(0f, -354f));
            B("FalloffReset", "初期値に戻す", () => { E.falloff = current[SelectedId].falloff.Clone(); selectedPoint = 0; }, at + new Vector2(0f, -418f));

            graph = UiBuild.Place(UiBuild.Rect("Graph", p), new Vector2(0f, 1f), new Vector2(560f, -44f), new Vector2(560f, 280f));
            graph.pivot = new Vector2(0f, 1f);
            UiBuild.Image(graph, new Color(0f, 0f, 0f, 0.5f), false);
            Label(p, "GraphTitle", "距離（横）と倍率（縦 0〜1）", new Vector2(0f, 1f), new Vector2(560f, -6f), new Vector2(560f, 36f), 16).rectTransform.pivot = new Vector2(0f, 1f);
            falloffError = Label(p, "FalloffError", "", new Vector2(0f, 1f), new Vector2(560f, -330f), new Vector2(700f, 36f), 17);
            falloffError.rectTransform.pivot = new Vector2(0f, 1f);
            falloffError.color = new Color(1f, 0.45f, 0.4f, 1f);
            falloffText = Label(p, "FalloffTable", "", new Vector2(0f, 1f), new Vector2(560f, -370f), new Vector2(640f, 160f), 17);
            falloffText.rectTransform.pivot = new Vector2(0f, 1f);
        }

        void BuildTest()
        {
            var p = Page(Tab.Test);
            Button B(string name, string label, Action a, Vector2 pos)
            {
                var b = Make(p, name, label, new Vector2(0f, 1f), pos, new Vector2(300f, 66f), a, 21);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
                return b;
            }
            B("Apply", "Bを試射に適用", ApplyToTest, new Vector2(16f, -12f));
            B("EndTest", "試射を終了（正式値に戻す）", EndTest, new Vector2(326f, -12f));
            B("RevertToA", "BをAに戻す", RevertEditedToApplied, new Vector2(16f, -88f));
            B("ResetEdit", "Bを正式値に戻す", ResetEdited, new Vector2(326f, -88f));
            B("AimReset", "照準リセット", ResetAim, new Vector2(16f, -164f));
            B("AimReference", "今の向きを照準の基準に", SetAimReference, new Vector2(326f, -164f));
            B("Save", "Bを保存", Save, new Vector2(16f, -240f));
            B("Load", "保存データをBに読み込み", Load, new Vector2(326f, -240f));
            B("DeleteSaved", "保存データを削除", DeleteSaved, new Vector2(16f, -316f));
            testText = Label(p, "TestInfo", "", new Vector2(0f, 1f), new Vector2(640f, -12f), new Vector2(560f, 500f), 16);
            testText.rectTransform.pivot = new Vector2(0f, 1f);
            testText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ------------------------------------------------------------------------------------------- actions

        public void ShowTab(Tab tab)
        {
            CurrentTab = tab;
            foreach (var pair in tabPages)
                pair.Value.gameObject.SetActive(pair.Key == tab);
            foreach (var pair in tabButtons)
                ((Image)pair.Value.targetGraphic).color = pair.Key == tab ? Chosen : ButtonColor;
            Refresh();
        }

        /// <summary>
        /// Shows this weapon's A / B and puts it in hand (through <see cref="DevGunSelection"/>, as the menu's test choice
        /// does): with test values of its own, the gun fires them (its A); otherwise the official weapon. A fresh gun, as
        /// any switch.
        /// </summary>
        public void Select(string weaponId)
        {
            if (!originals.ContainsKey(weaponId))
                return;
            SelectedId = weaponId;
            selectedPoint = 0;
            picker?.Close();
            Equip(weaponId);
            Refresh();
            EditedChanged?.Invoke();
        }

        void Equip(string weaponId)
        {
            var target = tests.TryGetValue(weaponId, out var t) ? t.clone : originals[weaponId];
            if (gun.Definition == target)
                return;
            switching = true;
            try
            {
                if (devGun != null && OriginalOf(gun.Definition) != originals[weaponId])
                    devGun.Select(weaponId); // the session's test choice follows (the official gun first)
                gun.SetDefinition(target);
            }
            finally
            {
                switching = false;
            }
        }

        /// <summary>B changed (A stays: the test only takes B through "Bを試射に適用").</summary>
        void Changed()
        {
            Say("");
            Refresh();
            EditedChanged?.Invoke();
        }

        /// <summary>For the in-game panel: the edited values changed through the shared API.</summary>
        public void NotifyEdited() => Changed();

        void AddPoint()
        {
            var pts = E.falloff.points;
            if (pts.Count == 0)
                pts.Add(new FalloffPoint(0f, 1f));
            else
            {
                var last = pts[pts.Count - 1];
                pts.Add(new FalloffPoint(last.distance + 10f, last.multiplier));
            }
            selectedPoint = pts.Count - 1;
        }

        void RemovePoint()
        {
            var pts = E.falloff.points;
            if (pts.Count == 0)
                return;
            pts.RemoveAt(selectedPoint);
            selectedPoint = Mathf.Clamp(selectedPoint, 0, Mathf.Max(0, pts.Count - 1));
        }

        void MovePoint(float dDistance, float dMultiplier)
        {
            var pts = E.falloff.points;
            if (pts.Count == 0)
                return;
            var pt = pts[selectedPoint];
            pt.distance = Mathf.Max(0f, pt.distance + dDistance);
            pt.multiplier = Mathf.Clamp01(Round(pt.multiplier + dMultiplier, 2));
            pts[selectedPoint] = pt; // order is checked by the validation, not silently fixed
        }

        public void SelectPoint(int index)
        {
            selectedPoint = Mathf.Clamp(index, 0, Mathf.Max(0, E.falloff.points.Count - 1));
            Refresh();
        }

        /// <summary>
        /// Puts B on a runtime clone of the selected weapon (weapon assets untouched): it becomes A and the gun fires it from
        /// the next shot. The gun's state carries on — shot number, aim and recoil still recovering, ADS, rounds, the held
        /// trigger; only the aim reset clears them. A weapon not in hand is put in hand first (a fresh gun, as any switch).
        /// </summary>
        public void ApplyToTest()
        {
            var problems = E.Problems();
            if (problems.Count > 0)
            {
                Refuse("適用できません：" + string.Join("／", problems));
                return;
            }
            var original = originals[SelectedId];
            bool inHand = OriginalOf(gun.Definition) == original;
            if (!inHand)
                Equip(SelectedId);
            var clone = Instantiate(original);
            clone.name = original.name + " (lab)";
            E.ApplyTo(clone);
            tests.TryGetValue(SelectedId, out var old);
            tests[SelectedId] = (clone, E.Clone()); // A: a copy, so editing B afterwards leaves it as applied
            switching = true;
            try
            {
                gun.SetDefinitionKeepingState(clone);
            }
            finally
            {
                switching = false;
            }
            if (old.clone != null)
                Destroy(old.clone);
            Say(inHand
                ? $"{SelectedId} にBを適用しました（A＝B、次の発射から）。照準・反動・発射番号・弾数はそのまま"
                : $"{SelectedId} に持ち替えてBを適用しました（A＝B、試射中）");
            Refresh();
            EditedChanged?.Invoke();
        }

        /// <summary>The selected weapon's test ends: A is the official values again; in hand, the gun fires them (its state carries on).</summary>
        public void EndTest()
        {
            if (!tests.TryGetValue(SelectedId, out var t))
            {
                Say("試射中の編集値はありません");
                Refresh();
                return;
            }
            tests.Remove(SelectedId);
            if (gun.Definition == t.clone)
            {
                switching = true;
                try
                {
                    gun.SetDefinitionKeepingState(originals[SelectedId]);
                }
                finally
                {
                    switching = false;
                }
            }
            Destroy(t.clone);
            Say("試射を終了し、正式な武器の値に戻しました（Bは残しています）");
            Refresh();
            EditedChanged?.Invoke();
        }

        void OnDefinitionChanged()
        {
            if (!switching)
            {
                // Switched elsewhere (the menu's test choice): every test ends (the gun is an official one now), and the
                // lab shows the weapon in hand.
                foreach (var t in tests.Values)
                    if (t.clone != gun.Definition)
                        Destroy(t.clone);
                tests.Clear();
                if (originals.ContainsKey(EquippedId))
                    SelectedId = EquippedId;
                if (root != null)
                {
                    Refresh();
                    EditedChanged?.Invoke();
                }
                return;
            }
            if (root != null && IsOpen)
                Refresh();
        }

        public static string BaseCommit
        {
            get
            {
#if UNITY_EDITOR
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                    {
                        WorkingDirectory = System.IO.Directory.GetCurrentDirectory(),
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    string head = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(2000);
                    if (head.Length >= 7)
                        return head;
                }
                catch (Exception)
                {
                    // fall through
                }
#endif
                return "unknown (build " + Application.version + ")";
            }
        }

        public void Save()
        {
            store.Save(E, BaseCommit);
            Say("保存しました：" + store.PathOf(SelectedId));
            Refresh();
        }

        public void Load()
        {
            if (store.TryLoad(SelectedId, out var loaded, out string error))
            {
                edited[SelectedId] = loaded;
                selectedPoint = 0;
                Say($"Bに読み込みました（保存 {loaded.savedAt}、基準 {Short(loaded.baseCommit)}）");
            }
            else
                Refuse(error);
            Refresh();
        }

        public void ResetEdited()
        {
            edited[SelectedId] = current[SelectedId].Clone();
            selectedPoint = 0;
            Say("Bを正式な武器の値に戻しました（保存データは消していません）");
            Refresh();
            EditedChanged?.Invoke();
        }

        public void DeleteSaved()
        {
            Say(store.Delete(SelectedId) ? "保存データを削除しました" : "保存データはありません");
            Refresh();
        }

        void OnHit(ProjectileHit hit)
        {
            if (hit.Shooter != gun.gameObject || !hit.HitDamageable)
                return;
            LastHitText = string.Format(CultureInfo.InvariantCulture, "最後の命中：{0:0.0} m、倍率 {1:0.00}、ダメージ {2:0.0}（{3}）",
                hit.Distance, hit.DamageMultiplier, hit.Damage, gun.Definition.weaponId);
            if (IsOpen)
                Refresh();
        }

        static string Short(string commit) => string.IsNullOrEmpty(commit) ? "-" : commit.Length > 10 ? commit.Substring(0, 10) : commit;

        static float Round(float v, int digits) => (float)Math.Round(v, digits);

        static string F(float v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------------------------------- refresh

        public void Refresh()
        {
            if (root == null)
                return;
            refreshing = true;
            var e = E;
            var c = current[SelectedId];
            int diffs = WeaponBalanceProfile.Diff(Applied(SelectedId), e).Count;
            statusText.text = (IsTesting(SelectedId) ? $"試射中：{SelectedId}（A＝適用したB）" : $"装備：{EquippedId}（A＝正式値）")
                + (diffs == 0 ? "｜B＝A" : $"｜BはAと {diffs} 項目ちがう（未適用）");
            picker.Refresh();
            recoilEditor.Refresh();
            RefreshTable();

            void V(string key, string now, string next)
            {
                if (!valueTexts.TryGetValue(key, out var t))
                    return;
                t.text = now == next ? now : $"{now}→{next}";
                t.color = now == next ? Color.white : EditedColor;
            }
            V("damage", F(c.damage), F(e.damage));
            V("rpm", F(c.rpm, "0"), F(e.rpm, "0"));
            V("automatic", c.automatic ? "フルオート" : "セミオート", e.automatic ? "フルオート" : "セミオート");
            V("mag", c.magazineSize.ToString(), e.magazineSize.ToString());
            V("startReserve", c.startReserve.ToString(), e.startReserve.ToString());
            V("reserveMax", c.reserveMax.ToString(), e.reserveMax.ToString());
            V("reload", F(c.reloadTime), F(e.reloadTime));
            V("speed", F(c.projectileSpeed, "0"), F(e.projectileSpeed, "0"));
            V("range", F(c.maxRange, "0"), F(e.maxRange, "0"));
            V("hip", F(c.hipSpreadDegrees), F(e.hipSpreadDegrees));
            V("ads", F(c.adsSpreadDegrees), F(e.adsSpreadDegrees));
            V("adsTime", F(c.adsTime), F(e.adsTime));
            V("pitch", F(c.recoil.pitchDegrees), F(e.recoil.pitchDegrees));
            V("yaw", F(c.recoil.yawDegrees), F(e.recoil.yawDegrees));
            V("adsRecoil", F(c.recoil.adsMultiplier), F(e.recoil.adsMultiplier));
            V("recoveryAmount", F(c.recoil.recoveryFraction), F(e.recoil.recoveryFraction));
            V("recoverySpeed", F(c.recoil.recoverySpeed, "0.0"), F(e.recoil.recoverySpeed, "0.0"));

            RefreshRecoil(e);
            RefreshFalloff(e, c);
            RefreshTest(e, c);
            refreshing = false;
            Refreshed?.Invoke();
        }

        /// <summary>Pixels per degree of the recoil preview (fixed, so strength and base angle change the drawing's size).</summary>
        public const float PreviewPixelsPerDegree = 10f;
        /// <summary>Shots in the recoil preview.</summary>
        public const int PreviewShots = 8;

        /// <summary>
        /// Cumulative aim movement (x = yaw right, y = pitch up, degrees) after each of <see cref="PreviewShots"/> shots
        /// (index 0 = before the first), at <paramref name="ads"/>. A disabled profile is the earlier recoil: pitch only,
        /// its random sideways kick averages out.
        /// </summary>
        public static List<Vector2> PreviewPattern(RecoilProfile r, float ads)
        {
            var pts = new List<Vector2> { Vector2.zero };
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < PreviewShots; i++)
            {
                float kp, ky;
                if (r.enabled)
                {
                    if (!RecoilCalculator.TryCompute(r, i, ads, out kp, out ky))
                        break;
                }
                else
                {
                    kp = r.pitchDegrees * Mathf.Lerp(1f, r.adsMultiplier, ads);
                    ky = 0f;
                }
                sum += new Vector2(ky, kp);
                pts.Add(sum);
            }
            return pts;
        }

        /// <summary>Last drawn preview (edited, hip / ADS; current), cumulative degrees.</summary>
        public List<Vector2> PreviewHip { get; private set; } = new List<Vector2>();
        public List<Vector2> PreviewAds { get; private set; } = new List<Vector2>();
        public List<Vector2> PreviewCurrent { get; private set; } = new List<Vector2>();

        void DrawPattern(List<Vector2> pts, string prefix, Color color, float size)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 px = pts[i] * PreviewPixelsPerDegree;
                bool outside = Mathf.Abs(px.x) > 205f || Mathf.Abs(px.y) > 205f;
                px = new Vector2(Mathf.Clamp(px.x, -205f, 205f), Mathf.Clamp(px.y, -205f, 205f));
                var dot = UiBuild.Place(UiBuild.Rect(prefix + i, pattern), new Vector2(0.5f, 0.5f), px, outside ? new Vector2(size + 6f, 4f) : new Vector2(size, size));
                UiBuild.Image(dot, color, false);
                patternDots.Add(dot.gameObject);
            }
        }

        /// <summary>
        /// The aim's offset from where it was before firing, taken right after each of <see cref="PreviewShots"/> shots
        /// at <paramref name="fireInterval"/> and 1 s after the last one, with the motor's recovery rule: each kick moves
        /// the aim at once, its recovery share returns at the recovery speed (the earlier recoil: half, at
        /// <paramref name="legacySpeed"/>). x = yaw right, y = pitch up (degrees). Hip fire.
        /// </summary>
        public static List<Vector2> PreviewView(RecoilProfile r, float fireInterval, float legacySpeed)
        {
            const float dt = 1f / 60f;
            var pts = new List<Vector2> { Vector2.zero };
            bool data = r.enabled && r.IsValid(out _);
            if (r.enabled && !data)
                return pts;
            float fraction = data ? r.recoveryFraction : Deadlock.Player.FirstPersonMotor.DefaultRecoveryFraction;
            float speed = data ? r.recoverySpeed : legacySpeed;
            Vector2 view = Vector2.zero, remaining = Vector2.zero;
            int frames = Mathf.Max(1, Mathf.RoundToInt(fireInterval / dt));
            void Frames(int n)
            {
                for (int f = 0; f < n; f++)
                {
                    var rec = Vector2.MoveTowards(Vector2.zero, remaining, speed * dt);
                    remaining -= rec;
                    view -= rec;
                }
            }
            for (int i = 0; i < PreviewShots; i++)
            {
                Vector2 kick;
                if (data)
                {
                    RecoilCalculator.TryCompute(r, i, 0f, out float kp, out float ky);
                    kick = new Vector2(ky, kp);
                }
                else
                    kick = new Vector2(0f, r.pitchDegrees);
                view += kick;
                remaining += kick * fraction;
                Frames(1); // the frame the kick lands also recovers
                pts.Add(view);
                if (i < PreviewShots - 1)
                    Frames(frames - 1);
            }
            Frames(Mathf.RoundToInt(1f / dt));
            pts.Add(view);
            return pts;
        }

        public List<Vector2> PreviewViewPath { get; private set; } = new List<Vector2>();

        void RefreshRecoil(WeaponBalanceProfile e)
        {
            var r = e.recoil;
            foreach (var go in patternDots)
                Destroy(go);
            patternDots.Clear();
            // Fixed scale: A, the applied recoil (grey), B at the hip (orange), fully aimed (light blue), and the aim's
            // actual path with B's recovery (green).
            PreviewCurrent = PreviewPattern(Applied(SelectedId).recoil, 0f);
            bool valid = r.IsValid(out string err) || !r.enabled;
            PreviewHip = valid ? PreviewPattern(r, 0f) : new List<Vector2> { Vector2.zero };
            PreviewAds = valid ? PreviewPattern(r, 1f) : new List<Vector2> { Vector2.zero };
            PreviewViewPath = valid ? PreviewView(r, 60f / Mathf.Max(1f, e.rpm), LegacyRecoverySpeed) : new List<Vector2> { Vector2.zero };
            DrawPattern(PreviewCurrent, "Cur_", new Color(0.7f, 0.7f, 0.7f, 0.6f), 16f);
            DrawPattern(PreviewAds, "Ads_", new Color(0.4f, 0.85f, 1f, 1f), 11f);
            DrawPattern(PreviewHip, "Hip_", EditedColor, 11f);
            DrawPattern(PreviewViewPath, "View_", new Color(0.4f, 1f, 0.45f, 1f), 9f);
            var view = PreviewViewPath;
            float peak = 0f;
            foreach (var v in view)
                peak = Mathf.Max(peak, v.magnitude);
            var rest = view[view.Count - 1];
            if (!r.enabled)
            {
                recoilError.text = "";
                recoilText.text = $"B＝従来の反動：縦 {F(r.pitchDegrees)}°（上）、横 ±{F(r.yawDegrees)}° のランダム、ADS ×{F(r.adsMultiplier)}、回復は半分を {F(LegacyRecoverySpeed)}°/秒。\n"
                    + string.Format(CultureInfo.InvariantCulture, "視点（回復込み）：8発で最大 {0:0.0}°、停止1秒後 縦{1:+0.0;-0.0;0.0}°。方向・強度などを押すと、データで指定する反動に切り替わります。", peak, rest.y);
                return;
            }
            recoilError.text = valid ? "" : "不正（適用できません）：" + err;
            if (!valid)
            {
                recoilText.text = "";
                return;
            }
            RecoilCalculator.TryCompute(r, 0, 0f, out float hp, out float hy);
            RecoilCalculator.TryCompute(r, 1, 0f, out float hp2, out float hy2);
            RecoilCalculator.TryCompute(r, 0, 1f, out float ap, out float ay);
            var end = PreviewHip[PreviewHip.Count - 1];
            var cur = PreviewCurrent[PreviewCurrent.Count - 1];
            recoilText.text = $"B：{RecoilCalculator.DirectionName(r)}・{(r.strength == RecoilStrength.Weak ? "弱" : r.strength == RecoilStrength.Strong ? "強" : "中")}　回復：{F(r.recoveryFraction)} を {F(r.recoverySpeed)}°/秒\n"
                + string.Format(CultureInfo.InvariantCulture,
                    "1発目 腰だめ 縦{0:+0.00;-0.00;0.00}° 横{1:+0.00;-0.00;0.00}°／2発目 縦{2:+0.00;-0.00;0.00}° 横{3:+0.00;-0.00;0.00}°／ADS 縦{4:+0.00;-0.00;0.00}° 横{5:+0.00;-0.00;0.00}°\n" +
                    "8発の累積（回復なし）：縦{6:+0.0;-0.0;0.0}° 横{7:+0.0;-0.0;0.0}°（A 縦{8:+0.0;-0.0;0.0}°）\n" +
                    "視点（回復込み）：最大 {9:0.0}°、8発目の直後 縦{10:+0.0;-0.0;0.0}° 横{11:+0.0;-0.0;0.0}°、停止1秒後 縦{12:+0.0;-0.0;0.0}° 横{13:+0.0;-0.0;0.0}°",
                    hp, hy, hp2, hy2, ap, ay, end.y, end.x, cur.y, peak, view[view.Count - 2].y, view[view.Count - 2].x, rest.y, rest.x);
        }

        void RefreshFalloff(WeaponBalanceProfile e, WeaponBalanceProfile c)
        {
            foreach (var go in pointRows)
                Destroy(go);
            pointRows.Clear();
            var pts = e.falloff.points;
            for (int i = 0; i < pts.Count && i < 8; i++)
            {
                int captured = i;
                var row = UiBuild.Button(pointList, "Point_" + i, string.Format(CultureInfo.InvariantCulture, "{0}. {1:0} m　×{2:0.00}", i + 1, pts[i].distance, pts[i].multiplier),
                    new Vector2(0f, 1f), new Vector2(0f, -i * 58f), new Vector2(300f, 54f), () => SelectPoint(captured), 19);
                ((RectTransform)row.transform).pivot = new Vector2(0f, 1f);
                ((Image)row.targetGraphic).color = i == selectedPoint ? Chosen : ButtonColor;
                pointRows.Add(row.gameObject);
            }
            if (pts.Count == 0)
            {
                var none = UiBuild.Text(UiBuild.Place(UiBuild.Rect("NoPoints", pointList), new Vector2(0f, 1f), Vector2.zero, new Vector2(300f, 54f)), "制御点なし（倍率1.0）", 18, TextAnchor.MiddleLeft);
                none.rectTransform.pivot = new Vector2(0f, 1f);
                pointRows.Add(none.gameObject);
            }
            multiplierSlider.interactable = pts.Count > 0;
            if (pts.Count > 0)
                multiplierSlider.value = Mathf.Round(pts[selectedPoint].multiplier * 100f);
            bool valid = e.falloff.IsValid(out string err);
            falloffError.text = valid ? "" : "不正（適用できません）：" + err;
            DrawGraph(e);
            var lines = new List<string> { "距離　倍率（現行→編集）　実ダメージ（現行→編集）" };
            foreach (float d in PreviewDistances)
            {
                float cm = d > c.maxRange ? 0f : c.falloff.Evaluate(d);
                float em = d > e.maxRange ? 0f : (valid ? e.falloff.Evaluate(d) : 1f);
                string Range(float m, WeaponBalanceProfile p) => d > p.maxRange ? "射程外" : F(m, "0.00");
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,3:0} m　{1}→{2}　{3}→{4}", d, Range(cm, c), Range(em, e),
                    d > c.maxRange ? "-" : F(c.damage * cm, "0.0"), d > e.maxRange ? "-" : F(e.damage * em, "0.0")));
            }
            falloffText.text = string.Join("\n", lines);
        }

        void DrawGraph(WeaponBalanceProfile e)
        {
            foreach (var go in graphParts)
                Destroy(go);
            graphParts.Clear();
            GraphSegmentCount = 0;
            float w = graph.rect.width > 1f ? graph.rect.width : 560f, h = graph.rect.height > 1f ? graph.rect.height : 280f;
            float maxD = Mathf.Max(e.maxRange, 10f);
            foreach (var pt in e.falloff.points)
                maxD = Mathf.Max(maxD, pt.distance);
            Vector2 ToGraph(float d, float m) => new Vector2(Mathf.Clamp01(d / maxD) * w, Mathf.Clamp01(m) * h);
            // Grid labels: 0 / max distance, 0 / 1.
            foreach (var (txt, pos, anchor) in new[] { ("0m", new Vector2(4f, 4f), new Vector2(0f, 0f)), ($"{maxD:0}m", new Vector2(-4f, 4f), new Vector2(1f, 0f)), ("1.0", new Vector2(4f, -4f), new Vector2(0f, 1f)) })
            {
                var t = UiBuild.Text(UiBuild.Place(UiBuild.Rect("GraphLabel", graph), anchor, pos, new Vector2(90f, 24f)), txt, 15, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
                if (anchor.x > 0.5f)
                    t.alignment = TextAnchor.MiddleRight;
                graphParts.Add(t.gameObject);
            }
            var line = new List<Vector2>();
            if (e.falloff.IsEmpty || !e.falloff.IsValid(out _))
            {
                line.Add(ToGraph(0f, 1f));
                line.Add(ToGraph(maxD, 1f));
            }
            else
            {
                var pts = e.falloff.points;
                line.Add(ToGraph(0f, pts[0].multiplier));
                foreach (var pt in pts)
                    line.Add(ToGraph(pt.distance, pt.multiplier));
                line.Add(ToGraph(maxD, pts[pts.Count - 1].multiplier));
            }
            for (int i = 1; i < line.Count; i++)
            {
                Vector2 a = line[i - 1], b = line[i];
                float len = Vector2.Distance(a, b);
                if (len < 0.5f)
                    continue;
                var seg = UiBuild.Rect("Segment", graph);
                seg.anchorMin = seg.anchorMax = Vector2.zero;
                seg.pivot = new Vector2(0f, 0.5f);
                seg.anchoredPosition = a;
                seg.sizeDelta = new Vector2(len, 4f);
                seg.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                UiBuild.Image(seg, EditedColor, false);
                graphParts.Add(seg.gameObject);
                GraphSegmentCount++;
            }
            if (!e.falloff.IsEmpty && e.falloff.IsValid(out _))
                for (int i = 0; i < e.falloff.points.Count; i++)
                {
                    var pt = e.falloff.points[i];
                    var dot = UiBuild.Rect("PointDot", graph);
                    dot.anchorMin = dot.anchorMax = Vector2.zero;
                    dot.pivot = new Vector2(0.5f, 0.5f);
                    dot.anchoredPosition = ToGraph(pt.distance, pt.multiplier);
                    dot.sizeDelta = new Vector2(14f, 14f);
                    UiBuild.Image(dot, i == selectedPoint ? Color.white : new Color(1f, 0.5f, 0.2f, 1f), false);
                    graphParts.Add(dot.gameObject);
                }
        }

        void RefreshTest(WeaponBalanceProfile e, WeaponBalanceProfile c)
        {
            var a = Applied(SelectedId);
            var diff = WeaponBalanceProfile.Diff(a, e);
            var lines = new List<string>
            {
                IsTesting(SelectedId) ? $"試射中：{SelectedId}（A＝適用したB。武器の正式な値は変えていません）" : "試射なし（A＝正式な武器の値）",
                LastHitText,
                $"保存先：{store.PathOf(SelectedId)}",
                $"基準コミット：{Short(BaseCommit)}",
                diff.Count == 0 ? "AとBの差：なし" : "AとBの差（A → B、未適用）：",
            };
            foreach (var (column, from, to) in diff)
                lines.Add($"・{column}：{from} → {to}");
            var official = WeaponBalanceProfile.Diff(c, e);
            lines.Add(official.Count == 0 ? "正式値とBの差：なし" : $"正式値とBの差：{official.Count} 項目（表の「正式→B」）");
            var problems = e.Problems();
            if (problems.Count > 0)
                lines.Add("適用できない理由：" + string.Join("／", problems));
            testText.text = string.Join("\n", lines);
        }
    }
}
