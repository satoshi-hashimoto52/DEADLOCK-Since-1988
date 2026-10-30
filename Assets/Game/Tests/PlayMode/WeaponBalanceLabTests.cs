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
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Weapon balance lab in the firing range, driven by the mouse at the controls' screen positions (EventSystem, as a
    /// finger would): opened from the menu, the weapon table, recoil and falloff editing, slider, graph, test session on a
    /// clone (recoil direction / alternation / strength while firing full auto, reload, weapon switch), end of session,
    /// save / load / reset, input blocked while open and nothing left pressed after closing. Weapon assets stay as they were.
    /// </summary>
    public class WeaponBalanceLabTests : FiringRangeTestBase
    {
        string folder;
        WeaponBalanceLab lab;
        GameMenu menu;

        public override void Setup()
        {
            base.Setup();
            folder = Path.Combine(Path.GetTempPath(), "DeadlockWeaponLabPlay_" + System.Guid.NewGuid().ToString("N"));
        }

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            base.TearDown();
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

        IEnumerator Click(string control)
        {
            var b = lab.ControlButton(control);
            Assert.IsNotNull(b, control);
            yield return Click(ScreenCenter((RectTransform)b.transform));
        }

        static GameObject TopUnder(Vector2 screen)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        IEnumerator OpenLab()
        {
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            menu = Object.FindAnyObjectByType<GameMenu>();
            yield return null;
            menu.Open();
            yield return null;
            Assert.IsNotNull(lab.OpenButton, "the menu offers the lab");
            var logo = menu.transform.Find("MenuLayer/MenuPanel/Window/TitleLogo") as RectTransform;
            if (logo != null)
            {
                var a = new Vector3[4];
                var b = new Vector3[4];
                logo.GetWorldCorners(a);
                ((RectTransform)lab.OpenButton.transform).GetWorldCorners(b);
                Assert.IsFalse(Rect.MinMaxRect(a[0].x, a[0].y, a[2].x, a[2].y).Overlaps(Rect.MinMaxRect(b[0].x, b[0].y, b[2].x, b[2].y)), "clear of the title logo");
            }
            Assert.AreEqual(lab.OpenButton.gameObject, TopUnder(ScreenCenter((RectTransform)lab.OpenButton.transform)).GetComponentInParent<Button>().gameObject);
            yield return Click(ScreenCenter((RectTransform)lab.OpenButton.transform));
            Assert.IsTrue(lab.IsOpen);
            Assert.IsFalse(menu.IsOpen, "the menu closes for the lab");
            Assert.IsTrue(router.IsBlocked, "the player's input is blocked while the lab is open");
        }

        [UnityTest]
        public IEnumerator Lab_EditRecoilAndFalloff_ApplyToTest_FireReloadSwitch_EndTest_AssetsUntouched()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var rifle = gun.Definition;
            var smg = player.GetComponent<DevGunSelection>().Options[1];
            float smgDamage = smg.damage, smgInterval = smg.fireInterval;
            int smgMag = smg.magazineSize;

            // Inside the safe area.
            var c = new Vector3[4];
            lab.Window.GetWorldCorners(c);
            Assert.IsTrue(Screen.safeArea.Contains(c[0]) && Screen.safeArea.Contains(c[2]), "lab window inside the safe area");

            // Table: weapons are rows; a click on the SMG row selects it.
            yield return Click("Cell_proto_smg_0");
            Assert.AreEqual("proto_smg", lab.SelectedId);

            // Performance: +1 damage, +10 RPM.
            yield return Click("Plus_damage");
            yield return Click("Plus_rpm");
            Assert.AreEqual(smgDamage + 1f, lab.Edited("proto_smg").damage);
            Assert.AreEqual(60f / smgInterval + 10f, lab.Edited("proto_smg").rpm, 1e-3f);
            Assert.AreEqual(smgDamage, lab.Current("proto_smg").damage, "the current values stay for comparison");

            // Recoil: up, alternating mode on, strong.
            yield return Click("Tab_Recoil");
            Assert.AreEqual(WeaponBalanceLab.Tab.Recoil, lab.CurrentTab);
            yield return Click("DirV_Up");
            yield return Click("AltToggle");
            yield return Click("Str_Strong");
            var r = lab.Edited("proto_smg").recoil;
            Assert.IsTrue(r.enabled);
            Assert.AreEqual(RecoilAxis.Mixed, r.axis);
            Assert.AreEqual(RecoilVertical.Up, r.vertical);
            Assert.AreEqual(RecoilHorizontal.Alternating, r.horizontal);
            Assert.AreEqual(RecoilStrength.Strong, r.strength);
            Assert.IsTrue(r.IsValid(out _));
            // Slider: a click near the right end sets a large base angle.
            var slider = (RectTransform)lab.PitchSlider.transform;
            var sc = new Vector3[4];
            slider.GetWorldCorners(sc);
            yield return Click(new Vector2(Mathf.Lerp(sc[0].x, sc[2].x, 0.9f), (sc[0].y + sc[2].y) * 0.5f));
            Assert.Greater(lab.Edited("proto_smg").recoil.pitchDegrees, 2f, "slider moved the base angle");
            Type(lab.RecoilEditor, RecoilParam.Pitch, "1.25");
            Assert.AreEqual(1.25f, lab.Edited("proto_smg").recoil.pitchDegrees, 1e-5f, "typed number");

            // Falloff: the example, select point 4, move it, add / remove, an invalid order is refused.
            yield return Click("Tab_Falloff");
            yield return Click("FalloffExample");
            Assert.AreEqual(6, lab.Edited("proto_smg").falloff.points.Count);
            Assert.Greater(lab.GraphSegmentCount, 3, "graph drawn from the data");
            yield return Click("Point_3");
            Assert.AreEqual(3, lab.SelectedPoint);
            yield return Click("DistPlus");
            yield return Click("MulMinus");
            Assert.AreEqual(new FalloffPoint(45f, 0.75f), lab.Edited("proto_smg").falloff.points[3]);
            yield return Click("PointAdd");
            Assert.AreEqual(7, lab.Edited("proto_smg").falloff.points.Count);
            yield return Click("PointRemove");
            Assert.AreEqual(6, lab.Edited("proto_smg").falloff.points.Count);
            var ms = (RectTransform)lab.MultiplierSlider.transform;
            ms.GetWorldCorners(sc);
            yield return Click("Point_5");
            yield return Click(new Vector2(Mathf.Lerp(sc[0].x, sc[2].x, 0.1f), (sc[0].y + sc[2].y) * 0.5f));
            Assert.Less(lab.Edited("proto_smg").falloff.points[5].multiplier, 0.3f, "multiplier slider");
            yield return Click("Point_1");
            for (int i = 0; i < 4; i++)
                yield return Click("DistPlus"); // 10 m → 30 m: past the next point (25 m)
            Assert.IsFalse(lab.Edited("proto_smg").falloff.IsValid(out _));
            yield return Click("Tab_Test");
            yield return Click("Apply");
            Assert.IsFalse(lab.Testing, "an invalid falloff is not applied");
            StringAssert.Contains("適用できません", lab.Message.text);
            yield return Click("Tab_Falloff");
            yield return Click("FalloffExample");

            // Apply: a clone with the edited values is equipped; the assets keep theirs.
            yield return Click("Tab_Test");
            yield return Click("Apply");
            Assert.IsTrue(lab.Testing);
            Assert.AreEqual("proto_smg", gun.Definition.weaponId);
            Assert.AreNotSame(smg, gun.Definition);
            Assert.AreEqual(smgDamage + 1f, gun.Definition.damage);
            Assert.AreEqual(smgDamage, smg.damage, "the SMG asset is not written");
            Assert.AreEqual(smgInterval, smg.fireInterval);
            Assert.IsFalse(smg.recoilProfile.enabled);
            Assert.IsTrue(smg.damageFalloff.IsEmpty);

            // Close: input back, nothing pressed.
            yield return Click("Close");
            Assert.IsFalse(lab.IsOpen);
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(router.Current.FireHeld, "the click on 閉じる does not become a shot");
            Assert.AreEqual(smgMag, gun.Ammo.Loaded, "no shot fired by the lab's clicks");

            // Full auto with the test recoil: up every shot, sideways alternating, strong.
            var kicks = new List<(int index, Vector2 kick)>();
            gun.Fired += () => kicks.Add((gun.ShotIndex - 1, gun.LastRecoil));
            Press(mouse.leftButton);
            yield return Wait(0.6f);
            Release(mouse.leftButton);
            yield return null;
            Assert.GreaterOrEqual(kicks.Count, 4, "full auto keeps firing");
            Assert.AreEqual(smgMag - kicks.Count, gun.Ammo.Loaded, "one round per shot");
            var profile = gun.Definition.recoilProfile;
            foreach (var (index, kick) in kicks)
            {
                Assert.IsTrue(RecoilCalculator.TryCompute(profile, index, gun.AdsBlend, out float p, out float y));
                Assert.AreEqual(p, kick.x, 1e-4f);
                Assert.AreEqual(y, kick.y, 1e-4f);
                Assert.Greater(kick.x, 0f, "up");
                Assert.AreEqual(index % 2 == 0 ? 1f : -1f, Mathf.Sign(kick.y), "right on even shots, left on odd");
            }
            Assert.AreEqual(0, kicks[0].index, "the pattern starts at the first shot");
            Assert.AreEqual(profile.pitchDegrees * RecoilProfile.StrongFactor, kicks[0].kick.x, 1e-4f, "strong");

            // Reload with the clone's values.
            yield return Tap(keyboard.rKey);
            Assert.IsTrue(gun.IsReloading);
            yield return Wait(gun.Definition.reloadTime + 0.2f);
            Assert.AreEqual(smgMag, gun.Ammo.Loaded);

            // End the session: back to the SMG asset.
            lab.Open();
            yield return null;
            yield return Click("Tab_Test");
            yield return Click("EndTest");
            Assert.IsFalse(lab.Testing);
            Assert.AreSame(smg, gun.Definition);

            // Apply again, then switch guns in the menu: the session ends, the official rifle is in hand.
            yield return Click("Apply");
            Assert.IsTrue(lab.Testing);
            yield return Click("Close");
            menu.Open();
            yield return null;
            var devButton = menu.transform.Find("MenuLayer/MenuPanel/Window/DevGunButton");
            yield return Click(ScreenCenter((RectTransform)devButton));
            Assert.AreSame(rifle, gun.Definition, "the menu switch goes to the official rifle");
            Assert.IsNull(lab.AppliedClone, "the test session ended");
            menu.Close();
            yield return null;
        }

        static Vector2 End(List<Vector2> pts) => pts[pts.Count - 1];

        /// <summary>A number typed into an editor's field and confirmed (as the on-screen keyboard's done key does).</summary>
        static void Type(WeaponRecoilEditor editor, RecoilParam p, string text)
        {
            var field = editor.InputOf(p);
            Assert.IsTrue(field.interactable, p + " field usable");
            field.SetTextWithoutNotify(text);
            field.onEndEdit.Invoke(text);
        }

        [UnityTest]
        public IEnumerator RecoilPreview_FollowsEveryEdit_AtAFixedScale_DirectionButtonsStayValid()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            yield return Click("Tab_Recoil");
            var r = lab.Edited("proto_rifle").recoil;
            var current = End(lab.PreviewCurrent);
            Assert.AreEqual(lab.Current("proto_rifle").recoil.pitchDegrees * WeaponBalanceLab.PreviewShots, current.y, 1e-3f, "the current (earlier) recoil drawn for comparison");

            // Strength: strong, then weak; the drawing grows / shrinks with it (fixed scale, no auto-fit).
            yield return Click("Str_Strong");
            Assert.IsTrue(r.enabled);
            Assert.AreEqual(RecoilStrength.Strong, r.strength);
            float strong = End(lab.PreviewHip).y;
            yield return Click("Str_Weak");
            Assert.AreEqual(RecoilStrength.Weak, r.strength);
            float weak = End(lab.PreviewHip).y;
            Assert.AreEqual(strong * 0.5f, weak, 1e-3f, "weak 0.7 is half of strong 1.4");
            yield return null; // the previous drawing is destroyed at the end of the click's frame
            var dot = (RectTransform)lab.Window.Find("TabArea/Page_Recoil/Pattern/Hip_" + WeaponBalanceLab.PreviewShots);
            Assert.AreEqual(Mathf.Min(205f, weak * WeaponBalanceLab.PreviewPixelsPerDegree), dot.anchoredPosition.y, 0.01f, "drawn at 10 px per degree");
            // Base angle and ADS factor.
            Type(lab.RecoilEditor, RecoilParam.Pitch, "0.75");
            Assert.Greater(End(lab.PreviewHip).y, weak, "a larger base angle shows at once");
            float ads = End(lab.PreviewAds).y;
            Assert.AreEqual(End(lab.PreviewHip).y * r.adsMultiplier, ads, 1e-3f, "ADS series = hip × ADS factor");
            Type(lab.RecoilEditor, RecoilParam.Ads, "0.4");
            Assert.Less(End(lab.PreviewAds).y, ads);

            // Up / down / none and left / right / none picked separately; the axis follows.
            yield return Click("DirV_Down");
            Assert.AreEqual((RecoilAxis.Vertical, RecoilVertical.Down, RecoilHorizontal.None), (r.axis, r.vertical, r.horizontal));
            Assert.Less(End(lab.PreviewHip).y, 0f, "down");
            yield return Click("DirH_Left");
            Assert.AreEqual((RecoilAxis.Diagonal, RecoilVertical.Down, RecoilHorizontal.Left), (r.axis, r.vertical, r.horizontal), "下＋左");
            Assert.Less(End(lab.PreviewHip).x, 0f);
            Assert.Less(End(lab.PreviewHip).y, 0f);
            yield return Click("DirV_Up");
            yield return Click("DirH_Right");
            Assert.AreEqual((RecoilAxis.Diagonal, RecoilVertical.Up, RecoilHorizontal.Right), (r.axis, r.vertical, r.horizontal), "上＋右");
            Assert.Greater(End(lab.PreviewHip).x, 0f);
            yield return Click("DirV_None");
            Assert.AreEqual((RecoilAxis.Horizontal, RecoilVertical.None, RecoilHorizontal.Right), (r.axis, r.vertical, r.horizontal));
            Assert.AreEqual(0f, End(lab.PreviewHip).y);
            // Both none is refused (the profile stays valid, the message says why).
            yield return Click("DirH_None");
            Assert.AreEqual(RecoilHorizontal.Right, r.horizontal, "not both none");
            StringAssert.Contains("両方", lab.Message.text);
            Assert.IsTrue(r.IsValid(out _));
            // Alternating mode: separate from left / right.
            yield return Click("AltToggle");
            Assert.AreEqual((RecoilAxis.Horizontal, RecoilHorizontal.Alternating), (r.axis, r.horizontal));
            Assert.Greater(lab.PreviewHip[1].x, 0f, "alternating: right first");
            Assert.AreEqual(0f, lab.PreviewHip[2].x, 1e-4f, "then back left");
            yield return Click("DirV_Up");
            Assert.AreEqual(RecoilAxis.Mixed, r.axis, "上＋左右交互");
            yield return Click("DirH_Left");
            Assert.AreEqual(RecoilHorizontal.Left, r.horizontal, "a side turns the alternating mode off");
            // Every combination the buttons can reach is valid.
            foreach (var v in new[] { "DirV_None", "DirV_Up", "DirV_Down" })
                foreach (var h in new[] { "DirH_None", "DirH_Left", "DirH_Right", "AltToggle" })
                {
                    yield return Click(v);
                    yield return Click(h);
                    Assert.IsTrue(r.IsValid(out string e1), $"{v} {h}: {e1}");
                }
            yield return Click("Close");
        }

        [UnityTest]
        public IEnumerator Lab_PickerClearOfTitleFpsClose_LongRecoilSliders_PositionMatchesValue()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            var fps = new GameObject("FpsForTest").AddComponent<FrameRateMonitor>(); // the development FPS read-out (top centre)
            try
            {
                yield return null;
                var fpsText = fps.GetComponentInChildren<Text>();
                fpsText.text = "DEV  60.0 fps  worst 16.7 ms  target 60";
                lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
                yield return null;
                lab.Open();
                yield return null;

                // Header: the drop-down overlaps neither the title, the FPS read-out nor 閉じる; it stays in the window.
                var pick = ScreenRect((RectTransform)lab.Picker.Header.transform);
                var c = new Vector3[4];
                fpsText.rectTransform.GetWorldCorners(c);
                float half = fpsText.preferredWidth * 0.5f * fpsText.rectTransform.lossyScale.x, mid = (c[0].x + c[2].x) * 0.5f;
                var fpsRect = Rect.MinMaxRect(mid - half, c[1].y - fpsText.preferredHeight * fpsText.rectTransform.lossyScale.y, mid + half, c[1].y);
                var tr = ScreenRect(lab.Title.rectTransform);
                var titleRect = new Rect(tr.xMin, tr.yMin, lab.Title.preferredWidth * lab.Title.rectTransform.lossyScale.x, tr.height);
                var close = ScreenRect((RectTransform)lab.ControlButton("Close").transform);
                TestContext.WriteLine($"screen {Screen.width}x{Screen.height}: picker {pick} fps {fpsRect} title {titleRect} close {close} text '{lab.Picker.Header.GetComponentInChildren<Text>().text}'");
                Assert.IsFalse(pick.Overlaps(fpsRect), "clear of the FPS read-out");
                Assert.IsFalse(pick.Overlaps(titleRect), "clear of the title");
                Assert.IsFalse(pick.Overlaps(close), "clear of 閉じる");
                var win = ScreenRect(lab.Window);
                Assert.IsTrue(win.Contains(pick.min) && win.Contains(pick.max), "inside the window");
                var label = lab.Picker.Header.GetComponentInChildren<Text>();
                Assert.LessOrEqual(label.preferredWidth, ((RectTransform)lab.Picker.Header.transform).rect.width, "the name fits (cut with … when long)");
                yield return Click(ScreenCenter((RectTransform)lab.Picker.Header.transform));
                yield return null;
                var list = ScreenRect(lab.Picker.List);
                Assert.IsTrue(win.Contains(list.min) && win.Contains(list.max), $"the open list stays in the window: list {list} window {win}");
                foreach (var id in lab.WeaponIds)
                    Assert.AreEqual(lab.WeaponLabel(id), lab.ControlButton("WeaponItem_" + id).GetComponentInChildren<Text>().text, "full names in the list");
                lab.Picker.Close();

                // Recoil tab: the sliders take the room up to the pattern; A in one column.
                lab.ShowTab(WeaponBalanceLab.Tab.Recoil);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var ed = lab.RecoilEditor;
                float dirW = ((RectTransform)ed.SliderOf(RecoilParam.Pitch).transform).rect.width, adsW = ((RectTransform)ed.SliderOf(RecoilParam.Ads).transform).rect.width;
                TestContext.WriteLine($"slider lengths (units): direction {dirW:0} (before 162), ADS / recovery {adsW:0} (before 322)");
                Assert.GreaterOrEqual(dirW, 162f * 2f - 0.5f, "direction sliders at least twice as long");
                Assert.GreaterOrEqual(adsW, 322f * 1.4f, "the other sliders longer too");
                var pattern = ScreenRect(lab.Pattern);
                foreach (RecoilParam p in System.Enum.GetValues(typeof(RecoilParam)))
                    Assert.IsFalse(ScreenRect((RectTransform)ed.SliderOf(p).transform).Overlaps(pattern), p + " slider clear of the pattern");
                foreach (RecoilRow row in System.Enum.GetValues(typeof(RecoilRow)))
                {
                    Assert.AreEqual(ScreenRect(ed.AText(RecoilRow.Vertical).rectTransform).xMin, ScreenRect(ed.AText(row).rectTransform).xMin, 0.5f, row + ": A column aligned");
                    Assert.IsFalse(ScreenRect(ed.AText(row).rectTransform).Overlaps(pattern), row + ": A clear of the pattern");
                }
                Assert.AreEqual(ScreenRect((RectTransform)ed.InputOf(RecoilParam.Pitch).transform).xMin, ScreenRect((RectTransform)ed.InputOf(RecoilParam.Ads).transform).xMin, 0.5f,
                    "B column aligned");

                // Where the finger is, is the value: fine 0.01° steps, ends of the window, coarse middle.
                yield return Click("DirV_Up");
                Type(ed, RecoilParam.Pitch, "1.00");
                yield return Click("FineToggle");
                Assert.IsTrue(lab.FineAdjust);
                var slider = ed.SliderOf(RecoilParam.Pitch);
                var area = ScreenRect((RectTransform)slider.transform.Find("Handle Slide Area"));
                var r = lab.Edited(lab.SelectedId).recoil;
                foreach (var (index, expected) in new[] { (37, 0.87f), (0, 0.5f), (100, 1.5f), (51, 1.01f) })
                {
                    yield return Click(new Vector2(Mathf.Lerp(area.xMin, area.xMax, index / 100f), area.center.y));
                    Assert.AreEqual(expected, r.pitchDegrees, 1e-4f, $"fine step {index}");
                    Assert.AreEqual(index, slider.value, 0.01f, "the handle where the value is");
                }
                yield return Click("FineToggle");
                Assert.IsFalse(lab.FineAdjust);
                area = ScreenRect((RectTransform)slider.transform.Find("Handle Slide Area"));
                yield return Click(new Vector2(Mathf.Lerp(area.xMin, area.xMax, 0.5f), area.center.y));
                Assert.AreEqual(5f, r.pitchDegrees, 1e-4f, "coarse: the middle is 5.00°");
                Assert.AreEqual("5.00", ed.InputOf(RecoilParam.Pitch).text);
                Assert.AreEqual("上 5.00°", RecoilParams.RowText(r, RecoilRow.Vertical, 9f), "B and its text agree");
                yield return Click("Close");
            }
            finally
            {
                Object.Destroy(fps.gameObject);
            }
        }

        IEnumerator QuickClick(string control)
        {
            var b = lab.Quick.Control(control);
            Assert.IsNotNull(b, control);
            lab.Quick.Reveal((RectTransform)b.transform); // scrolled into view when it is in the body
            yield return null;
            yield return Click(ScreenCenter((RectTransform)b.transform));
        }

        static Rect ScreenRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        IEnumerator OpenQuick()
        {
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            menu = Object.FindAnyObjectByType<GameMenu>();
            yield return null;
            var quick = lab.Quick;
            Assert.IsFalse(quick.IsOpen, "hidden at first");
            yield return Click(ScreenCenter((RectTransform)quick.ToggleButton.transform));
            Assert.IsTrue(quick.IsOpen);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// The HUD's rows are the full lab's (same layout, row height and font); every shown control takes touches on its own
        /// drawn area, and the areas of different controls never overlap (neighbours keep a gap). The face is half transparent.
        /// </summary>
        void AssertLikeTheLab(WeaponBalanceQuickPanel quick)
        {
            Assert.AreEqual(WeaponRecoilEditor.EditorLayout.Inline, quick.Editor.Layout, "the lab's row layout");
            Assert.AreEqual(lab.RecoilEditor.Height, quick.Editor.Height, 0.01f, "the lab's row heights");
            Assert.AreEqual(WeaponBalanceQuickPanel.Font, quick.Editor.InputOf(RecoilParam.Pitch).textComponent.fontSize, "the panel's own (larger) font");
            Assert.Greater(quick.Editor.InputOf(RecoilParam.Pitch).textComponent.fontSize, lab.RecoilEditor.InputOf(RecoilParam.Pitch).textComponent.fontSize);
            var cells = new List<(string name, Rect cell)>();
            float k = UnitsPerPixel(quick), smallest = float.MaxValue;
            foreach (var sel in quick.Panel.GetComponentsInChildren<Selectable>(false))
            {
                if (!IsShown(quick, (RectTransform)sel.transform))
                    continue;
                var cell = Union(ScreenRect(TouchUi.TouchAreaOf(sel)), ScreenRect((RectTransform)sel.transform));
                smallest = Mathf.Min(smallest, cell.height * k);
                Assert.GreaterOrEqual(cell.height * k, 30f - 0.5f, sel.name + " touch height (the lab's smallest, its header row)");
                foreach (var (other, oc) in cells)
                {
                    var o = Rect.MinMaxRect(Mathf.Max(cell.xMin, oc.xMin), Mathf.Max(cell.yMin, oc.yMin), Mathf.Min(cell.xMax, oc.xMax), Mathf.Min(cell.yMax, oc.yMax));
                    Assert.IsFalse(o.width > 0.5f && o.height > 0.5f, $"touch areas of {sel.name} and {other} overlap");
                }
                cells.Add((sel.name, cell));
            }
            TestContext.WriteLine($"{cells.Count} controls shown, smallest touch height {smallest:0.0} units");
            Assert.AreEqual(0.5f, quick.Background.color.a, 1e-4f, "background alpha 0.5");
            Assert.IsNull(quick.Background.GetComponent<BaseMeshEffect>(), "nothing redraws the face (an outline effect would hide the transparency)");
            Assert.IsNull(quick.Panel.GetComponentInParent<CanvasGroup>(), "no group alpha over the contents");
            foreach (var t in quick.Panel.GetComponentsInChildren<Text>(true))
                if (t.GetComponentInParent<InputField>(true) == null) // a field's hint text is dimmed on purpose
                    Assert.AreEqual(1f, t.color.a, 1e-4f, t.name + " opaque text");
            foreach (var b in quick.Panel.GetComponentsInChildren<Button>(true))
                Assert.AreEqual(1f, ((Image)b.targetGraphic).color.a, 1e-4f, b.name + " opaque button");
            foreach (var sl in quick.Panel.GetComponentsInChildren<Slider>(true))
                Assert.AreEqual(1f, ((Image)sl.targetGraphic).color.a, 1e-4f, sl.name + " opaque handle");
        }

        static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        /// <summary>Canvas units per screen pixel of the lab's canvas.</summary>
        static float UnitsPerPixel(WeaponBalanceQuickPanel quick) => 1f / quick.Panel.GetComponentInParent<Canvas>().scaleFactor;

        /// <summary>Inside the panel and, in the scrolling body, inside its visible part.</summary>
        static bool IsShown(WeaponBalanceQuickPanel quick, RectTransform r)
        {
            var sr = ScreenRect(r);
            var clip = r.IsChildOf(quick.Viewport) ? ScreenRect(quick.Viewport) : ScreenRect(quick.Panel);
            return sr.yMin >= clip.yMin - 0.5f && sr.yMax <= clip.yMax + 0.5f;
        }

        void AssertClearAndInside(WeaponBalanceQuickPanel quick, string what)
        {
            var pr = ScreenRect(quick.Panel);
            TestContext.WriteLine($"{what}: screen {Screen.width}x{Screen.height} safe {Screen.safeArea} panel {pr} anchored {quick.Panel.anchoredPosition} size {quick.Panel.sizeDelta}; controls {string.Join(" ", quick.Obstacles())} texts {string.Join(" ", quick.TextObstacles())}");
            var safeArea = Screen.safeArea;
            Assert.IsTrue(pr.xMin >= safeArea.xMin - 0.5f && pr.yMin >= safeArea.yMin - 0.5f && pr.xMax <= safeArea.xMax + 0.5f && pr.yMax <= safeArea.yMax + 0.5f,
                what + ": inside the safe area (its edges included)");
            var centre = new Vector2(Screen.width, Screen.height) * 0.5f;
            Assert.IsFalse(pr.Contains(centre), what + ": the crosshair stays visible");
            Assert.IsFalse(pr.Overlaps(ScreenRect((RectTransform)menu.MenuButton.transform)), what + ": clear of the menu button");
        }

        [UnityTest]
        public IEnumerator QuickHud_CompactThenExpanded_DropDownFromTheList_SameEditorAsTheLab()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var quick = lab.Quick;
            Assert.IsFalse(router.IsBlocked, "an open HUD alone does not stop the game");
            // Compact: weapon, state, A's main values and recovery amount only.
            Assert.IsFalse(quick.IsExpanded);
            Assert.IsFalse(quick.DetailText.gameObject.activeInHierarchy, "measured values only when expanded");
            Assert.IsFalse(quick.Editor.Root.gameObject.activeInHierarchy);
            StringAssert.Contains("試射なし", quick.StatusText.text);
            StringAssert.Contains("回復量 50%", quick.StatusText.text);
            StringAssert.Contains("\nB ＝A", quick.StatusText.text, "B shown (same as A)");
            StringAssert.Contains("PROTO RIFLE", quick.Picker.Header.GetComponentInChildren<Text>().text);
            AssertClearAndInside(quick, "compact");
            Assert.AreEqual(WeaponBalanceQuickPanel.CompactHeight, quick.Panel.rect.height, 0.5f);

            // Drop-down: made from the registered weapons.
            yield return QuickClick("QWeaponPick");
            Assert.IsTrue(quick.Picker.IsListOpen);
            CollectionAssert.AreEqual(lab.WeaponIds, quick.Picker.Listed);
            var options = player.GetComponent<DevGunSelection>().Options;
            Assert.AreEqual(options.Count, quick.Picker.Listed.Count, "every gun of the scene's list");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_smg");
            Assert.AreEqual("proto_smg", lab.SelectedId);
            Assert.AreEqual("proto_smg", gun.Definition.weaponId, "the gun in hand follows the choice");
            Assert.IsFalse(quick.Picker.IsListOpen);
            StringAssert.Contains("SMG", quick.Picker.Header.GetComponentInChildren<Text>().text);

            // Expanded: inside the safe area, crosshair clear; the editor rows are there.
            yield return QuickClick("QExpand");
            Assert.IsTrue(quick.IsExpanded);
            yield return null;
            AssertClearAndInside(quick, "expanded");
            Assert.GreaterOrEqual(quick.Panel.rect.height, WeaponBalanceQuickPanel.MinExpandedHeight - 0.5f);
            Assert.LessOrEqual(quick.Panel.rect.height, quick.FullExpandedHeight + 0.5f);
            AssertLikeTheLab(quick);
            StringAssert.Contains("実測", quick.DetailText.text);
            StringAssert.Contains("回復速度", quick.DetailText.text);

            // SMG B edited on the HUD; the rifle's B and both A stay.
            yield return QuickClick("QDirV_Down");
            yield return QuickClick("QStr_Weak");
            var smgB = lab.Edited("proto_smg").recoil;
            Assert.AreEqual((RecoilVertical.Down, RecoilStrength.Weak), (smgB.vertical, smgB.strength));
            Assert.IsFalse(lab.Edited("proto_rifle").recoil.enabled, "the rifle's B is its own");
            Assert.IsFalse(lab.Applied("proto_smg").recoil.enabled, "A does not follow B");
            Assert.IsTrue(quick.Editor.RowDiffers(RecoilRow.Vertical), "the difference is shown at once");
            Assert.IsTrue(quick.Editor.RowDiffers(RecoilRow.Strength));
            Assert.IsFalse(quick.Editor.RowDiffers(RecoilRow.Ads));
            StringAssert.Contains("B未適用", quick.StatusText.text);

            // Back to the rifle: its own A / B.
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_rifle");
            Assert.AreEqual("proto_rifle", lab.SelectedId);
            Assert.IsFalse(quick.Editor.RowDiffers(RecoilRow.Vertical));
            Assert.AreEqual(RecoilVertical.Down, lab.Edited("proto_smg").recoil.vertical, "SMG B kept");

            // Direction + slider: none disables the slider and the number, the angle is kept.
            yield return QuickClick("QDirV_Up");
            yield return QuickClick("QDirH_Right");
            Type(quick.Editor, RecoilParam.Yaw, "0.3");
            var r = lab.Edited("proto_rifle").recoil;
            Assert.AreEqual(0.3f, r.yawDegrees, 1e-5f);
            yield return QuickClick("QDirH_None");
            Assert.IsFalse(quick.Editor.SliderOf(RecoilParam.Yaw).interactable, "none: slider off");
            Assert.IsFalse(quick.Editor.InputOf(RecoilParam.Yaw).interactable, "none: number off");
            Assert.AreEqual(0.3f, r.yawDegrees, 1e-5f, "the angle is kept");
            Assert.IsFalse(lab.TrySetValue(RecoilParam.Yaw, 1f), "refused while none");
            StringAssert.Contains("なし", lab.Message.text);
            yield return QuickClick("QDirH_Right");
            Assert.IsTrue(quick.Editor.SliderOf(RecoilParam.Yaw).interactable);
            Assert.AreEqual(0.3f, r.yawDegrees, 1e-5f);
            // Typed numbers: outside the range refused, B unchanged.
            Type(quick.Editor, RecoilParam.Pitch, "12");
            Assert.AreEqual(0.7f, r.pitchDegrees, 1e-5f, "12° refused");
            StringAssert.Contains("範囲", lab.Message.text);
            Assert.IsTrue(lab.MessageIsError);
            Type(quick.Editor, RecoilParam.Pitch, "0.9");
            Assert.AreEqual(0.9f, r.pitchDegrees, 1e-5f);

            // Coarse / fine: the slider's range and step.
            var slider = quick.Editor.SliderOf(RecoilParam.Pitch);
            Assert.AreEqual(200f, slider.maxValue, "coarse: 0–10° at 0.05°");
            quick.Reveal((RectTransform)slider.transform);
            yield return null;
            yield return QuickClick("QFineToggle");
            Assert.IsTrue(lab.FineAdjust);
            Assert.AreEqual(100f, slider.maxValue, "fine: ±0.5° at 0.01°");
            quick.Reveal((RectTransform)slider.transform);
            yield return null;
            var sr = ScreenRect((RectTransform)slider.transform);
            yield return Click(new Vector2(Mathf.Lerp(sr.xMin, sr.xMax, 0.85f), sr.center.y));
            Assert.Greater(r.pitchDegrees, 0.9f);
            Assert.LessOrEqual(r.pitchDegrees, 1.4f + 1e-4f, "fine: stays in the window");
            Assert.AreEqual(r.pitchDegrees * 100f, Mathf.Round(r.pitchDegrees * 100f), 1e-2f, "0.01° steps");
            Type(quick.Editor, RecoilParam.Pitch, "0.9");
            yield return QuickClick("QFineToggle");
            Assert.IsFalse(lab.FineAdjust);

            // The full lab shows the same B (same editor rows), and its edits show on the HUD.
            lab.Open();
            yield return null;
            Assert.IsFalse(quick.IsOpen, "the full lab replaces the HUD");
            lab.ShowTab(WeaponBalanceLab.Tab.Recoil);
            yield return null;
            Assert.AreEqual("0.90", lab.RecoilEditor.InputOf(RecoilParam.Pitch).text);
            Assert.AreEqual(lab.Quick.Editor.AText(RecoilRow.Vertical).text, lab.RecoilEditor.AText(RecoilRow.Vertical).text);
            Type(lab.RecoilEditor, RecoilParam.RecoveryAmount, "30"); // percent
            yield return Click("Close");
            yield return Click(ScreenCenter((RectTransform)quick.ToggleButton.transform));
            yield return null;
            Assert.AreEqual("30", quick.Editor.InputOf(RecoilParam.RecoveryAmount).text, "edited in the lab, seen on the HUD");
            Assert.AreEqual("50%", quick.Editor.AText(RecoilRow.RecoveryAmount).text, "A in the same unit");
            Assert.IsTrue(quick.IsExpanded, "the HUD keeps its state");

            // Mouse over the panel holds the player's input (a click there never shoots); leaving gives it back.
            int loaded = gun.Ammo.Loaded;
            Move(mouse.position, ScreenCenter(quick.Panel));
            yield return null;
            yield return null;
            Assert.IsTrue(router.IsBlocked && quick.HoldingInputForMouse);
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "no shot through the panel");
            yield return QuickClick("QFold");
            Assert.IsFalse(quick.IsOpen);
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(router.Current.FireHeld);
            Assert.IsFalse(lab.Testing, "nothing applied by editing");
        }

        [UnityTest]
        public IEnumerator AB_OnlyApplyChangesTheTest_RevertBToA_AimReset_EndBackToOfficial()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var quick = lab.Quick;
            var rifle = gun.Definition;
            yield return QuickClick("QExpand");
            yield return null;
            var b = lab.Edited("proto_rifle").recoil;
            yield return QuickClick("QDirV_Up");
            yield return QuickClick("QDirH_Right");
            yield return QuickClick("QStr_Strong");
            Type(quick.Editor, RecoilParam.Pitch, "0.9");
            Type(quick.Editor, RecoilParam.Yaw, "0.3");
            Assert.AreSame(rifle, gun.Definition, "editing B does not touch the gun");

            // Apply B: A becomes B, the test fires with it.
            yield return QuickClick("QApply");
            Assert.IsTrue(lab.Testing);
            Assert.IsTrue(lab.AppliedMatchesEdited);
            Assert.AreEqual(0, lab.RecoilDiffCount);
            StringAssert.Contains("B＝A", quick.StatusText.text);
            StringAssert.Contains("Bを適用済み", quick.StatusText.text);
            Assert.AreEqual("右 0.30°", quick.Editor.AText(RecoilRow.Horizontal).text);

            // Edit B: A and the running test stay; the difference shows at once.
            Type(quick.Editor, RecoilParam.RecoveryAmount, "55");
            Type(quick.Editor, RecoilParam.Pitch, "2");
            Assert.AreEqual(0.5f, gun.Definition.recoilProfile.recoveryFraction, 1e-5f, "not applied before 'Bを試射に適用'");
            Assert.AreEqual(0.9f, gun.Definition.recoilProfile.pitchDegrees, 1e-5f);
            Assert.AreEqual(0.9f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f, "A unchanged");
            Assert.AreEqual(2, lab.RecoilDiffCount);
            Assert.IsTrue(quick.Editor.RowDiffers(RecoilRow.Vertical) && quick.Editor.RowDiffers(RecoilRow.RecoveryAmount));
            StringAssert.Contains("B未適用", quick.StatusText.text);

            // B back to A: only B changes.
            yield return QuickClick("QRevert");
            b = lab.Edited("proto_rifle").recoil;
            Assert.AreEqual(0.9f, b.pitchDegrees, 1e-5f);
            Assert.AreEqual(0.5f, b.recoveryFraction, 1e-5f);
            Assert.AreEqual(0, lab.RecoilDiffCount);
            Assert.AreNotSame(lab.Applied("proto_rifle").recoil, b, "A and B stay separate data");
            yield return QuickClick("QFold");

            // Fire with A; then the aim reset: same direction, recoil cleared, pattern from the first shot.
            var reference = lab.AimReference;
            yield return Wait(0.7f);
            Press(mouse.leftButton);
            yield return Wait(0.35f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(0.9f * 1.4f, gun.LastRecoil.x, 1e-4f, "A's values reach the shot");
            Assert.Greater(gun.ShotIndex, 1);
            Assert.Greater(motor.Pitch - reference.y, 1f, "the aim climbed");
            int ammo = gun.Ammo.Loaded;
            lab.Quick.SetOpen(true);
            yield return null;
            yield return null;
            yield return QuickClick("QAimReset");
            Assert.AreEqual(reference.x, motor.Yaw, 1e-4f);
            Assert.AreEqual(reference.y, motor.Pitch, 1e-4f);
            Assert.AreEqual(Vector2.zero, motor.RecoveryRemaining, "recovery still running cleared");
            Assert.AreEqual(0, gun.ShotIndex, "the next shot starts the pattern");
            Assert.AreEqual(ammo, gun.Ammo.Loaded, "ammo untouched");
            Assert.IsTrue(lab.Testing, "the test goes on");
            Assert.AreEqual(0.9f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f, "parameters untouched");
            yield return null;
            Assert.AreEqual(reference.y, motor.Pitch, 1e-3f, "no kick left over in the next frame");
            yield return QuickClick("QFold");
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(0.9f * 1.4f, gun.LastRecoil.x, 1e-4f, "first shot of the pattern again");
            Assert.AreEqual(0.3f * 1.4f, gun.LastRecoil.y, 1e-4f);

            // Apply the edited B: the next shot uses it.
            lab.Quick.SetOpen(true);
            yield return null;
            yield return null;
            Type(quick.Editor, RecoilParam.RecoveryAmount, "55");
            Type(quick.Editor, RecoilParam.RecoverySpeed, "7");
            yield return QuickClick("QApply");
            Assert.AreEqual(0.55f, gun.Definition.recoilProfile.recoveryFraction, 1e-5f);
            Assert.AreEqual(7f, gun.Definition.recoilProfile.recoverySpeed, 1e-5f);
            Assert.AreEqual(0.55f, lab.Applied("proto_rifle").recoil.recoveryFraction, 1e-5f, "A updated to the new values");
            yield return QuickClick("QAimReset");
            yield return QuickClick("QFold");
            yield return Wait(0.7f);
            float p0 = motor.Pitch;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return Wait(1.2f);
            Assert.AreEqual(7f, motor.CurrentRecoverySpeed);
            Assert.AreEqual(0.9f * 1.4f * (1f - 0.55f), motor.Pitch - p0, 0.02f, "45 % of the kick stays");

            // End: the official rifle, A = official, B kept.
            lab.EndTest();
            Assert.IsFalse(lab.Testing);
            Assert.AreSame(rifle, gun.Definition);
            Assert.IsFalse(gun.Definition.recoilProfile.enabled, "the asset was never written");
            Assert.IsFalse(lab.Applied("proto_rifle").recoil.enabled, "A is the official values again");
            Assert.AreEqual(0.55f, lab.Edited("proto_rifle").recoil.recoveryFraction, 1e-5f, "B kept for another try");
            Assert.IsFalse(router.IsBlocked);
        }

        /// <summary>Weapon, A, B, the gun's definition, shot number, aim, pending recovery and the last kick, in one line.</summary>
        string RecoilState(string what)
        {
            string R(RecoilProfile r) => r.enabled
                ? $"{RecoilCalculator.DirectionName(r)}/{r.strength} pitch {r.pitchDegrees:0.00} yaw {r.yawDegrees:0.00} ads {r.adsMultiplier:0.00} rec {r.recoveryFraction:0.00}@{r.recoverySpeed:0.0}"
                : $"legacy pitch {r.pitchDegrees:0.00} yaw {r.yawDegrees:0.00}";
            var id = lab.SelectedId;
            return $"{what}: weapon {id} gun={gun.Definition.name}({gun.Definition.weaponId}) A[{R(lab.Applied(id).recoil)}] B[{R(lab.Edited(id).recoil)}] " +
                   $"shot {gun.ShotIndex} aim yaw {motor.Yaw:0.000} pitch {motor.Pitch:0.000} recovering ({motor.RecoveryRemaining.x:0.000}, {motor.RecoveryRemaining.y:0.000}) " +
                   $"last kick ({gun.LastRecoil.x:0.000}, {gun.LastRecoil.y:0.000}) ads {gun.AdsBlend:0.00} ammo {gun.Ammo.Loaded}";
        }

        [UnityTest]
        public IEnumerator RecoilState_KeptThroughEditingB_ApplyingB_AndBToOfficial()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            yield return null;
            var asset = gun.Definition;
            lab.SetVertical(RecoilVertical.Up);
            lab.SetHorizontal(RecoilHorizontal.Right);
            lab.SetStrength(RecoilStrength.Strong);
            Assert.IsTrue(lab.TrySetValue(RecoilParam.Pitch, 0.9f));
            Assert.IsTrue(lab.TrySetValue(RecoilParam.RecoverySpeed, 3f));
            lab.ApplyToTest();
            yield return Wait(0.7f);
            TestContext.WriteLine(RecoilState("0 applied, before firing"));

            // A burst, the trigger kept held through every step below.
            Press(mouse.leftButton);
            yield return Wait(0.35f);
            string s1 = RecoilState("1 firing");
            TestContext.WriteLine(s1);
            int shot = gun.ShotIndex;
            float yaw = motor.Yaw, pitch = motor.Pitch;
            var recovering = motor.RecoveryRemaining;
            var lastKick = gun.LastRecoil;
            int ammo = gun.Ammo.Loaded;
            Assert.Greater(shot, 2, "a few shots into the pattern");
            Assert.Greater(recovering.y, 0f, "recovery under way");

            // Editing B: nothing of the gun or the aim changes.
            Assert.IsTrue(lab.TrySetValue(RecoilParam.Pitch, 2f));
            TestContext.WriteLine(RecoilState("2 B edited (pitch 2)"));
            Assert.AreEqual(0.9f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f, "A unchanged");
            Assert.AreEqual(0.9f, gun.Definition.recoilProfile.pitchDegrees, 1e-5f, "the gun fires A");
            Assert.AreEqual(shot, gun.ShotIndex);
            Assert.AreEqual(yaw, motor.Yaw);
            Assert.AreEqual(pitch, motor.Pitch);
            Assert.AreEqual(recovering, motor.RecoveryRemaining);

            // Applying B: A = B from the next shot; shot number, aim, recovery, ADS, ammo and the held trigger carry on.
            lab.ApplyToTest();
            string s3 = RecoilState("3 B applied");
            TestContext.WriteLine(s3);
            Assert.AreEqual(2f, gun.Definition.recoilProfile.pitchDegrees, 1e-5f);
            Assert.AreEqual(2f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f);
            Assert.AreEqual(shot, gun.ShotIndex, "shot number kept");
            Assert.AreEqual(yaw, motor.Yaw);
            Assert.AreEqual(pitch, motor.Pitch);
            Assert.AreEqual(recovering, motor.RecoveryRemaining, "recovery kept");
            Assert.AreEqual(lastKick, gun.LastRecoil);
            Assert.AreEqual(ammo, gun.Ammo.Loaded, "rounds kept");
            yield return Wait(0.25f);
            TestContext.WriteLine(RecoilState("4 still firing"));
            Assert.Greater(gun.ShotIndex, shot, "the burst goes on with the held trigger");
            Assert.IsTrue(RecoilCalculator.TryCompute(gun.Definition.recoilProfile, gun.ShotIndex - 1, gun.AdsBlend, out float kp, out float ky));
            Assert.AreEqual(kp, gun.LastRecoil.x, 1e-4f, "the new values, at the shot's place in the pattern");
            Assert.AreEqual(ky, gun.LastRecoil.y, 1e-4f);

            // B back to the official values: only B changes.
            shot = gun.ShotIndex;
            yaw = motor.Yaw;
            pitch = motor.Pitch;
            recovering = motor.RecoveryRemaining;
            var applied = gun.Definition;
            lab.ResetEdited();
            TestContext.WriteLine(RecoilState("5 B to official"));
            Assert.IsFalse(lab.Edited("proto_rifle").recoil.enabled, "B = the official (earlier) recoil");
            Assert.AreEqual(2f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f, "A unchanged");
            Assert.AreSame(applied, gun.Definition, "the test gun unchanged");
            Assert.AreEqual(shot, gun.ShotIndex);
            Assert.AreEqual(yaw, motor.Yaw);
            Assert.AreEqual(pitch, motor.Pitch);
            Assert.AreEqual(recovering, motor.RecoveryRemaining);
            Release(mouse.leftButton);
            yield return null;
            Assert.IsFalse(asset.recoilProfile.enabled, "the weapon asset was never written");
        }

        /// <summary>Mean time between the shots of one held trigger (s), and how many were fired.</summary>
        IEnumerator Burst(float seconds, List<float> times)
        {
            void OnFired() => times.Add(Time.time);
            gun.Fired += OnFired;
            Press(mouse.leftButton);
            yield return Wait(seconds);
            Release(mouse.leftButton);
            yield return null;
            gun.Fired -= OnFired;
        }

        static float MeanInterval(List<float> times) => (times[times.Count - 1] - times[0]) / (times.Count - 1);

        IEnumerator ReloadSeconds(float[] took)
        {
            float t0 = Time.time;
            Assert.IsTrue(gun.TryStartReload(), "a reload can start");
            while (gun.IsReloading && Time.time - t0 < 5f)
                yield return null;
            took[0] = Time.time - t0;
        }

        /// <summary>
        /// Rifle → SMG → rifle from the drop-down, with a test only on the SMG (values unlike the official ones, which
        /// are the same for both guns): the gun in hand fires, holds and reloads as its own definition says, the
        /// range's endless reserve stays, and neither gun's values leak into the other.
        /// </summary>
        [UnityTest]
        public IEnumerator WeaponPick_RifleSmgRifle_FiringMagazineReloadFollowTheGunInHand()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var rifle = gun.Definition;
            Assert.AreEqual("proto_rifle", rifle.weaponId);

            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_smg");
            var smgOfficial = gun.Definition;
            Assert.AreEqual("proto_smg", smgOfficial.weaponId);
            var b = lab.Edited("proto_smg");
            b.rpm = 900f;
            b.magazineSize = 20;
            b.reloadTime = 1f;
            b.damage = 28f;
            lab.NotifyEdited();
            lab.ApplyToTest();
            var smgTest = gun.Definition;
            TestContext.WriteLine(RecoilState("SMG test applied"));
            Assert.AreNotSame(smgOfficial, smgTest, "a runtime clone");
            Assert.AreEqual(60f / 900f, smgTest.fireInterval, 1e-5f);
            Assert.AreEqual(20, smgTest.magazineSize);
            Assert.AreEqual(1f, smgTest.reloadTime);
            Assert.AreEqual(28f, smgTest.damage);
            Assert.AreEqual(20, gun.Ammo.Loaded, "rounds cut to the test magazine, not refilled");
            Assert.AreEqual(30, smgOfficial.magazineSize, "the SMG asset unchanged");

            yield return QuickClick("QFold");
            var smgShots = new List<float>();
            yield return Burst(0.6f, smgShots);
            TestContext.WriteLine($"SMG: {smgShots.Count} shots, every {MeanInterval(smgShots):0.000} s, ammo {gun.Ammo.Loaded}");
            Assert.AreEqual(60f / 900f, MeanInterval(smgShots), 0.012f, "the SMG test's 900 rpm");
            Assert.AreEqual(20 - smgShots.Count, gun.Ammo.Loaded);
            Assert.IsTrue(gun.InfiniteReserve);
            var took = new float[1];
            yield return ReloadSeconds(took);
            TestContext.WriteLine($"SMG reload {took[0]:0.00} s");
            Assert.AreEqual(1f, took[0], 0.15f, "the SMG test's reload time");
            Assert.AreEqual(20, gun.Ammo.Loaded);

            lab.Quick.SetOpen(true);
            yield return null;
            yield return null;
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_rifle");
            TestContext.WriteLine(RecoilState("back to the rifle"));
            Assert.AreSame(rifle, gun.Definition, "the official rifle (it has no test)");
            Assert.AreEqual(30, gun.Ammo.Loaded, "a fresh rifle magazine (a weapon switch starts the gun fresh)");
            Assert.AreEqual(0.1f, gun.Definition.fireInterval, 1e-5f);
            Assert.AreEqual(1.8f, gun.Definition.reloadTime, 1e-5f);
            Assert.AreEqual(25f, gun.Definition.damage);
            Assert.IsFalse(lab.IsTesting("proto_rifle"));
            Assert.IsTrue(lab.IsTesting("proto_smg"), "the SMG's test is kept");
            Assert.AreEqual(600f, lab.Edited("proto_rifle").rpm, 1e-3f, "the rifle's B is not the SMG's");
            Assert.AreEqual(30, lab.Edited("proto_rifle").magazineSize);

            yield return QuickClick("QFold");
            var rifleShots = new List<float>();
            yield return Burst(0.6f, rifleShots);
            TestContext.WriteLine($"rifle: {rifleShots.Count} shots, every {MeanInterval(rifleShots):0.000} s, ammo {gun.Ammo.Loaded}");
            Assert.AreEqual(0.1f, MeanInterval(rifleShots), 0.012f, "the rifle's 600 rpm");
            Assert.AreEqual(30 - rifleShots.Count, gun.Ammo.Loaded);
            yield return ReloadSeconds(took);
            TestContext.WriteLine($"rifle reload {took[0]:0.00} s");
            Assert.AreEqual(1.8f, took[0], 0.15f, "the rifle's reload time");
            Assert.AreEqual(30, gun.Ammo.Loaded);
            Assert.IsTrue(gun.InfiniteReserve);

            lab.Quick.SetOpen(true);
            yield return null;
            yield return null;
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_smg");
            Assert.AreSame(smgTest, gun.Definition, "the SMG's test in hand again");
            Assert.AreEqual(20, gun.Ammo.Loaded, "a fresh magazine of the test's size");
            Assert.AreEqual(1f, gun.Definition.reloadTime);
        }

        /// <summary>
        /// Applying B while aiming down sights with the trigger held: ADS, the held trigger, the shot number, the rounds
        /// and the view's recoil all carry on; the next shots use B. Only the aim reset clears the pattern and the aim.
        /// </summary>
        [UnityTest]
        public IEnumerator ApplyB_WhileAimingAndFiring_KeepsAdsAndTrigger_OnlyAimResetClears()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            yield return null;
            lab.SetVertical(RecoilVertical.Up);
            lab.SetStrength(RecoilStrength.Strong);
            Assert.IsTrue(lab.TrySetValue(RecoilParam.Pitch, 0.9f));
            lab.ApplyToTest();
            yield return Wait(0.3f);

            Press(mouse.rightButton);
            yield return Wait(0.5f);
            Assert.IsTrue(gun.AdsRequested);
            Assert.AreEqual(1f, gun.AdsBlend, 1e-3f, "fully aimed");
            Press(mouse.leftButton);
            yield return Wait(0.35f);
            TestContext.WriteLine(RecoilState("aimed, firing"));
            int shot = gun.ShotIndex, ammo = gun.Ammo.Loaded;
            float yaw = motor.Yaw, pitch = motor.Pitch;
            var recovering = motor.RecoveryRemaining;
            Assert.Greater(shot, 2);

            Assert.IsTrue(lab.TrySetValue(RecoilParam.Pitch, 2f));
            lab.ApplyToTest();
            TestContext.WriteLine(RecoilState("B applied"));
            Assert.IsTrue(gun.AdsRequested, "ADS kept");
            Assert.AreEqual(1f, gun.AdsBlend, 1e-3f);
            Assert.AreEqual(shot, gun.ShotIndex, "shot number kept");
            Assert.AreEqual(ammo, gun.Ammo.Loaded, "no refill");
            Assert.AreEqual(yaw, motor.Yaw);
            Assert.AreEqual(pitch, motor.Pitch);
            Assert.AreEqual(recovering, motor.RecoveryRemaining, "the view's recoil is not cleared");

            yield return Wait(0.25f);
            TestContext.WriteLine(RecoilState("still aimed and firing"));
            Assert.IsTrue(gun.AdsRequested, "still aimed");
            Assert.AreEqual(1f, gun.AdsBlend, 1e-3f);
            Assert.Greater(gun.ShotIndex, shot, "the held trigger keeps firing (no re-press)");
            Assert.Less(gun.Ammo.Loaded, ammo);
            Assert.IsTrue(RecoilCalculator.TryCompute(gun.Definition.recoilProfile, gun.ShotIndex - 1, gun.AdsBlend, out float kp, out float ky));
            Assert.AreEqual(kp, gun.LastRecoil.x, 1e-4f, "B's kick (aimed)");
            Assert.AreEqual(ky, gun.LastRecoil.y, 1e-4f);

            Release(mouse.leftButton);
            yield return null;
            Assert.Greater(gun.ShotIndex, 0);
            int left = gun.Ammo.Loaded;
            var definition = gun.Definition;
            lab.ResetAim();
            TestContext.WriteLine(RecoilState("aim reset"));
            Assert.AreEqual(0, gun.ShotIndex, "the aim reset starts the pattern again");
            Assert.AreEqual(Vector2.zero, motor.RecoveryRemaining, "and clears the recovery");
            Assert.AreEqual(lab.AimReference.x, motor.Yaw, 1e-4f);
            Assert.AreEqual(lab.AimReference.y, motor.Pitch, 1e-4f);
            Assert.AreEqual(left, gun.Ammo.Loaded, "rounds untouched");
            Assert.AreSame(definition, gun.Definition, "values untouched");
            Assert.AreEqual(2f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f);
            Release(mouse.rightButton);
            yield return null;
        }

        /// <summary>
        /// The in-game panel's rows: every label on one line within its column, thin slider tracks and slim handles while
        /// each slider still takes touches over its whole row, the panel wider than before.
        /// </summary>
        [UnityTest]
        public IEnumerator QuickHud_OneLineLabels_ThinSliders_SameTouchArea()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            yield return QuickClick("QExpand");
            yield return null;
            var quick = lab.Quick;
            var editor = quick.Editor;
            Assert.GreaterOrEqual(quick.Panel.rect.width, WeaponBalanceQuickPanel.NarrowestWidth, "wider than the earlier 540–680");
            foreach (var t in editor.Root.GetComponentsInChildren<Text>(true))
            {
                if (!t.name.StartsWith("L_"))
                    continue;
                TestContext.WriteLine($"{t.name} '{t.text}' {t.preferredWidth:0}/{t.rectTransform.rect.width:0} font {t.fontSize}");
                StringAssert.DoesNotContain("\n", t.text, t.name + ": one line");
                Assert.LessOrEqual(t.preferredWidth, t.rectTransform.rect.width + 0.5f, t.name + ": fits its column");
                Assert.AreEqual(HorizontalWrapMode.Overflow, t.horizontalOverflow);
            }
            foreach (RecoilParam p in new[] { RecoilParam.Pitch, RecoilParam.Yaw, RecoilParam.Ads, RecoilParam.RecoveryAmount, RecoilParam.RecoverySpeed })
            {
                var s = editor.SliderOf(p);
                var r = (RectTransform)s.transform;
                var bg = (RectTransform)r.Find("Background");
                var handle = (RectTransform)r.Find("Handle Slide Area/Handle");
                TestContext.WriteLine($"{p}: touch {r.rect.width:0}x{r.rect.height:0}, track {bg.rect.height:0}, handle {handle.rect.width:0}x{handle.rect.height:0}");
                Assert.AreEqual(WeaponBalanceQuickPanel.Row, r.rect.height, 0.01f, p + ": touch area = the row, as before");
                var face = r.GetComponent<Image>();
                Assert.IsNotNull(face, p + ": the whole slider takes touches");
                Assert.IsTrue(face.raycastTarget);
                Assert.AreEqual(WeaponRecoilEditor.ThinTrack, bg.rect.height, 0.01f, p + ": thin track");
                Assert.AreEqual(WeaponRecoilEditor.ThinHandle, handle.rect.width, 0.01f, p + ": slim handle");
                Assert.Less(handle.rect.height, r.rect.height, p + ": handle drawn shorter than the row");
            }
            Assert.Greater(((RectTransform)editor.SliderOf(RecoilParam.Pitch).transform).rect.width, 112f, "the direction slider is longer than the earlier 112 units");
            // The full lab keeps its own look.
            Assert.IsFalse(lab.RecoilEditor.Style.OneLineLabels);
            Assert.IsFalse(lab.RecoilEditor.Style.ThinSliders);
        }

        /// <summary>Every text of the in-game panel on one line: compact and expanded, rifle and SMG, with B differing from A.</summary>
        [UnityTest]
        public IEnumerator QuickHud_NoTextWraps_CompactAndExpanded_RifleAndSmg()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var quick = lab.Quick;
            IEnumerator CheckAll(string what)
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.GreaterOrEqual(quick.Panel.rect.width, WeaponBalanceQuickPanel.NarrowestWidth, what);
                Assert.AreEqual(quick.Picker.FullHeaderText, quick.Picker.Header.GetComponentInChildren<Text>().text, what + ": the weapon's full name, not cut");
                foreach (var t in quick.Panel.GetComponentsInChildren<Text>(false))
                {
                    if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.GetComponentInParent<InputField>() != null && t.name == "Placeholder")
                        continue;
                    if (quick.Picker.IsListOpen && t.transform.IsChildOf(quick.Picker.List))
                        continue;
                    int lines = t.text.Split('\n').Length;
                    var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
                    settings.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var gen = new TextGenerator();
                    gen.Populate(t.text, settings);
                    TestContext.WriteLine($"{what} {t.name}: '{t.text.Replace("\n", "/")}' lines {gen.lineCount}/{lines} width {t.preferredWidth:0}/{t.rectTransform.rect.width:0}");
                    Assert.LessOrEqual(gen.lineCount, lines, $"{what}: '{t.name}' wraps: '{t.text}'");
                }
            }
            yield return CheckAll("compact rifle");
            // B differs from A, a test runs, a message is shown.
            lab.SetHorizontal(RecoilHorizontal.Right);
            lab.SetStrength(RecoilStrength.Strong);
            lab.ApplyToTest();
            lab.SetVertical(RecoilVertical.Down);
            lab.SetFineAdjust(true);
            lab.ResetAim();
            yield return CheckAll("compact testing");
            yield return QuickClick("QExpand");
            yield return CheckAll("expanded testing, fine");
            lab.SetFineAdjust(false);
            yield return CheckAll("expanded coarse");
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_smg");
            yield return CheckAll("expanded SMG");
            Assert.AreEqual("proto_smg", gun.Definition.weaponId);
            yield return QuickClick("QFold");
            lab.Quick.SetOpen(true);
            yield return CheckAll("compact SMG");
        }

        /// <summary>Dragging the panel by its title moves it, never out of the safe area; the face stays half transparent.</summary>
        [UnityTest]
        public IEnumerator QuickHud_DragByTitle_Moves_StaysInsideTheSafeArea()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var quick = lab.Quick;
            var canvas = quick.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            var start = quick.Panel.anchoredPosition;
            var data = new PointerEventData(EventSystem.current) { delta = new Vector2(-40f, -30f) * scale };
            quick.OnDrag(data);
            yield return null;
            TestContext.WriteLine($"drag: {start} -> {quick.Panel.anchoredPosition}, safe {Screen.safeArea}");
            Assert.IsTrue(quick.UserMoved);
            Assert.AreNotEqual(start, quick.Panel.anchoredPosition, "the panel follows the finger");

            foreach (var far in new[] { new Vector2(10000f, 10000f), new Vector2(-10000f, -10000f) })
            {
                quick.OnDrag(new PointerEventData(EventSystem.current) { delta = far });
                yield return null;
                var c = new Vector3[4];
                quick.Panel.GetWorldCorners(c);
                var safe = Screen.safeArea;
                Assert.IsTrue(c[0].x >= safe.xMin - 0.5f && c[0].y >= safe.yMin - 0.5f && c[2].x <= safe.xMax + 0.5f && c[2].y <= safe.yMax + 0.5f,
                    $"inside the safe area after a drag of {far}");
            }
            Assert.AreEqual(WeaponBalanceQuickPanel.BackgroundAlpha, quick.Background.color.a, 1e-4f);
            Assert.AreEqual(0.5f, WeaponBalanceQuickPanel.BackgroundAlpha);
        }

        static Sprite HipSprite()
        {
            var view = Object.FindAnyObjectByType<Deadlock.View.WeaponViewModel>();
            var f = typeof(Deadlock.View.WeaponViewModel).GetField("hipSprite", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ((SpriteRenderer)f.GetValue(view)).sprite;
        }

        [UnityTest]
        public IEnumerator WeaponPick_SwitchesTheGunInHand_ABPerWeapon_TestsKeptPerWeapon()
        {
            DevGunSelection.ClearSelection();
            yield return OpenQuick();
            var quick = lab.Quick;
            var rifle = gun.Definition;
            var sel = player.GetComponent<DevGunSelection>();
            Assert.IsTrue(gun.InfiniteReserve, "the range's endless reserve");

            // Rifle: B applied (a test), a few shots fired.
            lab.SetVertical(RecoilVertical.Up);
            lab.SetStrength(RecoilStrength.Strong);
            Assert.IsTrue(lab.TrySetValue(RecoilParam.Pitch, 0.8f));
            lab.ApplyToTest();
            var rifleTest = gun.Definition;
            var rifleHip = HipSprite();
            Assert.AreSame(rifle.viewProfile.hipSprite, rifleHip);
            yield return QuickClick("QFold");
            Press(mouse.leftButton);
            yield return Wait(0.3f);
            Release(mouse.leftButton);
            yield return null;
            Assert.Less(gun.Ammo.Loaded, rifle.magazineSize);
            lab.Quick.SetOpen(true);
            yield return null;
            yield return null;
            var rifleHipPreview = new List<Vector2>(lab.PreviewHip);

            // To the SMG from the drop-down: the gun in hand, its look, a fresh magazine, its own A / B and preview.
            gun.TryStartReload();
            Assert.IsTrue(gun.IsReloading);
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_smg");
            TestContext.WriteLine(RecoilState("to SMG"));
            Assert.AreEqual("proto_smg", lab.SelectedId);
            Assert.AreEqual("proto_smg", gun.Definition.weaponId);
            Assert.AreEqual("proto_smg", DevGunSelection.SelectedId, "the session's test choice follows");
            Assert.AreSame(sel.Options[1], gun.Definition, "the official SMG (no test of its own)");
            Assert.AreSame(gun.Definition.viewProfile.hipSprite, HipSprite(), "its look");
            Assert.AreNotSame(rifleHip, HipSprite());
            Assert.IsFalse(gun.IsReloading, "the rifle's reload does not carry over");
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded, "a full magazine");
            Assert.IsTrue(gun.InfiniteReserve, "the endless reserve stays");
            Assert.IsFalse(lab.Applied("proto_smg").recoil.enabled, "SMG A: official");
            Assert.IsFalse(lab.Edited("proto_smg").recoil.enabled, "SMG B: its own");
            Assert.IsTrue(lab.IsTesting("proto_rifle"), "the rifle's test is kept");
            lab.Refresh();
            CollectionAssert.AreNotEqual(rifleHipPreview, lab.PreviewHip, "the preview is the SMG's");
            StringAssert.Contains("試射なし", quick.StatusText.text);

            // SMG B edited: the rifle's A / B stay.
            lab.SetHorizontal(RecoilHorizontal.Left);
            Assert.IsTrue(lab.Edited("proto_smg").recoil.enabled);
            Assert.AreEqual(0.8f, lab.Edited("proto_rifle").recoil.pitchDegrees, 1e-5f);
            Assert.AreEqual(0.8f, lab.Applied("proto_rifle").recoil.pitchDegrees, 1e-5f);
            Assert.AreEqual(RecoilHorizontal.None, lab.Edited("proto_rifle").recoil.horizontal);

            // Back to the rifle: its test values in hand again (A), never the SMG's.
            yield return QuickClick("QWeaponPick");
            yield return null;
            yield return QuickClick("QWeaponItem_proto_rifle");
            TestContext.WriteLine(RecoilState("back to the rifle"));
            Assert.AreSame(rifleTest, gun.Definition, "the rifle's test clone");
            Assert.AreEqual("proto_rifle", DevGunSelection.SelectedId);
            Assert.AreSame(rifleHip, HipSprite());
            Assert.AreEqual(0.8f, gun.Definition.recoilProfile.pitchDegrees, 1e-5f);
            Assert.AreEqual(RecoilHorizontal.None, gun.Definition.recoilProfile.horizontal, "not the SMG's B");
            Assert.IsTrue(lab.Testing);
            StringAssert.Contains("試射中", quick.StatusText.text);

            // End of the rifle's test: the official rifle in hand.
            lab.EndTest();
            Assert.AreSame(rifle, gun.Definition);
            Assert.IsFalse(lab.IsTesting("proto_rifle"));
            Assert.IsTrue(gun.InfiniteReserve);

            // A switch from the menu's test choice: the lab follows it.
            sel.Select("proto_smg");
            yield return null;
            Assert.AreEqual("proto_smg", lab.SelectedId, "the lab shows the gun in hand");
            Assert.IsFalse(rifle.recoilProfile.enabled, "the weapon asset was never written");
        }

        [UnityTest]
        public IEnumerator WeaponList_AddedDefinition_ShowsUp_WithItsOwnAB()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            // A third gun in the scene's list (as a future weapon would be): no UI change needed.
            var sel = player.GetComponent<DevGunSelection>();
            var field = typeof(DevGunSelection).GetField("options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var options = new List<WeaponDefinition>((WeaponDefinition[])field.GetValue(sel));
            var extra = Object.Instantiate(options[0]);
            extra.weaponId = "proto_extra";
            extra.displayName = "TEST EXTRA WITH A VERY LONG NAME FOR THE LAYOUT";
            options.Add(extra);
            field.SetValue(sel, options.ToArray());
            try
            {
                lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
                yield return null;
                lab.Quick.SetOpen(true);
                yield return null;
                yield return null;
                yield return QuickClick("QWeaponPick");
                yield return null;
                CollectionAssert.AreEqual(new[] { "proto_rifle", "proto_smg", "proto_extra" }, lab.Quick.Picker.Listed);
                Assert.AreEqual(lab.WeaponLabel("proto_extra"), lab.Quick.Control("QWeaponItem_proto_extra").GetComponentInChildren<Text>().text, "full name in the list");
                yield return QuickClick("QWeaponItem_proto_extra");
                Assert.AreEqual("proto_extra", lab.SelectedId);
                var header = lab.Quick.Picker.Header.GetComponentInChildren<Text>();
                StringAssert.StartsWith("TEST", header.text);
                StringAssert.Contains("…", header.text, "a long name is cut");
                Assert.LessOrEqual(header.preferredWidth, ((RectTransform)lab.Quick.Picker.Header.transform).rect.width, "and fits the header");
                yield return QuickClick("QExpand");
                yield return null;
                yield return QuickClick("QStr_Strong");
                Assert.IsTrue(lab.Edited("proto_extra").recoil.enabled);
                Assert.IsFalse(lab.Edited("proto_rifle").recoil.enabled, "other weapons' B untouched");
                lab.ApplyToTest();
                Assert.AreEqual("proto_extra", gun.Definition.weaponId);
                Assert.AreEqual(RecoilStrength.Strong, gun.Definition.recoilProfile.strength);
                lab.EndTest();
                Assert.AreSame(extra, gun.Definition, "back to that gun's own definition");
            }
            finally
            {
                Object.Destroy(extra);
            }
        }

        [UnityTest]
        public IEnumerator Recovery_AmountAndSpeed_AreSeparate()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            yield return null;
            IEnumerator Shot(float fraction, float speed, float wait, System.Action<float> result)
            {
                lab.Edited("proto_rifle").recoil = new RecoilProfile { enabled = true, axis = RecoilAxis.Vertical, vertical = RecoilVertical.Up,
                    horizontal = RecoilHorizontal.None, strength = RecoilStrength.Medium, pitchDegrees = 2f, yawDegrees = 0f, adsMultiplier = 1f,
                    recoveryFraction = fraction, recoverySpeed = speed };
                lab.ApplyToTest();
                yield return Wait(0.6f);
                motor.SetYawPitch(motor.Yaw, 0f); // no recovery left over from the previous shot
                float p0 = motor.Pitch;
                Press(mouse.leftButton);
                yield return null;
                Release(mouse.leftButton);
                yield return Wait(wait);
                result(motor.Pitch - p0);
            }
            float none = 0f, all = 0f, slow = 0f, fast = 0f;
            yield return Shot(0f, 9f, 1f, v => none = v);
            yield return Shot(1f, 60f, 0.5f, v => all = v);
            yield return Shot(0.5f, 1f, 0.3f, v => slow = v);
            yield return Shot(0.5f, 60f, 0.3f, v => fast = v);
            Assert.AreEqual(2f, none, 0.02f, "amount 0: the whole kick stays");
            Assert.AreEqual(0f, all, 0.02f, "amount 1, fast: back to the aim point");
            Assert.AreEqual(1f, fast, 0.02f, "amount 0.5: half stays");
            Assert.Greater(slow, 1.5f, "same amount, slow speed: still recovering after 0.3 s");
            lab.EndTest();
        }

        [UnityTest]
        public IEnumerator AppliedRecoil_ReachesTheCamera_ReadOutShown_EndRestores()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var rifle = gun.Definition;
            // Test C of the investigation: diagonal up-right, strong, 2.0° / 1.0°.
            var e = lab.Edited("proto_rifle");
            e.recoil = new RecoilProfile { enabled = true, axis = RecoilAxis.Diagonal, vertical = RecoilVertical.Up, horizontal = RecoilHorizontal.Right,
                strength = RecoilStrength.Strong, pitchDegrees = 2f, yawDegrees = 1f, adsMultiplier = 0.55f };
            lab.Refresh();
            yield return Click("Tab_Test");
            yield return Click("Apply");
            Assert.IsTrue(lab.Testing);
            Assert.AreSame(lab.AppliedClone, gun.Definition, "the gun fires with the clone");
            yield return Click("Close");
            yield return Wait(0.7f);
            Assert.IsTrue(lab.TestHudVisible, "read-out over the game during the test");
            float pitch0 = motor.Pitch, yaw0 = motor.Yaw;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
            yield return null;
            Assert.AreEqual(2.8f, gun.LastRecoil.x, 1e-4f, "2.0° × strong 1.4");
            Assert.AreEqual(1.4f, gun.LastRecoil.y, 1e-4f);
            // The camera took the kick (less what has recovered since, at 9°/s).
            // More than the half that stays: the kick reached the camera (part may have recovered at 9°/s already).
            Assert.Greater(motor.Pitch - pitch0, 1.45f, "camera pitched up");
            Assert.Greater(motor.Yaw - yaw0, 0.72f, "camera turned right");
            StringAssert.Contains("右上", lab.TestHudText.text);
            StringAssert.Contains("強", lab.TestHudText.text);
            StringAssert.Contains("+2.80", lab.TestHudText.text);
            yield return Wait(1f);
            Assert.AreEqual(1.4f, motor.Pitch - pitch0, 0.05f, "half of the kick stays (earlier recovery rule)");
            lab.EndTest();
            Assert.AreSame(rifle, gun.Definition);
            yield return null;
            Assert.IsFalse(lab.TestHudVisible);
        }

        [UnityTest]
        public IEnumerator Rifle_SemiAuto_DownWeak_Test_ThenNoProfileKeepsTheEarlierRecoil()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var rifle = gun.Definition;
            Assert.AreEqual("proto_rifle", lab.SelectedId);
            var e = lab.Edited("proto_rifle");
            e.automatic = false;
            e.recoil = new RecoilProfile { enabled = true, axis = RecoilAxis.Vertical, vertical = RecoilVertical.Down, horizontal = RecoilHorizontal.None,
                strength = RecoilStrength.Weak, pitchDegrees = 1f, yawDegrees = 0.3f, adsMultiplier = 0.5f };
            lab.ApplyToTest();
            Assert.IsTrue(lab.Testing);
            lab.Close();
            yield return null;
            int loaded = gun.Ammo.Loaded;
            Press(mouse.leftButton);
            yield return Wait(0.5f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(loaded - 1, gun.Ammo.Loaded, "semi-auto: one shot per press");
            Assert.AreEqual(-0.7f, gun.LastRecoil.x, 1e-4f, "down, weak (0.7)");
            Assert.AreEqual(0f, gun.LastRecoil.y);
            yield return Wait(0.15f);
            yield return Tap(mouse.leftButton);
            Assert.AreEqual(loaded - 2, gun.Ammo.Loaded);
            lab.EndTest();
            Assert.AreSame(rifle, gun.Definition);
            // The official rifle (no profile): the earlier recoil, up with a sideways kick within ±yaw.
            yield return Wait(0.6f);
            Press(mouse.leftButton);
            yield return Wait(0.35f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(rifle.recoilPitchDegrees, gun.LastRecoil.x, 1e-4f);
            Assert.LessOrEqual(Mathf.Abs(gun.LastRecoil.y), rifle.recoilYawDegrees + 1e-4f);
        }

        [UnityTest]
        public IEnumerator Lab_SaveLoadReset_InItsOwnFolder_SettingsUntouched()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var prefsBefore = PlayerPrefs.GetString(TouchLayout.PrefsKey, "");
            yield return Click("Tab_Recoil");
            yield return Click("DirH_Left"); // up (the default) + left: diagonal
            var r = lab.Edited("proto_rifle").recoil;
            Assert.AreEqual(RecoilAxis.Diagonal, r.axis);
            Assert.AreEqual(RecoilHorizontal.Left, r.horizontal);
            yield return Click("Tab_Test");
            yield return Click("Save");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "proto_rifle.json")), "saved to the lab's own folder");
            yield return Click("ResetEdit");
            Assert.IsFalse(lab.Edited("proto_rifle").recoil.enabled, "back to the current values");
            yield return Click("Load");
            Assert.AreEqual(RecoilAxis.Diagonal, lab.Edited("proto_rifle").recoil.axis, "restored from the file");
            Assert.AreEqual(RecoilHorizontal.Left, lab.Edited("proto_rifle").recoil.horizontal);
            Assert.IsTrue(lab.Store.TryLoad("proto_rifle", out var saved, out _));
            Assert.AreEqual(WeaponBalanceProfile.CurrentVersion, saved.profileVersion);
            Assert.IsNotEmpty(saved.baseCommit);
            yield return Click("DeleteSaved");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "proto_rifle.json")));
            Assert.AreEqual(prefsBefore, PlayerPrefs.GetString(TouchLayout.PrefsKey, ""), "game settings untouched");
            Assert.IsFalse(gun.Definition.recoilProfile.enabled, "saving does not apply anything");
            yield return Click("Close");
        }

        [UnityTest]
        public IEnumerator Lab_TouchesAndClicks_DoNotReachTheGame_WhileOpen()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            float yaw = motor.Yaw;
            int loaded = gun.Ammo.Loaded;
            // Clicks on empty parts of the lab window and outside it.
            var c = new Vector3[4];
            lab.Window.GetWorldCorners(c);
            yield return Click(new Vector2(c[2].x - 30f, c[0].y + 30f));
            yield return Click(new Vector2(2f, 2f));
            Press(mouse.leftButton);
            Move(mouse.delta, new Vector2(200f, 0f));
            yield return Wait(0.3f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "no shot while the lab is open");
            Assert.AreEqual(yaw, motor.Yaw, 1e-3f, "no look while the lab is open");
            Assert.IsTrue(lab.IsOpen);
            yield return Click("Close");
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(router.Current.FireHeld);
        }
    }
}
#endif
