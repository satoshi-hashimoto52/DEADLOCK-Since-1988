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
            var crateIcon = layout.Element(HomeMenu.Crate).Find("Icon").GetComponent<Image>();
            Assert.IsTrue(crateIcon.enabled);
            Assert.AreEqual("home_icon_crate", crateIcon.sprite.name, "approved Crate (design exception)");
            Assert.AreEqual(Color.white, crateIcon.color);
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

        // ------------------------------------------------------------------ panel images and fill transparency

        static Sprite PanelSprite(string name, Color c)
        {
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var px = new Color[128 * 128];
            for (int i = 0; i < px.Length; i++)
                px[i] = c;
            tex.SetPixels(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
            s.name = name;
            return s;
        }

        [UnityTest]
        public IEnumerator PanelImages_FillTransparencyPerTarget_FrameStaysOpaque_AndCanGoBack()
        {
            var saved = HomePanelLook.Targets.ToDictionary(t => t, t => PlayerPrefs.HasKey(HomePanelLook.Key(t)) ? PlayerPrefs.GetInt(HomePanelLook.Key(t)) : (int?)null);
            foreach (var t in HomePanelLook.Targets)
                PlayerPrefs.DeleteKey(HomePanelLook.Key(t));
            try
            {
                yield return LoadHome();
                var parts = menu.PanelParts;
                Assert.AreEqual(7 + 7 + 3 + 2 + 2 + 4, parts.Length, "7 home panels, 7 windows, rows of Survival / Training / Event / Gear");
                CollectionAssert.AreEquivalent(HomePanelLook.Targets, parts.Select(p => p.target).Distinct());
                // The normal home: the approved panel images (from the scene, set by HomeSceneBuilder), defaults applied.
                Assert.AreEqual("home_menu_panel_frame", menu.panelFrame.name);
                Assert.AreEqual("home_menu_panel_fill", menu.panelFill.name);
                Assert.AreEqual(new Vector4(40, 40, 40, 40), menu.panelFrame.border, "9-slice border kept");
                Assert.AreEqual(new Vector4(40, 40, 40, 40), menu.panelFill.border);
                foreach (var p in parts)
                {
                    Assert.IsTrue(p.Skinned, p.name);
                    Assert.AreSame(menu.panelFill, p.Fill.sprite);
                    Assert.AreSame(menu.panelFrame, p.FrameImage.sprite);
                    Assert.AreEqual(1f, p.FrameImage.color.a, "frame and rivets opaque");
                    if (p.gameObject.activeInHierarchy)
                        Assert.AreEqual(HomePanelLook.FillAlpha(HomePanelLook.Default(p.target)) * (p.dimmed ? HomePanelPart.DimFactor : 1f), p.ShownAlpha, 1e-3f, "default drawn alpha: " + p.name);
                }

                var frame = PanelSprite("frame", Color.yellow);
                var fill = PanelSprite("fill", Color.black);
                menu.SetPanelSkin(frame, fill);
                yield return null;
                foreach (var p in parts)
                {
                    Assert.IsTrue(p.Skinned, p.name);
                    Assert.AreSame(fill, p.Fill.sprite);
                    Assert.AreEqual(Image.Type.Sliced, p.Fill.type);
                    Assert.AreSame(frame, p.FrameImage.sprite);
                    Assert.AreEqual(1f, p.FrameImage.color.a, "frame and rivets opaque");
                    Assert.AreEqual(0, p.FrameImage.transform.GetSiblingIndex(), "frame drawn above the fill, below the content");
                    Assert.IsFalse(p.transform.Find("Frame").gameObject.activeSelf, "Unity-drawn lines hidden");
                    float expected = HomePanelLook.FillAlpha(HomePanelLook.Default(p.target)) * (p.dimmed ? HomePanelPart.DimFactor : 1f);
                    if (p.gameObject.activeInHierarchy)
                        Assert.AreEqual(expected, p.ShownAlpha, 1e-3f, "drawn alpha (button state colour included): " + p.name);
                }
                // One target changes alone, at once; the frame stays opaque.
                HomePanelLook.Set(HomePanelTarget.MainButton, 30);
                var survival = menu.Layout.Element(HomeMenu.Survival).GetComponent<HomePanelPart>();
                Assert.AreEqual(0.7f, survival.ShownAlpha, 1e-3f);
                Assert.AreEqual(1f, survival.FrameImage.color.a);
                Assert.AreEqual(HomePanelLook.FillAlpha(8), menu.Layout.Element(HomeMenu.Points).GetComponent<HomePanelPart>().ShownAlpha, 1e-3f, "other targets keep theirs");
                // The menu still works over the images, and the layout is not touched.
                menu.Layout.Element(HomeMenu.Crate).GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual("CratePanel", menu.OpenPanelName);
                menu.ClosePanel();
                Assert.IsFalse(UiLayoutStore.HasSaved(HomeMenu.LayoutId));
                // Small Settings panel (0.5x): the slice border shrinks with it.
                menu.OpenLayoutEditor();
                menu.Editor.SetScale(HomeMenu.Settings, 0.5f);
                yield return null;
                var settings = menu.Layout.Element(HomeMenu.Settings).GetComponent<HomePanelPart>();
                var size = ((RectTransform)settings.transform).rect.size;
                float border = 40f / settings.FrameImage.pixelsPerUnitMultiplier;
                Assert.AreEqual(HomePanelPart.BorderUnits(size), border, 1e-3f);
                Assert.Less(border * 2f, Mathf.Min(size.x, size.y), "corners never meet");
                menu.Editor.Cancel();

                // Back to the Unity-drawn panels.
                menu.SetPanelSkin(null, null);
                foreach (var p in parts)
                {
                    Assert.IsFalse(p.Skinned);
                    Assert.IsNull(p.Fill.sprite);
                    Assert.IsFalse(p.FrameImage.gameObject.activeSelf);
                    Assert.IsTrue(p.transform.Find("Frame").gameObject.activeSelf);
                }
                Assert.AreEqual(HomeMenu.PanelColor, menu.Layout.Element(HomeMenu.Survival).GetComponent<Image>().color);
            }
            finally
            {
                foreach (var pair in saved)
                {
                    if (pair.Value.HasValue)
                        PlayerPrefs.SetInt(HomePanelLook.Key(pair.Key), pair.Value.Value);
                    else
                        PlayerPrefs.DeleteKey(HomePanelLook.Key(pair.Key));
                }
            }
        }

        /// <summary>
        /// Renders the home (background camera + menu canvas) into a texture and reads back the colour at two points of a
        /// panel (fx: share of the width, units: canvas units down from its top). Batchmode has no end-of-frame screen, so
        /// the canvas is drawn by the home camera into a RenderTexture for the read-back, then put back.
        /// </summary>
        IEnumerator RenderPanel(RectTransform panel, (float fx, float units)[] points, Color32[] result)
        {
            var cam = Object.FindAnyObjectByType<HomeBackgroundView>().GetComponent<Camera>();
            var canvas = menu.GetComponent<Canvas>();
            var rt = new RenderTexture(1600, 900, 24);
            var mode = canvas.renderMode;
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 1f;
            canvas.sortingOrder = 1000;
            yield return null;
            yield return null; // the layout follows the new canvas size
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var r = panel.rect;
            for (int i = 0; i < points.Length; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, panel.TransformPoint(new Vector2(r.xMin + r.width * points[i].fx, r.yMax - points[i].units)));
                result[i] = tex.GetPixel(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
            }
            Object.Destroy(tex);
            cam.targetTexture = null;
            canvas.renderMode = mode;
            canvas.sortingOrder = 0;
            rt.Release();
            Object.Destroy(rt);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PanelImages_TransparencyChangesTheShownFill_NotTheFrame()
        {
            var saved = HomePanelLook.Targets.ToDictionary(t => t, t => PlayerPrefs.HasKey(HomePanelLook.Key(t)) ? PlayerPrefs.GetInt(HomePanelLook.Key(t)) : (int?)null);
            try
            {
                yield return LoadHome();
                var white = PanelSprite("fill", Color.white); // a white fill: its alpha shows directly as brightness over the dark room
                var frame = PanelSprite("frame", new Color(0f, 0f, 1f, 1f));
                // Frame image: only a 6 px ring is opaque, the rest clear (as the real frame layer, whose centre is cleared).
                var tex = frame.texture;
                var px = tex.GetPixels();
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        if (x >= 6 && x < 122 && y >= 6 && y < 122)
                            px[y * 128 + x] = Color.clear;
                tex.SetPixels(px);
                tex.Apply();
                menu.SetPanelSkin(frame, white);
                var survival = menu.Layout.Element(HomeMenu.Survival);
                var part = survival.GetComponent<HomePanelPart>();

                var pts = new[] { (0.315f, 0f), (0.5f, 1f) };
                var at0 = new Color32[2];
                var at50 = new Color32[2];
                HomePanelLook.Set(HomePanelTarget.MainButton, 0);
                yield return null;
                // What is drawn: the fill Image's own alpha times its renderer (button state) alpha.
                Assert.AreEqual(1f, part.ShownAlpha, 1e-3f);
                pts[0].Item2 = survival.rect.height * 0.5f;
                yield return RenderPanel(survival, pts, at0);

                HomePanelLook.Set(HomePanelTarget.MainButton, 50);
                yield return null;
                Assert.AreEqual(0.5f, part.ShownAlpha, 1e-3f, "drawn at 50 %, not 57.5 % (button colour multiplier)");
                Assert.AreEqual(1f, part.FrameImage.color.a * part.FrameImage.canvasRenderer.GetColor().a, 1e-3f, "frame drawn opaque");
                pts[0].Item2 = survival.rect.height * 0.5f;
                yield return RenderPanel(survival, pts, at50);
                Color32 fill0 = at0[0], fill50 = at50[0], frame0 = at0[1], frame50 = at50[1];

                // Nothing opaque left over the fill: the only full-size Images are the fill and the frame layer.
                foreach (var g in survival.GetComponentsInChildren<Image>(false))
                    if (g != part.Fill && g != part.FrameImage)
                        Assert.IsFalse(g.rectTransform.rect.size == survival.rect.size, "no other panel-sized image: " + g.name);

                // Rendered: the white fill gets clearly darker at 50 % (the room shows through); the frame does not change.
                Debug.Log($"[test] fill 0%={fill0} 50%={fill50} frame 0%={frame0} 50%={frame50}");
                Assert.Greater(fill0.r + fill0.g + fill0.b, fill50.r + fill50.g + fill50.b + 150, "the fill shows through at 50 %");
                Assert.AreEqual(frame0, frame50, "the frame is the same at 0 % and 50 %");
            }
            finally
            {
                foreach (var pair in saved)
                {
                    if (pair.Value.HasValue)
                        PlayerPrefs.SetInt(HomePanelLook.Key(pair.Key), pair.Value.Value);
                    else
                        PlayerPrefs.DeleteKey(HomePanelLook.Key(pair.Key));
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static Rect ScreenRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        static Vector2 ScreenAtX(RectTransform r, float fx) =>
            RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(new Vector2(r.rect.xMin + r.rect.width * fx, r.rect.center.y)));

        [UnityTest]
        public IEnumerator PanelTuner_AllSixTargets_WorkWithRealPointerInput()
        {
            var saved = HomePanelLook.Targets.ToDictionary(t => t, t => PlayerPrefs.HasKey(HomePanelLook.Key(t)) ? PlayerPrefs.GetInt(HomePanelLook.Key(t)) : (int?)null);
            int? compareSaved = PlayerPrefs.HasKey(HomePanelTuner.CompareKey) ? PlayerPrefs.GetInt(HomePanelTuner.CompareKey) : (int?)null;
            foreach (var t in HomePanelLook.Targets)
                PlayerPrefs.DeleteKey(HomePanelLook.Key(t));
            PlayerPrefs.DeleteKey(HomePanelTuner.CompareKey);
            try
            {
                yield return LoadHome();
                var mouse = InputSystem.GetDevice<Mouse>();
                var tuner = HomePanelTuner.Create(menu);
                yield return null;

                // Every control, where it is drawn, is the first thing a tap there reaches (nothing transparent over it).
                void TopIs(Selectable s)
                {
                    var top = TopUnder(ScreenCenter((RectTransform)s.transform));
                    Assert.IsTrue(top != null && (top == s.gameObject || top.transform.IsChildOf(s.transform)), $"{s.name}: first hit is {(top != null ? top.name : "nothing")}");
                }
                TopIs(tuner.OpenButton);
                // The open button stays clear of every home element (default layout) and of the layout-edit button.
                var openRect = ScreenRect((RectTransform)tuner.OpenButton.transform);
                foreach (var id in menu.Layout.Ids)
                    Assert.IsFalse(openRect.Overlaps(ScreenRect(menu.Layout.Element(id))), "tuner button over " + id);
                Assert.IsFalse(openRect.Overlaps(ScreenRect((RectTransform)menu.LayoutEditButton.transform)), "tuner button over レイアウト編集");
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.OpenButton.transform));
                Assert.IsTrue(tuner.IsOpen);
                yield return null;
                foreach (var s in new Selectable[] { tuner.CompareButton, tuner.Slider, tuner.MinusButton, tuner.PlusButton, tuner.ResetButton, tuner.ResetAllButton, tuner.CloseButton })
                    TopIs(s);
                foreach (var t in HomePanelLook.Targets)
                    TopIs(tuner.TargetButton(t));
                // The window fits inside the safe area.
                var c = new Vector3[4];
                tuner.Window.GetWorldCorners(c);
                Assert.IsTrue(Screen.safeArea.Contains(c[0]) && Screen.safeArea.Contains(c[2]), "tuner inside the safe area");

                // The approved panels are shown; the comparison switch shows the earlier Unity-drawn ones and back.
                Assert.IsFalse(HomePanelTuner.ComparingLegacy);
                Assert.IsTrue(menu.PanelParts.All(p => p.Skinned));
                StringAssert.Contains("描画中", tuner.StatusText.text);
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.CompareButton.transform));
                Assert.IsTrue(HomePanelTuner.ComparingLegacy);
                Assert.IsTrue(menu.PanelParts.All(p => !p.Skinned), "comparison: Unity-drawn panels");
                StringAssert.Contains("従来", tuner.StatusText.text);
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.TargetButton(HomePanelTarget.Crate).transform));
                Assert.AreEqual(HomePanelTarget.Crate, tuner.Selected, "selectable while comparing");
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.CompareButton.transform));
                Assert.IsFalse(HomePanelTuner.ComparingLegacy);
                Assert.IsTrue(menu.PanelParts.All(p => p.Skinned && p.Fill.sprite == menu.panelFill));

                foreach (var t in HomePanelLook.Targets)
                {
                    var others = HomePanelLook.Targets.Where(o => o != t).ToDictionary(o => o, HomePanelLook.Get);
                    yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.TargetButton(t).transform));
                    Assert.AreEqual(t, tuner.Selected, $"{t} selected by a click on it");
                    Assert.AreEqual(HomePanelLook.Default(t), Mathf.RoundToInt(tuner.Slider.value), $"{t}: the slider shows its value");
                    StringAssert.Contains(HomePanelLook.DisplayName(t), tuner.ValueText.text);
                    // Click near the right end of the slider: the value jumps there.
                    yield return MouseClick(mouse, ScreenAtX((RectTransform)tuner.Slider.transform, 0.97f));
                    int high = HomePanelLook.Get(t);
                    Assert.Greater(high, 40, $"{t}: slider moved by the click");
                    yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.MinusButton.transform));
                    Assert.AreEqual(high - 1, HomePanelLook.Get(t), $"{t}: −1 %");
                    yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.PlusButton.transform));
                    Assert.AreEqual(high, HomePanelLook.Get(t), $"{t}: ＋1 %");
                    var part = menu.PanelParts.First(p => p.target == t);
                    if (part.gameObject.activeInHierarchy)
                        Assert.AreEqual(HomePanelLook.FillAlpha(high) * (part.dimmed ? HomePanelPart.DimFactor : 1f), part.ShownAlpha, 1e-3f, $"{t}: drawn fill alpha follows");
                    Assert.AreEqual(1f, part.FrameImage.color.a, $"{t}: frame opaque");
                    foreach (var o in others)
                        Assert.AreEqual(o.Value, HomePanelLook.Get(o.Key), $"{t} changed only itself (not {o.Key})");
                    yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.ResetButton.transform));
                    Assert.AreEqual(HomePanelLook.Default(t), HomePanelLook.Get(t), $"{t}: reset to its default");
                }
                // Values survive switching targets; "すべて初期値" resets every one.
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.TargetButton(HomePanelTarget.Window).transform));
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.MinusButton.transform));
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.TargetButton(HomePanelTarget.Row).transform));
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.TargetButton(HomePanelTarget.Window).transform));
                Assert.AreEqual(HomePanelLook.Default(HomePanelTarget.Window) - 1, HomePanelLook.Get(HomePanelTarget.Window));
                Assert.AreEqual(HomePanelLook.Default(HomePanelTarget.Window) - 1, Mathf.RoundToInt(tuner.Slider.value));
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.ResetAllButton.transform));
                Assert.AreEqual(HomePanelLook.Default(HomePanelTarget.Window), HomePanelLook.Get(HomePanelTarget.Window));
                yield return MouseClick(mouse, ScreenCenter((RectTransform)tuner.CloseButton.transform));
                Assert.IsFalse(tuner.IsOpen);
                // The home menu works as before with the tuner there; nothing saved to the layout.
                yield return MouseClick(mouse, ScreenCenter(menu.Layout.Element(HomeMenu.Crate)));
                Assert.AreEqual("CratePanel", menu.OpenPanelName);
                Assert.IsFalse(UiLayoutStore.HasSaved(HomeMenu.LayoutId));
            }
            finally
            {
                foreach (var pair in saved)
                {
                    if (pair.Value.HasValue)
                        PlayerPrefs.SetInt(HomePanelLook.Key(pair.Key), pair.Value.Value);
                    else
                        PlayerPrefs.DeleteKey(HomePanelLook.Key(pair.Key));
                }
                if (compareSaved.HasValue)
                    PlayerPrefs.SetInt(HomePanelTuner.CompareKey, compareSaved.Value);
                else
                    PlayerPrefs.DeleteKey(HomePanelTuner.CompareKey);
            }
        }
#endif

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
