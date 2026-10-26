using System.Collections;
using System.Linq;
using Deadlock.Game;
using Deadlock.Home;
using Deadlock.UI;
using Deadlock.UI.Layout;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>Home menu: full-screen free layout, edit (drag / resize / save / cancel / reset), entries, home ⇄ firing range.</summary>
    public class HomeMenuTests : InputTestFixture
    {
        string savedBefore;
        HomeMenu menu;

        // Input runs on the test fixture's devices (the home's UI input module must not touch the real input state).
        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            string key = UiLayoutStore.Key(HomeMenu.LayoutId);
            savedBefore = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            PlayerPrefs.DeleteKey(key);
        }

        public override void TearDown()
        {
            string key = UiLayoutStore.Key(HomeMenu.LayoutId);
            if (savedBefore != null)
                PlayerPrefs.SetString(key, savedBefore);
            else
                PlayerPrefs.DeleteKey(key);
            Time.timeScale = 1f;
            GameMenu.OpenSkinSettingsOnLoad = false;
            base.TearDown();
        }

        IEnumerator LoadHome()
        {
            SceneManager.LoadScene(GameScenes.Home);
            yield return null;
            yield return null;
            menu = Object.FindAnyObjectByType<HomeMenu>();
        }

        static void Click(Transform root, string name) =>
            root.GetComponentsInChildren<Button>(true).First(b => b.name == name).onClick.Invoke();

        static RectTransform Row(GameObject panel, string name) =>
            panel.GetComponentsInChildren<RectTransform>(true).First(r => r.name == name);

        static void Press(GameObject panel, string rowName) => Row(panel, rowName).GetComponent<Button>().onClick.Invoke();

        [UnityTest]
        public IEnumerator Menu_DesignElements_OnAFullScreenLayout_InsideTheSafeArea()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            var canvasRect = (RectTransform)menu.transform;
            Assert.AreEqual(canvasRect.rect.size, layout.CanvasSize, "the layout covers the whole canvas, not the background");
            CollectionAssert.AreEqual(new[] { HomeMenu.Logo, HomeMenu.Survival, HomeMenu.Training, HomeMenu.Event, HomeMenu.Gear, HomeMenu.Points, HomeMenu.Settings, HomeMenu.Crate }, layout.Ids);
            Assert.IsTrue(layout.ShowingDefaults);
            foreach (var id in layout.Ids)
            {
                Assert.IsTrue(layout.Element(id).gameObject.activeInHierarchy, id);
                Assert.IsTrue(UiLayoutMath.InsideSafeArea(layout.Current.Get(id), layout.Spec(id), layout.CanvasSize, layout.SafeRect), id);
            }
            var sizes = HomeMenu.MainButtons.Select(id => layout.Element(id).sizeDelta).ToArray();
            Assert.IsTrue(sizes.All(sz => sz == sizes[0]), "the four main buttons have one size");
            foreach (var id in HomeMenu.MainButtons)
            {
                var b = layout.Element(id);
                Assert.IsNotNull(b.Find("Icon"), id);
                Assert.AreEqual(HomeMenu.DisplayName(id), b.Find("Label").GetComponent<Text>().text);
                Assert.AreEqual("home_menu_arrow", b.Find("Arrow").GetComponent<Image>().sprite.name, "approved arrow, no text");
                Assert.IsNull(b.Find("Arrow").GetComponent<Text>());
                var icon = b.Find("Icon").GetComponent<Image>();
                Assert.AreEqual("home_icon_" + id, icon.sprite.name, "approved icon");
                Assert.IsTrue(icon.enabled);
                Assert.AreEqual(Color.white, icon.color, "pixel art drawn as painted");
            }
            Assert.AreEqual("home_title_logo", layout.Element(HomeMenu.Logo).GetComponent<Image>().sprite.name, "approved home logo");
            var gearIcon = layout.Element(HomeMenu.Settings).Find("Icon").GetComponent<Image>();
            Assert.AreEqual("home_icon_settings", gearIcon.sprite.name);
            Assert.AreEqual(Color.white, gearIcon.color);
            Assert.IsFalse(layout.Element(HomeMenu.Crate).Find("Icon").GetComponent<Image>().enabled, "Crate art not approved: empty slot");
            Assert.AreEqual("－ PT", layout.Element(HomeMenu.Points).GetComponentInChildren<Text>().text, "no fake balance");
            Assert.AreEqual("Crate", layout.Element(HomeMenu.Crate).Find("Label").GetComponent<Text>().text);
        }

        [UnityTest]
        public IEnumerator BackgroundTap_NoLongerLeavesTheHome()
        {
            yield return LoadHome();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator Edit_Drag_Resize_Save_IsRestoredAfterReload()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            menu.OpenLayoutEditor();
            Assert.IsTrue(menu.Editor.IsOpen);
            Assert.IsFalse(menu.LayoutEditButton.gameObject.activeSelf);
            var frame = menu.transform.GetComponentsInChildren<UiLayoutPointer>(true).First(p => p.name == "Frame_" + HomeMenu.Gear);
            var canvas = menu.GetComponent<Canvas>();
            var start = layout.Current.Get(HomeMenu.Gear).center;
            frame.OnPointerDown(new PointerEventData(EventSystem.current));
            Assert.AreEqual(HomeMenu.Gear, menu.Editor.SelectedId);
            frame.OnDrag(new PointerEventData(EventSystem.current) { delta = new Vector2(-400f, 0f) * canvas.scaleFactor });
            Assert.Less(menu.Editor.Draft.Get(HomeMenu.Gear).center.x, start.x);
            menu.Editor.SetScale(HomeMenu.Gear, 0.8f);
            Assert.AreEqual(490f * 0.8f, layout.Element(HomeMenu.Gear).sizeDelta.x, 1e-3f, "resize shown at once");
            var moved = menu.Editor.Draft.Get(HomeMenu.Gear).center;
            menu.Editor.Save();
            Assert.IsFalse(menu.Editor.IsOpen);
            Assert.IsTrue(menu.LayoutEditButton.gameObject.activeSelf);
            Assert.AreEqual(HomeMenu.LayoutVersion, UiLayoutStore.Load(HomeMenu.LayoutId).layoutVersion);

            yield return LoadHome();
            var item = menu.Layout.Current.Get(HomeMenu.Gear);
            Assert.IsFalse(menu.Layout.ShowingDefaults);
            Assert.AreEqual(moved.x, item.center.x, 1e-4f);
            Assert.AreEqual(0.8f, item.scale, 1e-4f);
        }

        [UnityTest]
        public IEnumerator SavedVersion1Layout_ShowsTheNewDefaults_AndIsKept()
        {
            const string v1 = "{\"layoutVersion\":1,\"items\":[{\"id\":\"mode\",\"center\":{\"x\":0.07,\"y\":0.64},\"scale\":1.0,\"visible\":true}]}";
            PlayerPrefs.SetString(UiLayoutStore.Key(HomeMenu.LayoutId), v1);
            yield return LoadHome();
            Assert.IsTrue(menu.Layout.ShowingDefaults);
            Assert.AreEqual(v1, PlayerPrefs.GetString(UiLayoutStore.Key(HomeMenu.LayoutId)), "not overwritten until the player saves");
        }

        [UnityTest]
        public IEnumerator Edit_Cancel_RestoresTheLayoutFromBefore_Reset_GivesDefaults()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            var before = layout.Current.Get(HomeMenu.Crate);
            menu.OpenLayoutEditor();
            menu.Editor.DragBy(HomeMenu.Crate, new Vector2(-300f, 200f));
            menu.Editor.SetScale(HomeMenu.Crate, 0.6f);
            Click(menu.transform, "CancelButton");
            Assert.IsFalse(menu.Editor.IsOpen);
            Assert.AreEqual(before.center, layout.Current.Get(HomeMenu.Crate).center);
            Assert.AreEqual(before.scale, layout.Current.Get(HomeMenu.Crate).scale);
            Assert.IsFalse(UiLayoutStore.HasSaved(HomeMenu.LayoutId), "cancel saves nothing");

            menu.OpenLayoutEditor();
            menu.Editor.DragBy(HomeMenu.Crate, new Vector2(-300f, 200f));
            Click(menu.transform, "ResetButton");
            Assert.AreEqual(layout.Defaults().Get(HomeMenu.Crate).center, menu.Editor.Draft.Get(HomeMenu.Crate).center);
            Click(menu.transform, "CancelButton");
        }

        [UnityTest]
        public IEnumerator Edit_OutsideTheSafeArea_IsAllowed_WithAWarning()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            menu.OpenLayoutEditor();
            menu.Editor.Select(HomeMenu.Settings);
            menu.Editor.DragBy(HomeMenu.Settings, new Vector2(5000f, 5000f));
            var r = UiLayoutMath.ItemRect(menu.Editor.Draft.Get(HomeMenu.Settings), layout.Spec(HomeMenu.Settings), layout.CanvasSize);
            Assert.AreEqual(layout.CanvasSize.x, r.xMax, 0.5f);
            Assert.AreEqual(layout.CanvasSize.y, r.yMax, 0.5f);
            bool outside = !UiLayoutMath.Contains(layout.SafeRect, r);
            Assert.AreEqual(outside, menu.Editor.IsOutsideSafeArea(HomeMenu.Settings));
            Assert.AreEqual(outside ? UiLayoutEditor.OutsideSafeAreaWarning : "", menu.Editor.Warning);
            Click(menu.transform, "CancelButton");
            Assert.IsFalse(menu.Editor.IsOpen);
        }

        [UnityTest]
        public IEnumerator Buttons_OpenTheirScreens_ComingSoonStartsNothing()
        {
            yield return LoadHome();
            foreach (var (id, panel) in new[] { (HomeMenu.Survival, "SurvivalPanel"), (HomeMenu.Training, "TrainingPanel"), (HomeMenu.Event, "EventPanel"),
                         (HomeMenu.Gear, "GearPanel"), (HomeMenu.Crate, "CratePanel"), (HomeMenu.Points, "PointsPanel"), (HomeMenu.Settings, "SettingsPanel") })
            {
                menu.Layout.Element(id).GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(panel, menu.OpenPanelName, id);
                Assert.IsFalse(menu.PanelObject(panel).GetComponentsInChildren<Button>(true).Any(b => b.name == "BackButton"), "no back button");
                menu.PanelObject(panel).GetComponentInChildren<HomePanelBackdrop>().OnPointerClick(new PointerEventData(EventSystem.current));
                Assert.IsNull(menu.OpenPanelName, "back to the home");
            }
            // Event: Ruins is the test field, Assault is not made yet and starts nothing
            menu.Layout.Element(HomeMenu.Event).GetComponent<Button>().onClick.Invoke();
            var eventPanel = menu.PanelObject("EventPanel");
            Assert.AreEqual("準備中", Row(eventPanel, "Assault（急襲）").Find("Status").GetComponent<Text>().text);
            Press(eventPanel, "Assault（急襲）");
            yield return null;
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
            StringAssert.Contains("準備中", menu.Message);
            StringAssert.Contains("テスト", Row(eventPanel, "Ruins（廃墟）").Find("Status").GetComponent<Text>().text);
            // Gear: three tabs
            menu.Layout.Element(HomeMenu.Gear).GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual("Loadout", menu.GearTab);
            foreach (var tab in HomeMenu.GearTabs)
            {
                Click(menu.PanelObject("GearPanel").transform, tab + "Tab");
                Assert.AreEqual(tab, menu.GearTab);
                Assert.IsTrue(Row(menu.PanelObject("GearPanel"), tab + "Page").gameObject.activeSelf);
            }
            // Settings: layout editor
            menu.Layout.Element(HomeMenu.Settings).GetComponent<Button>().onClick.Invoke();
            Click(menu.PanelObject("SettingsPanel").transform, "ホームのレイアウト編集");
            Assert.IsTrue(menu.Editor.IsOpen);
            menu.Editor.Cancel();
        }

        // ------------------------------------------------------------------ taps outside a sub-screen (real pointer input)

        static readonly string[] PanelButtons = { HomeMenu.Survival, HomeMenu.Training, HomeMenu.Event, HomeMenu.Gear, HomeMenu.Crate, HomeMenu.Points, HomeMenu.Settings };

        static Vector2 ScreenCenter(RectTransform r) => RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));

        static RectTransform Window(GameObject panel) => Row(panel, "Window");

        /// <summary>The topmost UI object under a screen point (what a tap there reaches first).</summary>
        static GameObject TopUnder(Vector2 screen)
        {
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        /// <summary>A home button that the open sub-screen's window does not cover (a tap there lands on the dimmed layer).</summary>
        string HomeButtonOutside(GameObject panel) =>
            PanelButtons.First(id => !RectTransformUtility.RectangleContainsScreenPoint(Window(panel), ScreenCenter(menu.Layout.Element(id)), null));

        IEnumerator MouseClick(Mouse mouse, Vector2 screen)
        {
            Move(mouse.position, screen);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OutsideClick_ClosesEverySubScreen_AndDoesNotPressTheHomeButtonBehind()
        {
            yield return LoadHome();
            var mouse = InputSystem.GetDevice<Mouse>();
            var layoutBefore = JsonUtility.ToJson(menu.Layout.Current);
            foreach (var id in PanelButtons)
            {
                menu.Layout.Element(id).GetComponent<Button>().onClick.Invoke();
                yield return null; // drawn once before a real tap can reach it
                string panelName = menu.OpenPanelName;
                Assert.IsNotNull(panelName, id);
                var panel = menu.PanelObject(panelName);
                Assert.IsFalse(panel.GetComponentsInChildren<Button>(true).Any(b => b.name == "BackButton"), "no back button: " + panelName);
                string behindId = HomeButtonOutside(panel);
                var at = ScreenCenter(menu.Layout.Element(behindId));
                Assert.AreEqual("Backdrop", TopUnder(at).name, "the dimmed layer is on top of the home button " + behindId);
                yield return MouseClick(mouse, at);
                Assert.IsNull(menu.OpenPanelName, $"{panelName}: one click outside closes it, and {behindId} behind it opened nothing");
                Assert.IsFalse(panel.activeSelf);
                Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
            }
            Assert.AreEqual(layoutBefore, JsonUtility.ToJson(menu.Layout.Current), "placement unchanged");
            Assert.IsFalse(UiLayoutStore.HasSaved(HomeMenu.LayoutId), "nothing saved");
            // A right click outside does not close.
            menu.Layout.Element(HomeMenu.Crate).GetComponent<Button>().onClick.Invoke();
            yield return null; // drawn once before a real tap can reach it
            Move(mouse.position, ScreenCenter(menu.Layout.Element(HomeButtonOutside(menu.PanelObject("CratePanel")))));
            yield return null;
            Press(mouse.rightButton);
            yield return null;
            Release(mouse.rightButton);
            yield return null;
            Assert.AreEqual("CratePanel", menu.OpenPanelName);
        }

        [UnityTest]
        public IEnumerator InsideClick_KeepsTheSubScreenOpen_AndItsButtonsWork()
        {
            yield return LoadHome();
            var mouse = InputSystem.GetDevice<Mouse>();
            menu.OpenGear("Loadout");
            yield return null;
            var gear = menu.PanelObject("GearPanel");
            var title = Row(gear, "Title");
            yield return MouseClick(mouse, ScreenCenter(title));
            Assert.AreEqual("GearPanel", menu.OpenPanelName, "a click on the window (no button) keeps it open");
            yield return MouseClick(mouse, ScreenCenter(Row(gear, "SkinTab")));
            Assert.AreEqual("GearPanel", menu.OpenPanelName);
            Assert.AreEqual("Skin", menu.GearTab, "the tab inside works");

            menu.Layout.Element(HomeMenu.Event).GetComponent<Button>().onClick.Invoke();

            yield return null; // drawn once before a real tap can reach it
            var eventPanel = menu.PanelObject("EventPanel");
            yield return MouseClick(mouse, ScreenCenter(Row(eventPanel, "Assault（急襲）")));
            Assert.AreEqual("EventPanel", menu.OpenPanelName);
            StringAssert.Contains("準備中", menu.Message, "the row inside works");
        }

        [UnityTest]
        public IEnumerator OutsideTouch_Closes_MultiTouchDoesNotPressAnything()
        {
            yield return LoadHome();
            var screen = InputSystem.AddDevice<Touchscreen>();
            // One finger outside: closes on lift.
            menu.Layout.Element(HomeMenu.Survival).GetComponent<Button>().onClick.Invoke();
            yield return null; // drawn once before a real tap can reach it
            var outside = ScreenCenter(menu.Layout.Element(HomeButtonOutside(menu.PanelObject("SurvivalPanel"))));
            BeginTouch(1, outside, screen: screen);
            yield return null;
            Assert.AreEqual("SurvivalPanel", menu.OpenPanelName, "not on touch down");
            EndTouch(1, outside, screen: screen);
            yield return null;
            Assert.IsNull(menu.OpenPanelName);

            // A finger holding a row inside while another taps outside: closes once, the held row is not pressed.
            menu.Layout.Element(HomeMenu.Event).GetComponent<Button>().onClick.Invoke();
            yield return null; // drawn once before a real tap can reach it
            var eventPanel = menu.PanelObject("EventPanel");
            var row = ScreenCenter(Row(eventPanel, "Assault（急襲）"));
            outside = ScreenCenter(menu.Layout.Element(HomeButtonOutside(eventPanel)));
            BeginTouch(2, row, screen: screen);
            yield return null;
            BeginTouch(3, outside, screen: screen);
            yield return null;
            EndTouch(3, outside, screen: screen);
            yield return null;
            Assert.IsNull(menu.OpenPanelName);
            EndTouch(2, row, screen: screen);
            yield return null;
            Assert.IsNull(menu.OpenPanelName, "nothing reopened");
            Assert.AreEqual("", menu.Message, "the held row did not fire");
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);

            // Two fingers outside at once: closed, and nothing behind opens.
            menu.Layout.Element(HomeMenu.Crate).GetComponent<Button>().onClick.Invoke();
            yield return null; // drawn once before a real tap can reach it
            var crate = menu.PanelObject("CratePanel");
            var a = ScreenCenter(menu.Layout.Element(HomeButtonOutside(crate)));
            var b = new Vector2(2f, 2f);
            Assert.AreEqual("Backdrop", TopUnder(b).name);
            BeginTouch(4, a, screen: screen);
            BeginTouch(5, b, screen: screen);
            yield return null;
            EndTouch(4, a, screen: screen);
            EndTouch(5, b, screen: screen);
            yield return null;
            yield return null;
            Assert.IsNull(menu.OpenPanelName);
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        public IEnumerator LayoutEditor_ClicksOnElementsDoNotOpenOrCloseSubScreens()
        {
            yield return LoadHome();
            var mouse = InputSystem.GetDevice<Mouse>();
            menu.Layout.Element(HomeMenu.Settings).GetComponent<Button>().onClick.Invoke();
            yield return null; // drawn once before a real tap can reach it
            Click(menu.PanelObject("SettingsPanel").transform, "ホームのレイアウト編集");
            yield return null;
            Assert.IsTrue(menu.Editor.IsOpen);
            Assert.IsNull(menu.OpenPanelName, "the settings screen closed for the editor");
            foreach (var p in menu.GetComponentsInChildren<HomePanelBackdrop>(true))
                Assert.IsFalse(p.gameObject.activeInHierarchy, "no dimmed layer while editing");
            yield return MouseClick(mouse, ScreenCenter(menu.Layout.Element(HomeMenu.Crate)));
            Assert.IsTrue(menu.Editor.IsOpen, "the editor stays open");
            Assert.AreEqual(HomeMenu.Crate, menu.Editor.SelectedId, "the click selects the element");
            Assert.IsNull(menu.OpenPanelName, "no sub-screen opens while editing");
            menu.Editor.Cancel();
        }

        [UnityTest]
        public IEnumerator Settings_ResetToDefaults_AsksOnce_ThenSaves()
        {
            yield return LoadHome();
            menu.OpenLayoutEditor();
            menu.Editor.DragBy(HomeMenu.Crate, new Vector2(-300f, 0f));
            menu.Editor.Save();
            var moved = menu.Layout.Current.Get(HomeMenu.Crate).center;
            menu.Layout.Element(HomeMenu.Settings).GetComponent<Button>().onClick.Invoke();
            var settings = menu.PanelObject("SettingsPanel").transform;
            Click(settings, "ホームの配置を初期配置に戻す");
            Assert.AreEqual(moved, menu.Layout.Current.Get(HomeMenu.Crate).center, "first press only asks");
            Click(settings, "ホームの配置を初期配置に戻す");
            Assert.AreEqual(menu.Layout.Defaults().Get(HomeMenu.Crate).center, menu.Layout.Current.Get(HomeMenu.Crate).center);
        }

        [UnityTest]
        public IEnumerator Training_ToFiringRange_AndBack_CandleStillAnimates()
        {
            yield return LoadHome();
            menu.Layout.Element(HomeMenu.Training).GetComponent<Button>().onClick.Invoke();
            Press(menu.PanelObject("TrainingPanel"), "射撃場");
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != GameScenes.FiringRange && Time.realtimeSinceStartup - t0 < 20f)
                yield return null;
            Assert.AreEqual(GameScenes.FiringRange, SceneManager.GetActiveScene().name);
            yield return null;

            var gameMenu = Object.FindAnyObjectByType<GameMenu>();
            gameMenu.Open();
            Assert.AreEqual(0f, Time.timeScale, "the firing range pauses while the menu is open");
            gameMenu.HomeButton.onClick.Invoke();
            Assert.IsTrue(gameMenu.IsHomeConfirmOpen, "asks first");
            Click(gameMenu.transform, "CancelHomeButton");
            Assert.IsTrue(gameMenu.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);
            gameMenu.HomeButton.onClick.Invoke();
            Click(gameMenu.transform, "ConfirmHomeButton");
            t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != GameScenes.Home && Time.realtimeSinceStartup - t0 < 20f)
                yield return null;
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
            Assert.AreEqual(1f, Time.timeScale);
            var candle = Object.FindAnyObjectByType<HomeCandleAnimator>();
            var seen = new System.Collections.Generic.HashSet<int>();
            t0 = Time.time;
            while (Time.time - t0 < 0.8f)
            {
                yield return null;
                seen.Add(candle.Frame);
            }
            Assert.AreEqual(4, seen.Count, "candle animates after coming back");
        }

        [UnityTest]
        public IEnumerator GearSkin_OpensTheSkinTestScreenInTheFiringRange()
        {
            yield return LoadHome();
            menu.OpenGear("Skin");
            Press(menu.PanelObject("GearPanel"), "スキンの確認（テスト用）");
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != GameScenes.FiringRange && Time.realtimeSinceStartup - t0 < 20f)
                yield return null;
            yield return null;
            yield return null;
            var gameMenu = Object.FindAnyObjectByType<GameMenu>();
            Assert.IsTrue(gameMenu.IsSkinSettingsOpen);
            Assert.IsFalse(GameMenu.OpenSkinSettingsOnLoad, "used once");
            gameMenu.CloseSkinSettings();
            gameMenu.Close();
        }
    }
}
