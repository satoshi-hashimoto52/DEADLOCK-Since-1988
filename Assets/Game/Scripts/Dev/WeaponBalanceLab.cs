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
    /// lab of the firing range, opened from the menu ("開発 武器調整"). Top: the weapon table (weapons are rows,
    /// parameters columns; edited cells show "current→edited"). Below, tabs for the selected weapon: performance values,
    /// recoil (axis, up / down, left / right, strength, base angles, ADS factor, preview of the kick pattern), damage
    /// falloff (control points, graph, damage by distance) and test / save ("試射に適用" puts the edited values on a
    /// runtime clone of the weapon definition; "試射を終了" goes back to the weapon asset; save / load / reset of the trial
    /// file). Weapon assets and game settings are never written. While open, the player's input is blocked (nothing
    /// reaches shooting or looking, and nothing stays pressed after closing).
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
        WeaponDefinition appliedClone;
        int selectedPoint;

        RectTransform root, window, tableContent, tabArea;
        readonly Dictionary<Tab, RectTransform> tabPages = new Dictionary<Tab, RectTransform>();
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        readonly Dictionary<string, Text> valueTexts = new Dictionary<string, Text>();
        readonly Dictionary<string, Button> cycleButtons = new Dictionary<string, Button>();
        readonly List<GameObject> tableCells = new List<GameObject>();
        readonly List<GameObject> pointRows = new List<GameObject>();
        readonly List<GameObject> graphParts = new List<GameObject>();
        readonly List<GameObject> patternDots = new List<GameObject>();
        RectTransform pointList, graph, pattern;
        Text statusText, recoilText, recoilError, falloffText, falloffError, testText, messageText;
        Slider pitchSlider, multiplierSlider;
        bool refreshing;

        public bool IsOpen => root != null && root.gameObject.activeSelf;
        public string SelectedId { get; private set; }
        public Tab CurrentTab { get; private set; } = Tab.Performance;
        public WeaponBalanceStore Store => store;
        /// <summary>The weapon clone carrying the edited values while a test session runs (null: the official weapon).</summary>
        public WeaponDefinition AppliedClone => appliedClone;
        public bool Testing => appliedClone != null && gun.Definition == appliedClone;
        public WeaponBalanceProfile Edited(string weaponId) => edited[weaponId];
        public WeaponBalanceProfile Current(string weaponId) => current[weaponId];
        public int SelectedPoint => selectedPoint;
        public Button OpenButton { get; private set; }
        public Text Message => messageText;
        public string LastHitText { get; private set; } = "";
        public RectTransform Window => window;
        public RectTransform Graph => graph;
        public int GraphSegmentCount { get; private set; }
        public Slider PitchSlider => pitchSlider;
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
            Build();
            AddMenuEntry();
            root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (projectiles != null)
                projectiles.Hit -= OnHit;
            if (gun != null)
                gun.DefinitionChanged -= OnDefinitionChanged;
            if (IsOpen && input != null)
                input.Unblock(InputBlockReason);
            if (appliedClone != null)
                Destroy(appliedClone);
        }

        WeaponDefinition OriginalOf(WeaponDefinition d)
        {
            if (d == appliedClone && appliedClone != null && originals.TryGetValue(appliedClone.weaponId, out var o))
                return o;
            return d;
        }

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

        void Build()
        {
            root = UiBuild.Stretch(UiBuild.Rect("WeaponLab", transform));
            UiBuild.Image(root, new Color(0f, 0f, 0f, 0.55f), true); // takes every touch outside the window too
            var safe = UiBuild.Stretch(UiBuild.Rect("SafeArea", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            window = UiBuild.Stretch(UiBuild.Rect("Window", safe));
            window.offsetMin = new Vector2(8f, 8f);
            window.offsetMax = new Vector2(-8f, -8f);
            UiBuild.Image(window, Bg, true);

            Label(window, "Title", "武器調整（開発用・試験値は正式な武器に反映しない）", new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(900f, 44f), 24).rectTransform.pivot = new Vector2(0f, 1f);
            statusText = Label(window, "Status", "", new Vector2(0f, 1f), new Vector2(930f, -8f), new Vector2(500f, 44f), 18);
            statusText.rectTransform.pivot = new Vector2(0f, 1f);
            var close = Make(window, "Close", "閉じる", new Vector2(1f, 1f), new Vector2(-10f, -6f), new Vector2(150f, 56f), Close, 22);
            ((RectTransform)close.transform).pivot = new Vector2(1f, 1f);

            BuildTable();

            float x = 16f;
            foreach (var (tab, label) in new[] { (Tab.Performance, "性能"), (Tab.Recoil, "反動"), (Tab.Falloff, "距離減衰"), (Tab.Test, "試射・保存") })
            {
                var captured = tab;
                var b = Make(window, "Tab_" + tab, label, new Vector2(0f, 1f), new Vector2(x, -262f), new Vector2(190f, 58f), () => ShowTab(captured), 22);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
                tabButtons[tab] = b;
                x += 200f;
            }
            messageText = Label(window, "Message", "", new Vector2(0f, 1f), new Vector2(x + 10f, -262f), new Vector2(760f, 58f), 18);
            messageText.rectTransform.pivot = new Vector2(0f, 1f);
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

        static float ColumnWidth(int i) => i == 17 ? 420f : i == 14 ? 220f : 150f;

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

        void Cycle(RectTransform page, string key, string label, Vector2 pos, Action next)
        {
            Label(page, "L_" + key, label, new Vector2(0f, 1f), pos, new Vector2(150f, 64f), 20).rectTransform.pivot = new Vector2(0f, 1f);
            var b = Make(page, "Cycle_" + key, "", new Vector2(0f, 1f), pos + new Vector2(155f, 0f), new Vector2(250f, 64f), () =>
            {
                E.recoil.enabled = true; // choosing a direction turns the data-driven recoil on
                next();
                Changed();
            }, 22);
            ((RectTransform)b.transform).pivot = new Vector2(0f, 1f);
            cycleButtons[key] = b;
        }

        static T NextOf<T>(T value) where T : Enum
        {
            var all = (T[])Enum.GetValues(typeof(T));
            return all[(Array.IndexOf(all, value) + 1) % all.Length];
        }

        void BuildRecoil()
        {
            var p = Page(Tab.Recoil);
            Cycle(p, "axis", "反動軸", new Vector2(16f, -12f), () => E.recoil.axis = NextOf(E.recoil.axis));
            Cycle(p, "vertical", "上下", new Vector2(16f, -84f), () => E.recoil.vertical = NextOf(E.recoil.vertical));
            Cycle(p, "horizontal", "左右", new Vector2(16f, -156f), () => E.recoil.horizontal = NextOf(E.recoil.horizontal));
            Cycle(p, "strength", "強度", new Vector2(16f, -228f), () => E.recoil.strength = NextOf(E.recoil.strength));
            Stepper(p, "pitch", "縦の基準角(°)", new Vector2(430f, -12f), d => { E.recoil.enabled = true; E.recoil.pitchDegrees = Mathf.Max(0f, Round(E.recoil.pitchDegrees + d * 0.05f, 2)); });
            Stepper(p, "yaw", "横の基準角(°)", new Vector2(430f, -84f), d => { E.recoil.enabled = true; E.recoil.yawDegrees = Mathf.Max(0f, Round(E.recoil.yawDegrees + d * 0.05f, 2)); });
            Stepper(p, "adsRecoil", "ADS反動倍率", new Vector2(430f, -156f), d => { E.recoil.enabled = true; E.recoil.adsMultiplier = Mathf.Clamp01(Round(E.recoil.adsMultiplier + d * 0.05f, 2)); });
            Label(p, "L_pitchSlider", "縦の基準角（スライダー）", new Vector2(0f, 1f), new Vector2(430f, -228f), new Vector2(230f, 64f), 18).rectTransform.pivot = new Vector2(0f, 1f);
            pitchSlider = UiBuild.Slider(p, "PitchSlider", new Vector2(0f, 1f), new Vector2(665f, -228f), new Vector2(340f, 64f), 0f, 300f, v =>
            {
                if (refreshing)
                    return;
                E.recoil.enabled = true;
                E.recoil.pitchDegrees = v / 100f;
                Changed();
            });
            ((RectTransform)pitchSlider.transform).pivot = new Vector2(0f, 1f);
            var legacy = Make(p, "RecoilLegacy", "従来の反動に戻す", new Vector2(0f, 1f), new Vector2(16f, -300f), new Vector2(405f, 64f), () =>
            {
                E.recoil = current[SelectedId].recoil.Clone();
                Changed();
            }, 20);
            ((RectTransform)legacy.transform).pivot = new Vector2(0f, 1f);
            recoilError = Label(p, "RecoilError", "", new Vector2(0f, 1f), new Vector2(16f, -372f), new Vector2(1000f, 40f), 18);
            recoilError.rectTransform.pivot = new Vector2(0f, 1f);
            recoilError.color = new Color(1f, 0.45f, 0.4f, 1f);
            recoilText = Label(p, "RecoilPreview", "", new Vector2(0f, 1f), new Vector2(16f, -416f), new Vector2(1000f, 120f), 18);
            recoilText.rectTransform.pivot = new Vector2(0f, 1f);
            recoilText.verticalOverflow = VerticalWrapMode.Truncate;
            Label(p, "PatternTitle", "予想反動（8発の累積、照準の動く向き・回復は含めない）", new Vector2(1f, 1f), new Vector2(-16f, -8f), new Vector2(420f, 40f), 16).rectTransform.pivot = new Vector2(1f, 1f);
            pattern = UiBuild.Place(UiBuild.Rect("Pattern", p), new Vector2(1f, 1f), new Vector2(-16f, -52f), new Vector2(420f, 420f));
            UiBuild.Image(pattern, new Color(0f, 0f, 0f, 0.5f), false);
            var hLine = UiBuild.Place(UiBuild.Rect("AxisH", pattern), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 1f));
            UiBuild.Image(hLine, new Color(1f, 1f, 1f, 0.25f), false);
            var vLine = UiBuild.Place(UiBuild.Rect("AxisV", pattern), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1f, 420f));
            UiBuild.Image(vLine, new Color(1f, 1f, 1f, 0.25f), false);
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
            falloffText = Label(p, "FalloffTable", "", new Vector2(0f, 1f), new Vector2(560f, -370f), new Vector2(720f, 160f), 17);
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
            B("Apply", "試射に適用", ApplyToTest, new Vector2(16f, -12f));
            B("EndTest", "試射を終了（正式値に戻す）", EndTest, new Vector2(326f, -12f));
            B("Save", "試験データを保存", Save, new Vector2(16f, -88f));
            B("Load", "保存データを読み込み", Load, new Vector2(326f, -88f));
            B("ResetEdit", "編集を現行値に戻す", ResetEdited, new Vector2(16f, -164f));
            B("DeleteSaved", "保存データを削除", DeleteSaved, new Vector2(326f, -164f));
            testText = Label(p, "TestInfo", "", new Vector2(0f, 1f), new Vector2(660f, -12f), new Vector2(620f, 500f), 17);
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

        public void Select(string weaponId)
        {
            if (!originals.ContainsKey(weaponId))
                return;
            SelectedId = weaponId;
            selectedPoint = 0;
            Refresh();
        }

        void Changed()
        {
            messageText.text = "";
            Refresh();
        }

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

        /// <summary>Puts the edited values on a runtime clone of the selected weapon and equips it (weapon assets untouched).</summary>
        public void ApplyToTest()
        {
            var problems = E.Problems();
            if (problems.Count > 0)
            {
                messageText.text = "適用できません：" + string.Join("／", problems);
                return;
            }
            var original = originals[SelectedId];
            if (devGun != null && OriginalOf(gun.Definition) != original)
                devGun.Select(SelectedId);
            var clone = Instantiate(original);
            clone.name = original.name + " (lab)";
            E.ApplyTo(clone);
            var old = appliedClone;
            appliedClone = clone;
            gun.SetDefinition(clone);
            if (old != null)
                Destroy(old);
            messageText.text = $"{SelectedId} に編集値を適用しました（試射中）。弾倉・予備弾は満タンから";
            Refresh();
        }

        /// <summary>Back to the official weapon (the asset) for the gun in hand.</summary>
        public void EndTest()
        {
            if (appliedClone == null)
            {
                messageText.text = "試射中の編集値はありません";
                return;
            }
            var original = originals[appliedClone.weaponId];
            var clone = appliedClone;
            appliedClone = null;
            if (gun.Definition == clone)
                gun.SetDefinition(original);
            Destroy(clone);
            messageText.text = "正式な武器の値に戻しました";
            Refresh();
        }

        void OnDefinitionChanged()
        {
            // Switched elsewhere (menu test choice): the test session of the clone ends.
            if (appliedClone != null && gun.Definition != appliedClone)
            {
                Destroy(appliedClone);
                appliedClone = null;
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
            messageText.text = "保存しました：" + store.PathOf(SelectedId);
            Refresh();
        }

        public void Load()
        {
            if (store.TryLoad(SelectedId, out var loaded, out string error))
            {
                edited[SelectedId] = loaded;
                selectedPoint = 0;
                messageText.text = $"読み込みました（保存 {loaded.savedAt}、基準 {Short(loaded.baseCommit)}）";
            }
            else
                messageText.text = error;
            Refresh();
        }

        public void ResetEdited()
        {
            edited[SelectedId] = current[SelectedId].Clone();
            selectedPoint = 0;
            messageText.text = "編集値を現行の武器の値に戻しました（保存データは消していません）";
            Refresh();
        }

        public void DeleteSaved()
        {
            messageText.text = store.Delete(SelectedId) ? "保存データを削除しました" : "保存データはありません";
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
            statusText.text = Testing ? $"試射中：{appliedClone.weaponId}（編集値）" : $"装備：{OriginalOf(gun.Definition).weaponId}（正式値）";
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
            pitchSlider.value = Mathf.Round(e.recoil.pitchDegrees * 100f);

            RefreshRecoil(e);
            RefreshFalloff(e, c);
            RefreshTest(e, c);
            refreshing = false;
        }

        void RefreshRecoil(WeaponBalanceProfile e)
        {
            var r = e.recoil;
            string[] axisNames = { "縦方向", "横方向", "斜め", "複合" };
            string[] vNames = { "なし", "上", "下" };
            string[] hNames = { "なし", "左", "右", "左右交互" };
            string[] sNames = { "弱(0.7)", "中(1.0)", "強(1.4)" };
            void SetCycle(string key, string text)
            {
                var t = cycleButtons[key].GetComponentInChildren<Text>();
                t.text = r.enabled ? text : text + "（未使用）";
            }
            SetCycle("axis", axisNames[(int)r.axis]);
            SetCycle("vertical", vNames[(int)r.vertical]);
            SetCycle("horizontal", hNames[(int)r.horizontal]);
            SetCycle("strength", sNames[(int)r.strength]);
            foreach (var go in patternDots)
                Destroy(go);
            patternDots.Clear();
            if (!r.enabled)
            {
                recoilError.text = "";
                recoilText.text = $"従来の反動：縦 {F(r.pitchDegrees)}°（上）、横 ±{F(r.yawDegrees)}° のランダム、ADS ×{F(r.adsMultiplier)}。方向・強度のどれかを押すと、データで指定する反動に切り替わります。";
                return;
            }
            recoilError.text = r.IsValid(out string err) ? "" : "組み合わせが不正（適用できません）：" + err;
            if (!string.IsNullOrEmpty(recoilError.text))
            {
                recoilText.text = "";
                return;
            }
            var lines = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                RecoilCalculator.TryCompute(r, i, 0f, out float hp, out float hy);
                RecoilCalculator.TryCompute(r, i, 1f, out float ap, out float ay);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}発目：腰だめ 縦{1:+0.00;-0.00;0.00}° 横{2:+0.00;-0.00;0.00}°／ADS 縦{3:+0.00;-0.00;0.00}° 横{4:+0.00;-0.00;0.00}°",
                    i + 1, hp, hy, ap, ay));
            }
            recoilText.text = $"{RecoilCalculator.DirectionName(r)}・{(r.strength == RecoilStrength.Weak ? "弱" : r.strength == RecoilStrength.Strong ? "強" : "中")}\n" + string.Join("\n", lines);
            // Cumulative kick of 8 shots (aim moves up = dot goes up, right = right).
            float px = 0f, py = 0f;
            var pts = new List<Vector2> { Vector2.zero };
            for (int i = 0; i < 8; i++)
            {
                RecoilCalculator.TryCompute(r, i, 0f, out float kp, out float ky);
                px += ky;
                py += kp;
                pts.Add(new Vector2(px, py));
            }
            float extent = 0.01f;
            foreach (var v in pts)
                extent = Mathf.Max(extent, Mathf.Abs(v.x), Mathf.Abs(v.y));
            float scale = 180f / extent;
            for (int i = 0; i < pts.Count; i++)
            {
                var dot = UiBuild.Place(UiBuild.Rect("Dot" + i, pattern), new Vector2(0.5f, 0.5f), pts[i] * scale, new Vector2(14f, 14f));
                UiBuild.Image(dot, i == 0 ? Color.white : Color.Lerp(EditedColor, new Color(1f, 0.3f, 0.2f, 1f), i / 8f), false);
                patternDots.Add(dot.gameObject);
            }
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
            var diff = WeaponBalanceProfile.Diff(c, e);
            var lines = new List<string>
            {
                Testing ? $"試射中：{appliedClone.weaponId} に編集値を適用中（武器の正式な値は変えていません）" : "試射中の編集値なし（正式な武器の値）",
                LastHitText,
                $"保存先：{store.PathOf(SelectedId)}",
                $"基準コミット：{Short(BaseCommit)}",
                diff.Count == 0 ? "現行値との差：なし" : "現行値との差：",
            };
            foreach (var (column, from, to) in diff)
                lines.Add($"・{column}：{from} → {to}");
            var problems = e.Problems();
            if (problems.Count > 0)
                lines.Add("適用できない理由：" + string.Join("／", problems));
            testText.text = string.Join("\n", lines);
        }
    }
}
