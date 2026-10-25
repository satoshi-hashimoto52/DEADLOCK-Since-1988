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

        [UnityTest]
        public IEnumerator Menu_SixEntries_OnAFullScreenLayout_InsideTheSafeArea()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            var canvasRect = (RectTransform)menu.transform;
            Assert.AreEqual(canvasRect.rect.size, layout.CanvasSize, "the layout covers the whole canvas, not the background");
            CollectionAssert.AreEquivalent(new[] { HomeMenu.Mode, HomeMenu.Equipment, HomeMenu.Skill, HomeMenu.Points, HomeMenu.Gacha, HomeMenu.Settings }, layout.Ids);
            Assert.IsTrue(layout.ShowingDefaults);
            foreach (var id in layout.Ids)
            {
                Assert.IsTrue(layout.Element(id).gameObject.activeInHierarchy, id);
                Assert.IsTrue(UiLayoutMath.InsideSafeArea(layout.Current.Get(id), layout.Spec(id), layout.CanvasSize, layout.SafeRect), id);
            }
            Assert.IsNull(GameObject.Find("InterimContinueHint"), "the interim hint is gone");
        }

        [UnityTest]
        public IEnumerator BackgroundTap_NoLongerLeavesTheHome()
        {
            yield return LoadHome();
            // HomeScreen has no input handling any more; nothing else loads a scene on its own
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
            // a drag through the frame's pointer handler, into the left black margin
            var frame = menu.transform.GetComponentsInChildren<UiLayoutPointer>(true).First(p => p.name == "Frame_" + HomeMenu.Mode);
            var canvas = menu.GetComponent<Canvas>();
            var start = layout.Current.Get(HomeMenu.Mode).center;
            frame.OnPointerDown(new PointerEventData(EventSystem.current));
            Assert.AreEqual(HomeMenu.Mode, menu.Editor.SelectedId);
            frame.OnDrag(new PointerEventData(EventSystem.current) { delta = new Vector2(-400f, 0f) * canvas.scaleFactor });
            var moved = menu.Editor.Draft.Get(HomeMenu.Mode).center;
            Assert.Less(moved.x, start.x);
            Assert.AreEqual(layout.Current.Get(HomeMenu.Mode).center, moved, "shown while dragging");
            menu.Editor.SetScale(HomeMenu.Mode, 1.3f);
            moved = menu.Editor.Draft.Get(HomeMenu.Mode).center; // a larger element may be pulled back onto the screen
            Assert.AreEqual(300f * 1.3f, layout.Element(HomeMenu.Mode).sizeDelta.x, 1e-3f, "resize shown at once");
            Assert.IsTrue(menu.Editor.IsOutsideSafeArea(HomeMenu.Mode) == !UiLayoutMath.InsideSafeArea(menu.Editor.Draft.Get(HomeMenu.Mode), layout.Spec(HomeMenu.Mode), layout.CanvasSize, layout.SafeRect));
            menu.Editor.Save();
            Assert.IsFalse(menu.Editor.IsOpen);
            Assert.IsTrue(menu.LayoutEditButton.gameObject.activeSelf);
            Assert.IsTrue(UiLayoutStore.HasSaved(HomeMenu.LayoutId));

            yield return LoadHome();
            var item = menu.Layout.Current.Get(HomeMenu.Mode);
            Assert.IsFalse(menu.Layout.ShowingDefaults);
            Assert.AreEqual(moved.x, item.center.x, 1e-4f);
            Assert.AreEqual(1.3f, item.scale, 1e-4f);
        }

        [UnityTest]
        public IEnumerator Edit_Cancel_RestoresTheLayoutFromBefore_Reset_GivesDefaults()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            var before = layout.Current.Get(HomeMenu.Gacha);
            menu.OpenLayoutEditor();
            menu.Editor.DragBy(HomeMenu.Gacha, new Vector2(-300f, 200f));
            menu.Editor.SetScale(HomeMenu.Gacha, 0.6f);
            Click(menu.transform, "CancelButton");
            Assert.IsFalse(menu.Editor.IsOpen);
            Assert.AreEqual(before.center, layout.Current.Get(HomeMenu.Gacha).center);
            Assert.AreEqual(before.scale, layout.Current.Get(HomeMenu.Gacha).scale);
            Assert.IsFalse(UiLayoutStore.HasSaved(HomeMenu.LayoutId), "cancel saves nothing");

            menu.OpenLayoutEditor();
            menu.Editor.DragBy(HomeMenu.Gacha, new Vector2(-300f, 200f));
            Click(menu.transform, "ResetButton");
            Assert.AreEqual(layout.Defaults().Get(HomeMenu.Gacha).center, menu.Editor.Draft.Get(HomeMenu.Gacha).center);
            Click(menu.transform, "CancelButton");
        }

        [UnityTest]
        public IEnumerator Edit_OutsideTheSafeArea_IsAllowed_WithAWarning()
        {
            yield return LoadHome();
            var layout = menu.Layout;
            menu.OpenLayoutEditor();
            menu.Editor.Select(HomeMenu.Settings);
            // to the very corner of the screen (allowed; kept on screen)
            menu.Editor.DragBy(HomeMenu.Settings, new Vector2(5000f, 5000f));
            var r = UiLayoutMath.ItemRect(menu.Editor.Draft.Get(HomeMenu.Settings), layout.Spec(HomeMenu.Settings), layout.CanvasSize);
            Assert.AreEqual(layout.CanvasSize.x, r.xMax, 0.5f);
            Assert.AreEqual(layout.CanvasSize.y, r.yMax, 0.5f);
            bool outside = !UiLayoutMath.Contains(layout.SafeRect, r);
            Assert.AreEqual(outside, menu.Editor.IsOutsideSafeArea(HomeMenu.Settings));
            Assert.AreEqual(outside ? UiLayoutEditor.OutsideSafeAreaWarning : "", menu.Editor.Warning);
            // the toolbar stays usable: cancel ends editing
            Click(menu.transform, "CancelButton");
            Assert.IsFalse(menu.Editor.IsOpen);
        }

        [UnityTest]
        public IEnumerator Entries_OpenTheirPanels_UnimplementedOnesSaySo()
        {
            yield return LoadHome();
            foreach (var (id, panel) in new[] { (HomeMenu.Mode, "ModePanel"), (HomeMenu.Equipment, "EquipmentPanel"), (HomeMenu.Skill, "SkillPanel"),
                         (HomeMenu.Gacha, "GachaPanel"), (HomeMenu.Points, "PointsPanel"), (HomeMenu.Settings, "SettingsPanel") })
            {
                menu.Layout.Element(id).GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(panel, menu.OpenPanelName, id);
                var title = menu.PanelObject(panel).transform.Find("SafeArea/Window/Title").GetComponent<Text>().text;
                if (id == HomeMenu.Equipment || id == HomeMenu.Skill || id == HomeMenu.Gacha || id == HomeMenu.Points)
                    StringAssert.Contains("未実装", title);
                Click(menu.PanelObject(panel).transform, "CloseButton");
                Assert.IsNull(menu.OpenPanelName);
            }
            menu.Layout.Element(HomeMenu.Settings).GetComponent<Button>().onClick.Invoke();
            Click(menu.PanelObject("SettingsPanel").transform, "ホームのレイアウト編集");
            Assert.IsTrue(menu.Editor.IsOpen);
            menu.Editor.Cancel();
        }

        [UnityTest]
        public IEnumerator Home_ToFiringRange_AndBack_CandleStillAnimates()
        {
            yield return LoadHome();
            menu.Layout.Element(HomeMenu.Mode).GetComponent<Button>().onClick.Invoke();
            Click(menu.PanelObject("ModePanel").transform, "射撃場");
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
            Assert.IsFalse(gameMenu.IsHomeConfirmOpen);
            Assert.IsTrue(gameMenu.IsOpen, "back in the menu, still paused");
            Assert.AreEqual(0f, Time.timeScale);
            gameMenu.HomeButton.onClick.Invoke();
            Click(gameMenu.transform, "ConfirmHomeButton");
            t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != GameScenes.Home && Time.realtimeSinceStartup - t0 < 20f)
                yield return null;
            Assert.AreEqual(GameScenes.Home, SceneManager.GetActiveScene().name);
            Assert.AreEqual(1f, Time.timeScale, "the clock runs again at home");
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
    }
}
