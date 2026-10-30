#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.Input;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// The development recoil HUD's "反動調整" button and the touch layout editor: while the editor is open the button is
    /// hidden (not drawn, no touches: it sat on the editor's toolbar, its canvas above the menu's), an open HUD closes,
    /// and after Save or Cancel the button is back and the HUD stays closed; repeated editing keeps it right; Cancel puts
    /// the controls back. Driven by the mouse at screen positions (EventSystem), on the test screen (4:3) and with the
    /// canvases laid out for an iPhone 16 Pro (2622 x 1206) and an iPad (2048 x 1536). The touch layout saved in the
    /// Editor is restored after each test (FiringRangeTestBase).
    /// </summary>
    public class LayoutEditorRecoilButtonTests : FiringRangeTestBase
    {
        string folder;
        WeaponBalanceLab lab;
        GameMenu menu;
        TouchLayoutEditor editor;

        public override void Setup()
        {
            base.Setup();
            folder = Path.Combine(Path.GetTempPath(), "DeadlockLayoutRecoil_" + System.Guid.NewGuid().ToString("N"));
        }

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true); // the test's own temp folder
            base.TearDown();
        }

        IEnumerator Load()
        {
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            menu = Object.FindAnyObjectByType<GameMenu>();
            editor = menu.GetComponent<TouchLayoutEditor>();
            yield return null;
            yield return null;
        }

        IEnumerator Click(Vector2 screen)
        {
            Move(mouse.position, screen);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
        }

        static List<GameObject> Under(Vector2 screen)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.ConvertAll(h => h.gameObject);
        }

        GameObject Toggle => lab.Quick.ToggleButton.gameObject;

        TouchLayoutApplier Applier => Object.FindAnyObjectByType<TouchLayoutApplier>(FindObjectsInactive.Include);

        static string Json(TouchLayoutData d) => JsonUtility.ToJson(d);

        void AssertHiddenWhileEditing(Vector2 togglePoint, string what)
        {
            Assert.IsTrue(editor.IsOpen && TouchLayoutEditor.AnyOpen, what + ": editing");
            Assert.IsFalse(Toggle.activeInHierarchy, what + ": the recoil button is hidden (not drawn)");
            foreach (var go in Under(togglePoint))
                Assert.IsFalse(go.transform.IsChildOf(lab.Quick.transform), what + ": nothing of the recoil HUD takes a touch there: " + go.name);
            Assert.IsFalse(lab.Quick.IsOpen, what + ": the HUD is closed");
        }

        void AssertBack(string what)
        {
            Assert.IsFalse(editor.IsOpen || TouchLayoutEditor.AnyOpen, what + ": editor closed");
            Assert.IsTrue(Toggle.activeInHierarchy, what + ": the recoil button is back");
            Assert.IsFalse(lab.Quick.IsOpen, what + ": the HUD did not open by itself");
        }

        [UnityTest]
        public IEnumerator RecoilButton_HiddenWhileEditing_NoTouch_BackAfterSaveAndCancel_Repeated()
        {
            yield return Load();
            Assert.IsTrue(Toggle.activeInHierarchy, "shown normally");
            Vector2 point = ScreenCenter((RectTransform)Toggle.transform);
            Assert.IsTrue(Under(point).Exists(g => g.transform.IsChildOf(Toggle.transform)), "it takes touches normally");
            for (int round = 0; round < 4; round++)
            {
                bool save = round % 2 == 0;
                string what = $"round {round} ({(save ? "save" : "cancel")})";
                string before = Json(Applier.Saved);
                menu.OpenLayoutEditor();
                yield return null;
                AssertHiddenWhileEditing(point, what);
                yield return Click(point); // a tap where the button was
                Assert.IsFalse(lab.Quick.IsOpen, what + ": the tap did not open the HUD");
                Assert.IsTrue(editor.IsOpen, what + ": still editing");
                int dir = round == 0 || round == 3 ? 1 : -1; // from the saved place: out, back (cancelled), back (saved), out (cancelled)
                Assert.IsTrue(editor.MoveBy(TouchLayout.Fire, new Vector2(-20f, 10f) * dir), what + ": a control still moves");
                Assert.IsTrue(editor.Resize(TouchLayout.Fire, dir), what + ": and resizes");
                string edited = Json(editor.Working);
                Assert.AreNotEqual(before, edited);
                yield return null;
                AssertHiddenWhileEditing(point, what + " after editing");
                if (save)
                    editor.ApplyAndClose();
                else
                    editor.CancelAndClose();
                yield return null;
                AssertBack(what);
                Assert.AreEqual(save ? edited : before, Json(Applier.Saved), what + (save ? ": saved" : ": the layout is back as before"));
                Assert.AreEqual(Json(Applier.Saved), Json(Applier.Shown), what + ": the controls show the saved layout");
                menu.Close();
                yield return null;
                Assert.IsTrue(Toggle.activeInHierarchy, what + ": shown after the menu closes");
                Assert.IsTrue(Under(point).Exists(g => g.transform.IsChildOf(Toggle.transform)), what + ": takes touches again");
            }
            yield return Click(point);
            Assert.IsTrue(lab.Quick.IsOpen, "the HUD opens normally afterwards");
            lab.Quick.SetOpen(false);
        }

        [UnityTest]
        public IEnumerator HudOpen_ThenLayoutEditor_TheHudCloses_NothingStuck_ItWorksAfterwards()
        {
            yield return Load();
            Vector2 point = ScreenCenter((RectTransform)Toggle.transform);
            yield return Click(point);
            Assert.IsTrue(lab.Quick.IsOpen);
            lab.Quick.SetExpanded(true);
            yield return null;
            // The pointer over the HUD holds the player's input; the HUD closing must let it go.
            Move(mouse.position, ScreenCenter(lab.Quick.Panel));
            yield return null;
            menu.OpenLayoutEditor();
            yield return null;
            AssertHiddenWhileEditing(point, "HUD open before");
            Assert.IsFalse(lab.Quick.HoldingInputForMouse, "the HUD's hold on the input is released");
            lab.Quick.SetOpen(true);
            Assert.IsFalse(lab.Quick.IsOpen, "the HUD does not open over the layout editor");
            Assert.IsFalse(Toggle.activeInHierarchy);
            Assert.IsTrue(editor.MoveBy(TouchLayout.Jump, new Vector2(15f, 0f)), "the editor works");
            editor.CancelAndClose();
            menu.Close();
            yield return null;
            AssertBack("after cancel");
            Assert.IsFalse(router.IsBlocked, "the player's input is free");
            yield return Click(point);
            Assert.IsTrue(lab.Quick.IsOpen, "the HUD opens again from its button");
            Assert.IsFalse(Toggle.activeInHierarchy, "its button hides while it is open (as before)");
            lab.Quick.SetOpen(false);
            yield return null;
            Assert.IsTrue(Toggle.activeInHierarchy);
        }

        /// <summary>
        /// The menu's and the lab's canvases laid out for an iPhone 16 Pro and an iPad (rendered by a camera into a texture
        /// of that size; batch mode has a fixed screen): the button sits on the toolbar's band when both are shown, it is
        /// hidden while editing, and it is back in the same place after Cancel.
        /// </summary>
        [UnityTest]
        public IEnumerator IPhoneAndIPad_TheButtonWouldSitOnTheToolbar_HiddenWhileEditing_SamePlaceAfter()
        {
            yield return Load();
            var labCanvas = lab.GetComponent<Canvas>();
            var menuCanvas = editor.GetComponentInParent<Canvas>().rootCanvas;
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            try
            {
                foreach (var size in new[] { new Vector2Int(2622, 1206), new Vector2Int(2048, 1536) })
                {
                    var rt = new RenderTexture(size.x, size.y, 16);
                    cam.targetTexture = rt;
                    foreach (var c in new[] { labCanvas, menuCanvas })
                    {
                        c.renderMode = RenderMode.ScreenSpaceCamera;
                        c.worldCamera = cam;
                        c.planeDistance = 1f;
                    }
                    yield return null;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    Rect Pixels(RectTransform r)
                    {
                        var w = new Vector3[4];
                        r.GetWorldCorners(w);
                        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                    }
                    Assert.IsTrue(Toggle.activeInHierarchy, $"{size}: shown normally");
                    Rect toggle = Pixels((RectTransform)Toggle.transform);
                    menu.OpenLayoutEditor();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    var bar = FindChild(menuCanvas.transform, "Toolbar");
                    Assert.IsNotNull(bar, "the editor's toolbar");
                    Rect toolbar = Pixels(bar);
                    bool wouldOverlap = toggle.Overlaps(toolbar);
                    TestContext.WriteLine($"{size}: recoil button {toggle}, toolbar {toolbar}, would overlap: {wouldOverlap}");
                    Assert.IsTrue(wouldOverlap, $"{size}: the case being fixed (the button sits on the toolbar's band)");
                    Assert.IsTrue(editor.IsOpen && TouchLayoutEditor.AnyOpen);
                    Assert.IsFalse(Toggle.activeInHierarchy, $"{size}: hidden while editing");
                    Assert.IsTrue(new Rect(0f, 0f, size.x, size.y).Contains(toolbar.center), $"{size}: the toolbar is on screen");
                    editor.CancelAndClose();
                    menu.Close();
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    AssertBack(size.ToString());
                    Rect after = Pixels((RectTransform)Toggle.transform);
                    Assert.Less(Vector2.Distance(after.center, toggle.center), 0.5f, $"{size}: back in the same place");
                    cam.targetTexture = null;
                    rt.Release();
                }
            }
            finally
            {
                foreach (var c in new[] { labCanvas, menuCanvas })
                    if (c != null)
                    {
                        c.renderMode = RenderMode.ScreenSpaceOverlay;
                        c.worldCamera = null;
                    }
                if (editor != null && editor.IsOpen)
                    editor.CancelAndClose();
                Object.Destroy(camGo);
            }
        }

        static RectTransform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
#endif
