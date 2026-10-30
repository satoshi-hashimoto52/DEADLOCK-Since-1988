using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.UI;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Layout editing up to the screen's left and right edges (past the Safe Area, into the notch side margins) and the
    /// ammo panel (reserve / loaded, the gun's side view) as a movable layout item. The device's side margins are
    /// reproduced by narrowing every Safe Area to an iPhone 16 Pro's (186 px of 2622 on each side).
    /// </summary>
    public class LayoutEdgesAndAmmoPanelTests : FiringRangeTestBase
    {
        const float PhoneInset = 186f / 2622f;

        GameMenu menu;
        TouchLayoutEditor editor;
        TouchLayoutApplier applier;
        HudController hud;

        IEnumerator Load(InputMode mode, bool phoneMargins)
        {
            yield return LoadScene(mode);
            menu = Object.FindAnyObjectByType<GameMenu>();
            editor = menu.GetComponent<TouchLayoutEditor>();
            applier = Object.FindAnyObjectByType<TouchLayoutApplier>(FindObjectsInactive.Include);
            hud = Object.FindAnyObjectByType<HudController>();
            if (phoneMargins)
                foreach (var fitter in Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    fitter.enabled = false;
                    var r = (RectTransform)fitter.transform;
                    r.anchorMin = new Vector2(PhoneInset, r.anchorMin.y);
                    r.anchorMax = new Vector2(1f - PhoneInset, r.anchorMax.y);
                }
            yield return null;
            yield return null;
        }

        static Rect ScreenRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        Rect ScreenArea => ScreenRect((RectTransform)applier.SafeArea.GetComponentInParent<Canvas>().rootCanvas.transform);
        Rect Safe => ScreenRect(applier.SafeArea);

        [UnityTest]
        public IEnumerator EveryButton_ReachesTheLeftAndRightScreenEdges_PastTheSafeArea_NeverOffScreen()
        {
            yield return Load(InputMode.Touch, true);
            Assert.Greater(applier.SideRoom.x, 50f, "the simulated notch margins");
            foreach (string id in new[] { TouchLayout.Fire, TouchLayout.Ads, TouchLayout.Jump, TouchLayout.Posture, TouchLayout.Reload, TouchLayout.Weapon })
                foreach (float dir in new[] { 1f, -1f })
                {
                    menu.OpenLayoutEditor();
                    yield return null;
                    bool moved = editor.MoveBy(id, new Vector2(10000f * dir, 0f));
                    yield return null;
                    var r = ScreenRect(applier.Widget(id));
                    TestContext.WriteLine($"{id} {(dir > 0 ? "right" : "left")}: moved={moved} '{editor.Message}' x {r.xMin:0.0}–{r.xMax:0.0} screen {ScreenArea.xMin:0}–{ScreenArea.xMax:0} safe {Safe.xMin:0}–{Safe.xMax:0}");
                    if (moved)
                    {
                        if (dir > 0)
                        {
                            Assert.AreEqual(ScreenArea.xMax, r.xMax, 0.6f, id + ": on the right screen edge");
                            Assert.Greater(r.xMax, Safe.xMax + 10f, id + ": past the Safe Area");
                        }
                        else
                        {
                            Assert.AreEqual(ScreenArea.xMin, r.xMin, 0.6f, id + ": on the left screen edge");
                            Assert.Less(r.xMin, Safe.xMin - 10f, id + ": past the Safe Area");
                        }
                    }
                    else
                        StringAssert.Contains("重なっています", editor.Message, id + ": only a button in the way may stop it");
                    editor.CancelAndClose();
                    menu.Close();
                    yield return null;
                }
            // Every button reaches at least one side (its own side is clear in the default layout).
            foreach (string id in new[] { TouchLayout.Fire, TouchLayout.Reload, TouchLayout.Weapon })
            {
                menu.OpenLayoutEditor();
                yield return null;
                Assert.IsTrue(editor.MoveBy(id, new Vector2(10000f, 0f)), id + " to the right: " + editor.Message);
                editor.CancelAndClose();
                menu.Close();
            }
            foreach (string id in new[] { TouchLayout.Ads, TouchLayout.Jump, TouchLayout.Posture })
            {
                menu.OpenLayoutEditor();
                yield return null;
                Assert.IsTrue(editor.MoveBy(id, new Vector2(-10000f, 0f)), id + " to the left: " + editor.Message);
                editor.CancelAndClose();
                menu.Close();
            }
        }

        [UnityTest]
        public IEnumerator EdgePlacement_AndTheAmmoPanel_SavedAndRestoredOnTheNextStart()
        {
            yield return Load(InputMode.Touch, true);
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.MoveBy(TouchLayout.Fire, new Vector2(10000f, 0f)), editor.Message);
            Assert.IsTrue(editor.MoveBy(TouchLayout.Jump, new Vector2(-10000f, 0f)), editor.Message);
            Assert.IsTrue(editor.MoveBy(TouchLayout.Ammo, new Vector2(-10000f, -350f)), editor.Message);
            Assert.IsTrue(editor.Resize(TouchLayout.Ammo, +1), editor.Message);
            yield return null;
            var panel = ScreenRect(hud.AmmoPanel);
            TestContext.WriteLine($"ammo panel {panel} screen {ScreenArea} safe {Safe}");
            Assert.AreEqual(ScreenArea.xMin, panel.xMin, 0.6f, "the ammo panel on the left screen edge");
            Assert.AreEqual(1.1f, hud.AmmoPanel.localScale.x, 1e-4f, "resized like the buttons");
            var working = editor.Working;
            editor.ApplyAndClose();
            menu.Close();

            // Next start: everything where it was put.
            yield return Load(InputMode.Touch, true);
            Assert.AreEqual(working.Get(TouchLayout.Fire).center.x, applier.Widget(TouchLayout.Fire).anchorMin.x, 0.001f);
            Assert.AreEqual(ScreenArea.xMax, ScreenRect(applier.Widget(TouchLayout.Fire)).xMax, 0.6f);
            Assert.AreEqual(ScreenArea.xMin, ScreenRect(applier.Widget(TouchLayout.Jump)).xMin, 0.6f);
            Assert.AreEqual(working.Get(TouchLayout.Ammo).center.x, hud.AmmoPanel.anchorMin.x, 0.001f);
            Assert.AreEqual(working.Get(TouchLayout.Ammo).center.y, hud.AmmoPanel.anchorMin.y, 0.001f);
            Assert.AreEqual(ScreenArea.xMin, ScreenRect(hud.AmmoPanel).xMin, 0.6f);
            Assert.AreEqual(1.1f, hud.AmmoPanel.localScale.x, 1e-4f);

            // PC mode (touch controls hidden): the panel still takes the saved place.
            yield return Load(InputMode.Desktop, false);
            Assert.AreEqual(working.Get(TouchLayout.Ammo).center.y, hud.AmmoPanel.anchorMin.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator AmmoPanel_ReserveThenLoaded_SideViewFollowsTheGun()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            var skins = menu.Skins;
            var rifleSide = skins.WeaponSet("proto_rifle").sideView;
            Assert.IsNotNull(rifleSide, "the rifle has a side view");
            Assert.AreEqual($"{gun.Definition.magazineSize} / ∞", hud.AmmoText.text, "loaded / reserve, endless in the range");
            Assert.IsNotNull(hud.AmmoArt);
            Assert.AreSame(rifleSide, hud.AmmoArt.sprite, "the rifle's side view");
            Assert.IsTrue(hud.AmmoArt.preserveAspect);
            Assert.IsTrue(hud.AmmoArt.rectTransform.IsChildOf(hud.AmmoPanel), "under the count, in the panel");
            Assert.IsTrue(hud.AmmoText.rectTransform.IsChildOf(hud.AmmoPanel));
            Assert.Greater(ScreenRect(hud.AmmoText.rectTransform).yMin, ScreenRect(hud.AmmoArt.rectTransform).yMax - 0.5f, "count above the picture");

            Press(mouse.leftButton);
            yield return Wait(0.35f);
            Release(mouse.leftButton);
            yield return null;
            Assert.Less(gun.Ammo.Loaded, gun.Definition.magazineSize);
            Assert.AreEqual($"{gun.Ammo.Loaded} / ∞", hud.AmmoText.text);

            var sel = player.GetComponent<DevGunSelection>();
            Assert.IsTrue(sel.Select("proto_smg"));
            yield return null;
            var smgSet = skins.WeaponSet("proto_smg");
            Assert.AreEqual($"{gun.Definition.magazineSize} / ∞", hud.AmmoText.text, "the SMG fresh magazine");
            Assert.IsNotNull(smgSet.sideView, "the SMG has its registered side view");
            Assert.IsNotNull(hud.AmmoArt, "a picture, not the name");
            Assert.AreSame(smgSet.sideView, hud.AmmoArt.sprite, "the SMG's own side view");
            Assert.AreNotSame(rifleSide, hud.AmmoArt.sprite);
            Assert.AreEqual("", hud.AmmoCaption.text, "no 'SMG（候補）' caption any more");
            Assert.IsTrue(hud.AmmoArt.preserveAspect);
            Assert.IsTrue(hud.AmmoArt.rectTransform.IsChildOf(hud.AmmoPanel));

            Assert.IsTrue(sel.Select("proto_rifle"));
            yield return null;
            Assert.AreSame(rifleSide, hud.AmmoArt.sprite, "back to the rifle's");
            Assert.AreEqual("", hud.AmmoCaption.text);
            DevGunSelection.ClearSelection();
        }

        [UnityTest]
        public IEnumerator AmmoCount_IsSmaller_PanelKeepsItsParts()
        {
            yield return Load(InputMode.Desktop, false);
            Assert.AreEqual(HudController.AmmoCountFont, hud.AmmoText.resizeTextMaxSize);
            Assert.Less(HudController.AmmoCountFont, 56, "smaller than the earlier 56");
            Assert.GreaterOrEqual(HudController.AmmoCountFont, 32, "still easy to read");
            Assert.AreEqual(HudController.AmmoCountRow, hud.AmmoText.rectTransform.rect.height, 0.01f);
            Assert.AreEqual(TouchLayout.AmmoBaseSize.y, hud.AmmoPanel.rect.height, 0.01f);
            Assert.IsNotNull(hud.AmmoArt, "the side view stays");
            var count = ScreenRect(hud.AmmoText.rectTransform);
            var art = ScreenRect(hud.AmmoArt.rectTransform);
            var stance = ScreenRect(hud.StanceText.rectTransform);
            Assert.GreaterOrEqual(count.yMin, art.yMax - 0.5f, "count above the picture");
            Assert.LessOrEqual(stance.yMax, ScreenRect(hud.AmmoPanel).yMin + 0.5f, "the posture line under the panel, not over it");
            Assert.LessOrEqual(hud.AmmoText.preferredWidth, hud.AmmoText.rectTransform.rect.width, "30 / ∞ fits");
        }

        /// <summary>
        /// The rifle's and the SMG's side views in the ammo panel, with the HUD laid out for an iPhone 16 Pro (2622 x 1206)
        /// and an iPad (2048 x 1536) (rendered by a camera into a texture of that size; batch mode has a fixed screen):
        /// each drawn whole inside the panel with its own proportions (preserveAspect: the fitted picture has the image's
        /// aspect), under the count `30 / ∞`, switching with the gun; both fill the picture area's height, so their
        /// canvases are drawn at one scale (the drawn guns' heights: <c>SkinDataTests</c>). A moved and resized panel
        /// (the layout editor's sizes, saved as before) keeps the picture inside.
        /// </summary>
        [UnityTest]
        public IEnumerator SideViews_RifleAndSmg_FitThePanel_AspectKept_OneScale_IPhoneAndIPad()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Touch, false);
            var canvas = hud.AmmoPanel.GetComponentInParent<Canvas>().rootCanvas;
            var mode = canvas.renderMode;
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            var sel = player.GetComponent<DevGunSelection>();
            Rect Pixels(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            // What preserveAspect draws: the sprite fitted inside its rectangle, centred.
            Rect Fitted(Rect area, Vector2 sprite)
            {
                float s = Mathf.Min(area.width / sprite.x, area.height / sprite.y);
                var size = sprite * s;
                return new Rect(area.center - size / 2f, size);
            }
            try
            {
                foreach (var screen in new[] { new Vector2Int(2622, 1206), new Vector2Int(2048, 1536) })
                {
                    var rt = new RenderTexture(screen.x, screen.y, 16);
                    cam.targetTexture = rt;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    foreach (float scale in new[] { 1f, TouchLayout.MaxScale })
                    {
                        hud.AmmoPanel.localScale = new Vector3(scale, scale, 1f);
                        var heights = new System.Collections.Generic.Dictionary<string, float>();
                        foreach (string id in new[] { "proto_rifle", "proto_smg", "proto_rifle" })
                        {
                            Assert.IsTrue(sel.Select(id));
                            yield return null;
                            yield return null;
                            Canvas.ForceUpdateCanvases();
                            string what = $"{screen} x{scale} {id}";
                            var sprite = menu.Skins.WeaponSet(id).sideView;
                            Assert.IsNotNull(hud.AmmoArt, what);
                            Assert.AreSame(sprite, hud.AmmoArt.sprite, what + ": follows the gun");
                            Assert.AreEqual("", hud.AmmoCaption.text, what + ": no caption");
                            Assert.IsTrue(hud.AmmoArt.preserveAspect, what);
                            Rect panel = Pixels(hud.AmmoPanel), area = Pixels(hud.AmmoArt.rectTransform);
                            Rect pic = Fitted(area, sprite.rect.size);
                            Assert.AreEqual(sprite.rect.width / sprite.rect.height, pic.width / pic.height, 1e-3f, what + ": proportions kept");
                            Assert.GreaterOrEqual(pic.xMin, panel.xMin - 0.5f, what + ": inside the panel (left)");
                            Assert.LessOrEqual(pic.xMax, panel.xMax + 0.5f, what + ": (right)");
                            Assert.GreaterOrEqual(pic.yMin, panel.yMin - 0.5f, what + ": (bottom)");
                            Assert.LessOrEqual(pic.yMax, panel.yMax + 0.5f, what + ": (top)");
                            Assert.AreEqual(area.height, pic.height, 0.5f, what + ": fills the picture area's height");
                            Assert.GreaterOrEqual(Pixels(hud.AmmoText.rectTransform).yMin, pic.yMax - 0.5f, what + ": count above the picture");
                            Assert.AreEqual($"{gun.Ammo.Loaded} / ∞", hud.AmmoText.text, what + ": loaded / reserve");
                            heights[id] = pic.height;
                            TestContext.WriteLine($"{what}: panel {panel}, picture {pic}");
                        }
                        Assert.AreEqual(heights["proto_rifle"], heights["proto_smg"], 0.5f, $"{screen} x{scale}: one scale for both");
                    }
                    hud.AmmoPanel.localScale = Vector3.one;
                    cam.targetTexture = null;
                    rt.Release();
                }
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.renderMode = mode;
                    canvas.worldCamera = null;
                }
                Object.Destroy(camGo);
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>One frame of a dash as drawn (pixels): muzzle and crosshair it used, its tail / head, and where its line really projects.</summary>
        struct DashFrame
        {
            public Vector2 Muzzle, Aim, Tail, Head, TailProjected, HeadProjected, MuzzleNow, Crosshair, EndProjected;
            public float Width;
            public override string ToString() =>
                $"muzzle {Muzzle} (gun now {MuzzleNow}) shot's end {Aim} (projected {EndProjected}, crosshair {Crosshair}) tail {Tail} head {Head} projected {TailProjected} / {HeadProjected} length {Vector2.Distance(Tail, Head):0.0} width {Width:0.0000}";
        }

        /// <summary>Samples after every other LateUpdate (the tracers' included): what is drawn this frame, with this frame's camera.</summary>
        [DefaultExecutionOrder(20000)]
        sealed class LateProbe : MonoBehaviour
        {
            public System.Action Sample;
            void LateUpdate() => Sample?.Invoke();
        }

        /// <summary>One shot at 60 fps, followed until its dash is gone (sampled as drawn: after the LateUpdates).</summary>
        IEnumerator ShootAndFollow(ProjectileVisuals visuals, WeaponViewModel view, System.Collections.Generic.List<DashFrame> frames, bool ads = false)
        {
            var cam = view.WorldCamera;
            Time.captureDeltaTime = 1f / 60f; // the device's frame step (batch mode frames are long)
            var known = new System.Collections.Generic.HashSet<int>(visuals.TracerIds);
            int id = -1;
            bool gone = false;
            var probe = new GameObject("LateProbe").AddComponent<LateProbe>();
            probe.Sample = () =>
            {
                if (gone)
                    return;
                foreach (int i in visuals.TracerIds)
                    if (!known.Contains(i))
                        id = i;
                if (id >= 0 && visuals.TryGetOwnDash(id, out Vector2 m, out Vector2 aim, out Vector2 tail, out Vector2 head)
                    && visuals.TryGetTracer(id, out Vector3 tw, out Vector3 hw) && visuals.TryGetOwnTracerEnd(id, out Vector3 end))
                {
                    view.TryGetMuzzleScreenPoint(out Vector2 now);
                    frames.Add(new DashFrame
                    {
                        Muzzle = m, Aim = aim, Tail = tail, Head = head, MuzzleNow = now, Crosshair = hud.CrosshairScreenPoint,
                        TailProjected = cam.WorldToScreenPoint(tw), HeadProjected = cam.WorldToScreenPoint(hw), Width = visuals.OwnTracerWidth(id),
                        EndProjected = cam.WorldToScreenPoint(end),
                    });
                }
                else if (id >= 0)
                    gone = true;
            };
            try
            {
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads, FireHeld = true, FirePressed = true };
                yield return null;
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                for (int k = 0; k < 30 && !gone; k++)
                    yield return null;
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                router.ExternalSource = null;
                Object.Destroy(probe.gameObject);
            }
        }

        /// <summary>
        /// Checked on screen, frame by frame (within 1 px): the dash's ends are the gun's muzzle as drawn now and where the
        /// shot really ends, as it shows now (its spread and recoil included); its line really projects onto those pixels;
        /// it lies on the segment muzzle → end, starts just past the muzzle, moves forward as a whole, never passes the
        /// end, never moves down, ends at the shot's end.
        /// </summary>
        void CheckDash(System.Collections.Generic.List<DashFrame> frames, string what)
        {
            for (int i = 0; i < frames.Count; i++)
                TestContext.WriteLine($"{what} frame {i}: {frames[i]}");
            float ppt = ProjectileVisuals.PixelsPerPoint;
            Assert.GreaterOrEqual(frames.Count, 3, what + ": seen on a few frames");
            Assert.LessOrEqual(frames.Count, 5, what + ": about 0.05 s, then gone");
            float lastToAim = float.MaxValue, lastFromMuzzle = -1f, lastY = float.MinValue;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                Assert.Less(Vector2.Distance(f.Muzzle, f.MuzzleNow), 1f, $"{what} frame {i}: starts from the muzzle as drawn now (the kick too)");
                Assert.Less(Vector2.Distance(f.Aim, f.EndProjected), 1f, $"{what} frame {i}: heads for where the shot really ends");
                Assert.Less(Vector2.Distance(f.Tail, f.TailProjected), 1f, $"{what} frame {i}: drawn where it says (tail)");
                Assert.Less(Vector2.Distance(f.Head, f.HeadProjected), 1f, $"{what} frame {i}: drawn where it says (head)");
                Vector2 seg = f.Aim - f.Muzzle;
                float len = seg.magnitude;
                Vector2 dir = seg / len;
                float Off(Vector2 p) => Mathf.Abs((p.x - f.Muzzle.x) * dir.y - (p.y - f.Muzzle.y) * dir.x);
                float Along(Vector2 p) => Vector2.Dot(p - f.Muzzle, dir) / len;
                Assert.Less(Off(f.Head), 1f, $"{what} frame {i}: head on the line muzzle → the shot's end");
                Assert.Less(Off(f.Tail), 1f, $"{what} frame {i}: tail on the line muzzle → the shot's end");
                Assert.LessOrEqual(Along(f.Head), 1.0005f, $"{what} frame {i}: never past the shot's end");
                Assert.Greater(Along(f.Tail), 0f, $"{what} frame {i}: not back at the muzzle");
                Assert.LessOrEqual(Vector2.Distance(f.Tail, f.Head), ProjectileVisuals.DashPoints * ppt + 1f, $"{what} frame {i}: a short dash");
                float toAim = Vector2.Distance(f.Head, f.Aim), fromMuzzle = Vector2.Distance(f.Tail, f.Muzzle);
                Assert.LessOrEqual(toAim, lastToAim + 0.5f, $"{what} frame {i}: the head goes toward the shot's end");
                Assert.GreaterOrEqual(fromMuzzle, lastFromMuzzle - 0.5f, $"{what} frame {i}: the tail moves forward too");
                if (f.Aim.y > f.Muzzle.y)
                    Assert.GreaterOrEqual(f.Head.y, lastY - 0.5f, $"{what} frame {i}: never moves down on screen");
                lastToAim = toAim;
                lastFromMuzzle = fromMuzzle;
                lastY = f.Head.y;
            }
            var last = frames[frames.Count - 1];
            Assert.Less(Vector2.Distance(last.Head, last.Aim), 1f, what + ": arrives at the shot's end");
        }

        [UnityTest]
        public IEnumerator OwnShot_ShortDash_FromTheMuzzle_ToTheShotsRealEnd_RifleAndSmg_NearAndFar()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            PhoneSafeArea(); // the crosshair is the screen's centre even where the Safe Area's centre is not
            yield return Wait(0.5f);
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            Assert.AreEqual(0.05f, visuals.TravelTime, 1e-5f, "0.05 s by default (shorter than the 0.06 s before)");
            Assert.AreEqual(14f, ProjectileVisuals.DashPoints, "a little shorter than 18 pt");
            Assert.AreEqual(1.3f, ProjectileVisuals.WidthPoints, 1e-5f, "a little thinner than 1.6 pt");
            foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                foreach (bool far in new[] { false, true })
                {
                    Assert.IsTrue(player.GetComponent<DevGunSelection>().Select(weapon));
                    motor.SetYawPitch(motor.Yaw, far ? 45f : 0f);
                    yield return Wait(0.6f);
                    var frames = new System.Collections.Generic.List<DashFrame>();
                    yield return ShootAndFollow(visuals, view, frames);
                    CheckDash(frames, weapon + (far ? " far" : " near"));
                }
            DevGunSelection.ClearSelection();
        }

        /// <summary>
        /// The debug line: from the gun's muzzle as drawn to the crosshair's centre, within 1 px every frame, also while
        /// the gun kicks in a burst, with the iPhone's Safe Area; off by default.
        /// </summary>
        [UnityTest]
        public IEnumerator DebugAimLine_MuzzleToCrosshairCentre_EveryFrame_ThroughTheKick()
        {
            yield return Load(InputMode.Desktop, false);
            PhoneSafeArea();
            yield return Wait(0.4f);
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var cam = view.WorldCamera;
            Assert.IsFalse(ProjectileVisuals.DebugAimLine, "off by default");
            Assert.IsFalse(visuals.TryGetDebugLine(out _, out _));
            ProjectileVisuals.DebugAimLine = true;
            var samples = new System.Collections.Generic.List<string>();
            int checkedFrames = 0;
            float moved = 0f;
            Vector2 firstMuzzle = default;
            var probe = new GameObject("LateProbe").AddComponent<LateProbe>();
            LineRenderer line = null;
            probe.Sample = () =>
            {
                if (!visuals.TryGetDebugLine(out Vector2 m, out Vector2 aim))
                    return;
                if (line == null)
                    foreach (var l in visuals.GetComponentsInChildren<LineRenderer>(true))
                        if (l.name == "DebugAimLine")
                            line = l;
                view.TryGetMuzzleScreenPoint(out Vector2 now);
                Vector2 p0 = cam.WorldToScreenPoint(line.GetPosition(0)), p1 = cam.WorldToScreenPoint(line.GetPosition(1));
                Assert.Less(Vector2.Distance(m, now), 1f, "starts at the muzzle as drawn now");
                Assert.Less(Vector2.Distance(aim, hud.CrosshairScreenPoint), 1f, "ends at the crosshair's centre");
                Assert.Less(Vector2.Distance(p0, now), 1f, "its start really shows at the muzzle");
                Assert.Less(Vector2.Distance(p1, hud.CrosshairScreenPoint), 1f, "its end really shows at the crosshair's centre");
                if (checkedFrames == 0)
                    firstMuzzle = now;
                moved = Mathf.Max(moved, Vector2.Distance(now, firstMuzzle));
                if (checkedFrames % 10 == 0)
                    samples.Add($"muzzle {now} line {p0} -> {p1} crosshair {hud.CrosshairScreenPoint}");
                checkedFrames++;
            };
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                for (int k = 0; k < 10; k++)
                    yield return null;
                router.ExternalSource = () => new PlayerInputFrame { FireHeld = true, FirePressed = true };
                yield return null;
                router.ExternalSource = () => new PlayerInputFrame { FireHeld = true };
                for (int k = 0; k < 40; k++)
                    yield return null;
            }
            finally
            {
                router.ExternalSource = null;
                Time.captureDeltaTime = 0f;
                ProjectileVisuals.DebugAimLine = false;
                Object.Destroy(probe.gameObject);
            }
            foreach (string sample in samples)
                TestContext.WriteLine(sample);
            TestContext.WriteLine($"{checkedFrames} frames checked; the muzzle moved up to {moved:0.0} px (kick)");
            Assert.Greater(checkedFrames, 40);
            Assert.Greater(moved, 2f, "the gun kicked while it was checked");
            yield return null;
            Assert.IsFalse(visuals.TryGetDebugLine(out _, out _), "off again");
        }

        [UnityTest]
        public IEnumerator OwnShot_Ads_NoDash_FlashOnly_BurstFewDashes()
        {
            yield return Load(InputMode.Desktop, false);
            yield return Wait(0.3f);
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            router.ExternalSource = () => new PlayerInputFrame { AdsActive = true };
            yield return Wait(0.6f);
            Assert.IsTrue(view.ShowsAdsImage);
            var frames = new System.Collections.Generic.List<DashFrame>();
            yield return ShootAndFollow(visuals, view, frames, ads: true);
            Assert.AreEqual(0, frames.Count, "aimed: no dash over the sight picture");
            Assert.AreEqual(0, visuals.OwnTracerCount);
            foreach (bool ads in new[] { false, true })
            {
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                yield return Wait(0.5f);
                int most = 0, shots = 0;
                void OnFired() => shots++;
                gun.Fired += OnFired;
                Time.captureDeltaTime = 1f / 60f;
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads, FireHeld = true, FirePressed = true };
                yield return null;
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads, FireHeld = true };
                for (int k = 0; k < 60; k++)
                {
                    most = Mathf.Max(most, visuals.OwnTracerCount);
                    yield return null;
                }
                router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                for (int k = 0; k < 10; k++)
                    yield return null;
                Time.captureDeltaTime = 0f;
                gun.Fired -= OnFired;
                TestContext.WriteLine($"ads={ads}: {shots} shots in 1 s, at most {most} dashes at once, {visuals.OwnTracerCount} left after");
                Assert.Greater(shots, 5);
                Assert.LessOrEqual(most, ads ? 0 : 1, "one short dash at a time (600 rpm, 0.06 s each); none aimed");
                Assert.AreEqual(0, visuals.OwnTracerCount, "none left after the burst");
            }
            router.ExternalSource = null;
        }

        /// <summary>
        /// The hip weapon sits a little left of its earlier place, still a lower-right hold (muzzle right of centre
        /// and below the crosshair) with the arm coming in from the bottom edge, for the rifle and the SMG on an iPhone
        /// 16 Pro, 16:9 and an iPad (4:3). (The images carry wide transparent margins: their bounds say nothing about cuts.)
        /// </summary>
        [UnityTest]
        public IEnumerator HipWeapon_ALittleLeft_StillLowerRight_RifleAndSmg()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var cam = view.WorldCamera;
            var vmCam = cam.transform.Find("ViewModelCamera").GetComponent<Camera>();
            SpriteRenderer hip = null;
            foreach (var r in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r.sprite != null && r.sprite.name.Contains("_hip_"))
                    hip = r;
            Assert.IsNotNull(hip);
            Assert.AreEqual(0.10f * Mathf.Tan(25f * Mathf.Deg2Rad), WeaponViewModel.HipShiftLeft, 1e-5f);
            var sel = player.GetComponent<DevGunSelection>();
            foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
            {
                Assert.IsTrue(sel.Select(weapon));
                foreach (var size in new[] { new Vector2Int(2622, 1206), new Vector2Int(1920, 1080), new Vector2Int(2048, 1536) })
                {
                    var rt = new RenderTexture(size.x, size.y, 16);
                    cam.targetTexture = vmCam.targetTexture = rt;
                    yield return Wait(0.3f);
                    Assert.IsTrue(view.TryGetMuzzleScreenPoint(out Vector2 m));
                    var b = hip.bounds;
                    Vector3 lo = vmCam.WorldToViewportPoint(b.min), hi = vmCam.WorldToViewportPoint(b.max);
                    Vector2 mv = new Vector2(m.x / size.x, m.y / size.y);
                    TestContext.WriteLine($"{weapon} {size}: muzzle at ({mv.x:0.000}, {mv.y:0.000}) of the screen; image x {lo.x:0.000}–{hi.x:0.000}, y {lo.y:0.000}–{hi.y:0.000}");
                    Assert.Greater(mv.x, 0.55f, $"{weapon} {size}: still on the right");
                    Assert.Less(mv.x, 0.80f, $"{weapon} {size}: a little left of the edge");
                    Assert.Less(mv.y, 0.5f, $"{weapon} {size}: below the crosshair");
                    Assert.LessOrEqual(lo.y, 0.001f, $"{weapon} {size}: the arm comes in from the bottom edge");
                    cam.targetTexture = vmCam.targetTexture = null;
                    rt.Release();
                }
            }
            DevGunSelection.ClearSelection();
        }

        /// <summary>
        /// Hip places A (the earlier one), B (3 % of the screen height lower: the place now), C (5 % lower), the left shift kept: per gun and
        /// screen, the muzzle on screen, the angle of the line muzzle → crosshair, the angle of the gun's own axis on
        /// screen (hands → muzzle) and their difference (how far "sideways" a shot leaves the barrel), and a dash checked
        /// on screen. Comparison only (the place is B); the muzzle must stay on screen below the crosshair.
        /// </summary>
        [UnityTest]
        public IEnumerator HipPlaces_ABC_MuzzleAimAndBarrelAngles_Compared()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var cam = view.WorldCamera;
            var vmCam = cam.transform.Find("ViewModelCamera").GetComponent<Camera>();
            var sel = player.GetComponent<DevGunSelection>();
            try
            {
                foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                {
                    Assert.IsTrue(sel.Select(weapon));
                    foreach (var size in new[] { new Vector2Int(2622, 1206), new Vector2Int(2048, 1536) })
                        foreach (var (place, total) in new[] { ("A (0 %)", 0f), ("B (3 %)", 0.03f), ("C (5 %)", 0.05f), ("stage 14 (9 %)", 0.09f), ("now", -1f) })
                        {
                            // Each place as its total drop from the profile's place; "now" is this screen's own place.
                            float now = WeaponViewModel.HipShiftDownAt((float)size.x / size.y);
                            WeaponViewModel.HipShiftDownForComparison = total < 0f ? 0f : total - now;
                            string name = total < 0f ? $"{place} ({now * 100f:0} %)" : place;
                            var rt = new RenderTexture(size.x, size.y, 16);
                            cam.targetTexture = vmCam.targetTexture = rt;
                            motor.SetYawPitch(motor.Yaw, 0f);
                            yield return Wait(0.4f);
                            Assert.IsTrue(view.TryGetMuzzleScreenPoint(out Vector2 m));
                            Assert.IsTrue(view.TryGetHoldScreenPoint(out Vector2 hold));
                            var centre = new Vector2(size.x, size.y) * 0.5f;
                            float aimAngle = Mathf.Atan2(centre.y - m.y, m.x - centre.x) * Mathf.Rad2Deg; // up-left, from the horizontal
                            float barrelAngle = Mathf.Atan2(m.y - hold.y, hold.x - m.x) * Mathf.Rad2Deg;
                            TestContext.WriteLine($"{weapon} {size} {name}: muzzle ({m.x / size.x:0.000}, {m.y / size.y:0.000}) of the screen, " +
                                $"{(m.x - centre.x) / size.y * 100f:0.0}% of the height right and {(centre.y - m.y) / size.y * 100f:0.0}% below the crosshair; " +
                                $"line to the crosshair {aimAngle:0.0}° up, gun axis {barrelAngle:0.0}° up, difference {barrelAngle - aimAngle:0.0}°");
                            Assert.Less(m.y, centre.y, $"{weapon} {name}: below the crosshair");
                            Assert.Greater(m.y, 0f, $"{weapon} {name}: on screen");
                            Assert.Greater(m.x, centre.x, $"{weapon} {name}: right of the crosshair");
                            if (size.x == 2622)
                            {
                                // A dash on this screen and place, checked on screen.
                                var frames = new System.Collections.Generic.List<DashFrame>();
                                yield return ShootAndFollow(visuals, view, frames);
                                CheckDash(frames, $"{weapon} {name}");
                                yield return Wait(0.3f);
                            }
                            cam.targetTexture = vmCam.targetTexture = null;
                            rt.Release();
                        }
                }
            }
            finally
            {
                WeaponViewModel.HipShiftDownForComparison = 0f;
                cam.targetTexture = vmCam.targetTexture = null;
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>
        /// The hip weapon is 14 % of the screen's height lower than its profile's place on an iPhone 16 Pro (5 % below the
        /// stage-14 place, 9 %) and still 9 % on an iPad (4:3); the left shift kept, rifle and SMG; the muzzle stays on
        /// screen below the crosshair; the ADS picture does not move.
        /// </summary>
        [UnityTest]
        public IEnumerator HipWeapon_FourteenPercentLowerOnIPhone_NineOnIPad_LeftShiftKept_AdsUnmoved()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var cam = view.WorldCamera;
            var vmCam = cam.transform.Find("ViewModelCamera").GetComponent<Camera>();
            var sel = player.GetComponent<DevGunSelection>();
            Assert.AreEqual(0.09f, WeaponViewModel.HipShiftDownPart, "3 % + 6 %");
            Assert.AreEqual(0.14f, WeaponViewModel.HipShiftDownAt(2622f / 1206f), 1e-5f, "iPhone 16 Pro: 5 % more");
            Assert.AreEqual(0.14f, WeaponViewModel.HipShiftDownAt(2.4f), 1e-5f, "no more on wider screens");
            Assert.AreEqual(0.09f, WeaponViewModel.HipShiftDownAt(4f / 3f), 1e-5f, "iPad 4:3: unchanged");
            try
            {
                foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                {
                    Assert.IsTrue(sel.Select(weapon));
                    foreach (var size in new[] { new Vector2Int(2622, 1206), new Vector2Int(2048, 1536) })
                    {
                        var rt = new RenderTexture(size.x, size.y, 16);
                        cam.targetTexture = vmCam.targetTexture = rt;
                        Vector2 Muzzle()
                        {
                            Assert.IsTrue(view.TryGetMuzzleScreenPoint(out Vector2 m));
                            return new Vector2(m.x / size.x, m.y / size.y);
                        }
                        foreach (bool ads in new[] { false, true })
                        {
                            router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                            WeaponViewModel.HipShiftDownForComparison = 0.09f - WeaponViewModel.HipShiftDownAt((float)size.x / size.y); // the stage-14 place (9 % down)
                            yield return Wait(0.5f);
                            Vector2 before = Muzzle();
                            WeaponViewModel.HipShiftDownForComparison = 0f;
                            yield return Wait(0.3f);
                            Vector2 now = Muzzle();
                            TestContext.WriteLine($"{weapon} {size} {(ads ? "ADS" : "hip")}: muzzle {before} -> {now}");
                            if (ads)
                                Assert.AreEqual(before, now, $"{weapon} {size}: ADS not moved");
                            else
                            {
                                Assert.AreEqual(size.x == 2622 ? 0.05f : 0f, before.y - now.y, 0.0015f, $"{weapon} {size}: 5 % lower than before on the iPhone, unchanged on the iPad");
                                Assert.AreEqual(before.x, now.x, 0.0005f, $"{weapon} {size}: same left-right place (the left shift kept)");
                                Assert.Greater(now.y, 0.05f, $"{weapon} {size}: the muzzle still well on screen");
                                Assert.Less(now.y, 0.5f, $"{weapon} {size}: below the crosshair");
                            }
                        }
                        router.ExternalSource = null;
                        cam.targetTexture = vmCam.targetTexture = null;
                        rt.Release();
                    }
                }
            }
            finally
            {
                WeaponViewModel.HipShiftDownForComparison = 0f;
                router.ExternalSource = null;
                cam.targetTexture = vmCam.targetTexture = null;
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>
        /// The ammo panel (count, divider, picture or name, face, frame, posture line) is exactly as see-through as the
        /// on-screen controls: 0 / 50 / 100 % transparency, set from the settings screen's slider (live preview), full
        /// strength while the layout editor is open, after a weapon switch, and after a restart; one CanvasGroup only
        /// (nothing applied twice). The Editor's setting is put back afterwards.
        /// </summary>
        [UnityTest]
        public IEnumerator AmmoPanel_TakesTheControlsTransparency_SliderEditorSwitchRestart()
        {
            bool had = PlayerPrefs.HasKey(ControlSettings.TouchTransparencyKey);
            int saved = PlayerPrefs.GetInt(ControlSettings.TouchTransparencyKey, 0);
            DevGunSelection.ClearSelection();
            try
            {
                yield return Load(InputMode.Touch, false);
                var controls = Object.FindAnyObjectByType<TouchControlsOpacity>();
                Assert.IsNotNull(controls);
                Assert.AreEqual(1, hud.AmmoPanel.GetComponentsInChildren<CanvasGroup>(true).Length, "one group: nothing applied twice");
                Assert.IsFalse(hud.AmmoPanel.IsChildOf(controls.transform), "not under the controls' group as well");
                Assert.AreEqual(1f, hud.AmmoText.color.a, 1e-4f, "the count keeps its own alpha");
                menu.OpenControlSettings();
                yield return null;
                var slider = GameObject.Find("TransparencySlider").GetComponent<UnityEngine.UI.Slider>();
                foreach (int t in new[] { 0, 50, 100 })
                {
                    slider.value = t; // the settings screen (live preview)
                    yield return null;
                    TestContext.WriteLine($"transparency {t}%: controls {controls.Alpha:0.00}, ammo panel {hud.AmmoPanelAlpha:0.00}");
                    Assert.AreEqual(t, ControlSettings.TouchControlTransparency);
                    Assert.AreEqual(1f - t / 100f, controls.Alpha, 1e-4f);
                    Assert.AreEqual(controls.Alpha, hud.AmmoPanelAlpha, 1e-4f, $"{t}%: as see-through as the buttons");
                }
                slider.value = 50;
                menu.CloseControlSettings();
                menu.Close();
                yield return null;
                Assert.AreEqual(0.5f, hud.AmmoPanelAlpha, 1e-4f);

                menu.OpenLayoutEditor();
                yield return null;
                Assert.AreEqual(1f, controls.Alpha, 1e-4f, "editing: the controls at full strength");
                Assert.AreEqual(1f, hud.AmmoPanelAlpha, 1e-4f, "and the ammo panel too");
                editor.CancelAndClose();
                menu.Close();
                yield return null;
                Assert.AreEqual(0.5f, hud.AmmoPanelAlpha, 1e-4f);

                Assert.IsTrue(player.GetComponent<DevGunSelection>().Select("proto_smg"));
                yield return null;
                Assert.IsNotNull(hud.AmmoArt, "the SMG's side view");
                Assert.AreSame(menu.Skins.WeaponSet("proto_smg").sideView, hud.AmmoArt.sprite);
                Assert.IsTrue(hud.AmmoArt.transform.IsChildOf(hud.AmmoPanel), "under the same group (its CanvasGroup)");
                Assert.AreEqual("", hud.AmmoCaption.text);
                Assert.AreEqual(0.5f, hud.AmmoPanelAlpha, 1e-4f, "after a weapon switch");
                DevGunSelection.ClearSelection();

                yield return Load(InputMode.Touch, false); // next start
                Assert.AreEqual(50, ControlSettings.TouchControlTransparency, "kept");
                Assert.AreEqual(0.5f, hud.AmmoPanelAlpha, 1e-4f, "after a restart");
                Assert.IsTrue(hud.AmmoArt.transform.IsChildOf(hud.AmmoPanel));
                Assert.IsTrue(hud.StanceText.transform.IsChildOf(hud.AmmoPanel), "the posture line goes with it");
            }
            finally
            {
                if (had)
                    PlayerPrefs.SetInt(ControlSettings.TouchTransparencyKey, saved);
                else
                    PlayerPrefs.DeleteKey(ControlSettings.TouchTransparencyKey);
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>Every Safe Area narrowed like an iPhone 16 Pro in landscape: 186 px each side, 63 px at the bottom (of 2622 x 1206).</summary>
        static void PhoneSafeArea()
        {
            foreach (var fitter in Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                fitter.enabled = false;
                var r = (RectTransform)fitter.transform;
                r.anchorMin = new Vector2(186f / 2622f, 63f / 1206f);
                r.anchorMax = new Vector2(1f - 186f / 2622f, 1f);
            }
        }

        /// <summary>
        /// Shots go where the crosshair is: with the iPhone's Safe Area (home indicator inset at the bottom only), the
        /// crosshair is at the screen's centre (where the camera points), and a shot with no spread and no recoil (a
        /// runtime copy of the gun; assets untouched) hits there, its tracer ending at the hit, within 2 px (of 640 x 480:
        /// half a pixel of projection rounding, a pixel of the hit sitting on a surface at an angle). Hip and ADS, rifle
        /// and SMG. With spread and recoil on, a shot stays within its spread cone around where the crosshair was. The
        /// dash always arrives where its shot really ends (at the centre when there is no spread or recoil).
        /// </summary>
        [UnityTest]
        public IEnumerator Shots_GoWhereTheCrosshairIs_HitAtTheCentre_DashToTheShotsEnd_WithThePhoneSafeArea()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            PhoneSafeArea();
            yield return null;
            yield return null;
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var cam = view.WorldCamera;
            var screenCentre = new Vector2(cam.pixelWidth, cam.pixelHeight) * 0.5f;
            Vector2 crosshair = hud.CrosshairScreenPoint;
            var safe = ScreenRect(applier.SafeArea);
            TestContext.WriteLine($"screen centre {screenCentre}, crosshair {crosshair}, Safe Area centre {safe.center}");
            Assert.Less(Vector2.Distance(crosshair, screenCentre), 0.5f, "the crosshair is at the screen's centre (not the Safe Area's)");
            Assert.Greater(Vector2.Distance(safe.center, screenCentre), 5f, "the Safe Area's centre is elsewhere here (the case that went wrong)");

            var hits = new System.Collections.Generic.List<ProjectileHit>();
            projectiles.Hit += h => hits.Add(h);
            var sel = player.GetComponent<DevGunSelection>();
            try
            {
                foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                    foreach (bool ads in new[] { false, true })
                        foreach (bool baseline in new[] { true, false })
                        {
                            Assert.IsTrue(sel.Select(weapon));
                            var official = gun.Definition;
                            WeaponDefinition copy = null;
                            if (baseline)
                            {
                                copy = Object.Instantiate(official); // a runtime copy: no spread, no recoil
                                copy.hipSpreadDegrees = copy.adsSpreadDegrees = 0f;
                                copy.recoilPitchDegrees = copy.recoilYawDegrees = 0f;
                                if (copy.recoilProfile != null)
                                    copy.recoilProfile = new RecoilProfile { enabled = false };
                                gun.SetDefinitionKeepingState(copy);
                            }
                            motor.SetYawPitch(motor.Yaw, 0f);
                            router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                            yield return Wait(0.6f);
                            Vector2 aim = ads ? screenCentre : hud.CrosshairScreenPoint; // ADS: the sights are at the centre
                            Vector3 camPos = cam.transform.position;
                            Quaternion camRot = cam.transform.rotation;
                            float spread = gun.CurrentSpreadDegrees;
                            int before = hits.Count;
                            var known = new System.Collections.Generic.HashSet<int>(visuals.TracerIds);
                            int dashId = -1;
                            Time.captureDeltaTime = 1f / 60f; // the device's frame step
                            router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads, FireHeld = true, FirePressed = true };
                            yield return null;
                            router.ExternalSource = () => new PlayerInputFrame { AdsActive = ads };
                            foreach (int i in visuals.TracerIds)
                                if (!known.Contains(i))
                                    dashId = i;
                            bool haveTracer = dashId >= 0;
                            Vector2 dashHead = default, dashEnd = default;
                            for (int k = 0; k < 10 && haveTracer && visuals.TryGetOwnDash(dashId, out _, out _, out _, out Vector2 h); k++)
                            {
                                dashHead = h;
                                visuals.TryGetOwnTracerEnd(dashId, out Vector3 e);
                                dashEnd = cam.WorldToScreenPoint(e); // the shot's real end as it shows on that frame
                                yield return null;
                            }
                            for (int k = 0; k < 90 && hits.Count == before; k++)
                                yield return null;
                            Time.captureDeltaTime = 0f;
                            Assert.Greater(hits.Count, before, $"{weapon} {(ads ? "ADS" : "hip")}: the shot hit something");
                            var hit = hits[hits.Count - 1];
                            // Project with the camera as it was when fired (recoil turns it afterwards).
                            Vector2 Project(Vector3 p)
                            {
                                Vector3 local = Quaternion.Inverse(camRot) * (p - camPos);
                                var now = cam.transform;
                                return cam.WorldToScreenPoint(now.position + now.rotation * local);
                            }
                            Vector2 hitOnScreen = Project(hit.Point);
                            float off = Vector2.Distance(hitOnScreen, aim);
                            string what = $"{weapon} {(ads ? "ADS" : "hip")} {(baseline ? "no spread / recoil" : "normal")}";
                            TestContext.WriteLine($"{what}: aim {aim}, hit {hitOnScreen} ({hit.Distance:0.0} m) off by {off:0.00} px" +
                                (haveTracer ? $", dash last at {dashHead} ({Vector2.Distance(dashHead, aim):0.00} px from the crosshair, {Vector2.Distance(dashHead, dashEnd):0.00} px from the shot's real end; the hit is {off:0.00} px from the crosshair)" : ", no dash (ADS)"));
                            if (baseline)
                                Assert.Less(off, 2f, what + ": hits the crosshair's centre");
                            else
                            {
                                float cone = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.pixelHeight * 0.5f;
                                Assert.LessOrEqual(off, cone + 2f, what + $": within the spread cone ({spread:0.00}° = {cone:0.0} px)");
                            }
                            if (haveTracer)
                            {
                                Assert.Less(Vector2.Distance(dashHead, dashEnd), 1f, what + ": the dash arrives where the shot really ends");
                                if (baseline)
                                    Assert.Less(Vector2.Distance(dashHead, hud.CrosshairScreenPoint), 2f, what + ": no spread or recoil: at the crosshair's centre");
                            }
                            Assert.IsTrue(!ads || !haveTracer, "ADS: no dash");
                            router.ExternalSource = null;
                            if (copy != null)
                            {
                                gun.SetDefinitionKeepingState(official);
                                Object.Destroy(copy);
                            }
                            yield return Wait(0.5f);
                        }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                router.ExternalSource = null;
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>
        /// A hip burst with the guns' own spread and recoil (nothing changed): every dash heads, on every frame, for where
        /// its own shot really ends (within 1 px); so the dashes spread around the crosshair like the shots do, instead of
        /// all meeting at its centre; none goes far off. Rifle and SMG.
        /// </summary>
        [UnityTest]
        public IEnumerator HipBurst_EachDash_FollowsItsOwnShot_TheSpreadShows_RifleAndSmg()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            PhoneSafeArea();
            var visuals = Object.FindAnyObjectByType<ProjectileVisuals>();
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var cam = view.WorldCamera;
            var sel = player.GetComponent<DevGunSelection>();
            try
            {
                foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                {
                    Assert.IsTrue(sel.Select(weapon));
                    motor.SetYawPitch(motor.Yaw, 0f);
                    yield return Wait(0.6f);
                    var lastAim = new System.Collections.Generic.Dictionary<int, Vector2>();
                    float worst = 0f;
                    int frames = 0;
                    var probe = new GameObject("LateProbe").AddComponent<LateProbe>();
                    probe.Sample = () =>
                    {
                        foreach (int id in visuals.TracerIds)
                            if (visuals.TryGetOwnDash(id, out _, out Vector2 aim, out _, out _) && visuals.TryGetOwnTracerEnd(id, out Vector3 end))
                            {
                                worst = Mathf.Max(worst, Vector2.Distance(aim, cam.WorldToScreenPoint(end)));
                                lastAim[id] = aim;
                                frames++;
                            }
                    };
                    Time.captureDeltaTime = 1f / 60f;
                    router.ExternalSource = () => new PlayerInputFrame { FireHeld = true, FirePressed = true };
                    yield return null;
                    router.ExternalSource = () => new PlayerInputFrame { FireHeld = true };
                    for (int k = 0; k < 40; k++)
                        yield return null;
                    router.ExternalSource = null;
                    for (int k = 0; k < 10; k++)
                        yield return null;
                    Time.captureDeltaTime = 0f;
                    Object.Destroy(probe.gameObject);
                    var offs = new System.Collections.Generic.List<float>();
                    foreach (var aim in lastAim.Values)
                        offs.Add(Vector2.Distance(aim, hud.CrosshairScreenPoint));
                    TestContext.WriteLine($"{weapon}: {lastAim.Count} dashes over {frames} frames; each dash's end from the crosshair (px): " +
                        string.Join(" ", offs.ConvertAll(o => o.ToString("0.0"))) + $"; worst off its shot's real end {worst:0.00} px");
                    Assert.GreaterOrEqual(lastAim.Count, 4, weapon + ": a burst");
                    Assert.Less(worst, 1f, weapon + ": every dash heads for its own shot's end");
                    offs.Sort();
                    Assert.Greater(offs[offs.Count - 1], 2f, weapon + ": the dashes spread like the shots (not all at the centre)");
                    Assert.Greater(offs[offs.Count - 1] - offs[0], 1f, weapon + ": not all the same");
                    Assert.Less(offs[offs.Count - 1], cam.pixelHeight * 0.15f, weapon + ": none goes far off");
                    yield return Wait(0.6f);
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                router.ExternalSource = null;
                DevGunSelection.ClearSelection();
            }
        }

        /// <summary>
        /// ADS: the flash shows just past the front sight (at most <see cref="WeaponViewModel.AdsFlashMaxBelowSight"/> under
        /// its tip, mid-screen) and is drawn behind the ADS image, never on the gun's top; at the hip it stays on the muzzle
        /// tip, in front. Rifle and SMG (each gun's own place after a switch).
        /// </summary>
        [UnityTest]
        public IEnumerator Flash_Ads_JustPastTheFrontSight_BehindTheGun_Hip_OnTheMuzzle_RifleAndSmg()
        {
            DevGunSelection.ClearSelection();
            yield return Load(InputMode.Desktop, false);
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            var renderers = view.GetComponentsInChildren<SpriteRenderer>(true);
            SpriteRenderer Find(string name, string parent) =>
                System.Array.Find(renderers, r => r.name == name && (parent == null || r.transform.parent.name == parent));
            SpriteRenderer hip = Find("Hip", null), ads = Find("Ads", null), hipFlash = Find("MuzzleFlash", "Hip"), adsFlash = Find("MuzzleFlash", "Ads");
            Assert.IsNotNull(hip); Assert.IsNotNull(ads); Assert.IsNotNull(hipFlash); Assert.IsNotNull(adsFlash);
            var vmCam = view.WorldCamera.transform.Find("ViewModelCamera").GetComponent<Camera>();
            var sel = player.GetComponent<DevGunSelection>();
            float unitPx = vmCam.pixelHeight / (2f * Mathf.Tan(25f * Mathf.Deg2Rad)); // sprite units → pixels at z = 1
            try
            {
                foreach (string weapon in new[] { "proto_rifle", "proto_smg" })
                {
                    Assert.IsTrue(sel.Select(weapon));
                    Assert.Less(adsFlash.sortingOrder, ads.sortingOrder, weapon + ": ADS flash behind the gun");
                    Assert.Greater(hipFlash.sortingOrder, hip.sortingOrder, weapon + ": hip flash in front, on the muzzle tip");
                    foreach (bool aimed in new[] { true, false })
                    {
                        router.ExternalSource = () => new PlayerInputFrame { AdsActive = aimed };
                        yield return Wait(0.8f);
                        Vector2 rest = default;
                        Assert.IsTrue(view.TryGetMuzzleScreenPoint(out rest));
                        router.ExternalSource = () => new PlayerInputFrame { AdsActive = aimed, FireHeld = true, FirePressed = true };
                        yield return null;
                        router.ExternalSource = () => new PlayerInputFrame { AdsActive = aimed };
                        yield return null;
                        var shown = aimed ? adsFlash : hipFlash;
                        Assert.IsTrue(shown.enabled, $"{weapon} {(aimed ? "ADS" : "hip")}: the flash shows");
                        Assert.IsFalse((aimed ? hipFlash : adsFlash).enabled, "only one flash");
                        Vector2 flash = vmCam.WorldToScreenPoint(shown.transform.position);
                        TestContext.WriteLine($"{weapon} {(aimed ? "ADS" : "hip")}: flash at {flash}, rest muzzle {rest}, screen centre {new Vector2(vmCam.pixelWidth, vmCam.pixelHeight) * 0.5f}");
                        if (aimed)
                        {
                            Vector2 sight = new Vector2(vmCam.pixelWidth, vmCam.pixelHeight) * 0.5f; // the front sight's tip
                            Assert.AreEqual(sight.x, rest.x, 1f, weapon + ": mid-screen, under the front sight");
                            float below = sight.y - rest.y;
                            Assert.Greater(below, 0f, weapon + ": just under the front sight's tip");
                            Assert.LessOrEqual(below, WeaponViewModel.AdsFlashMaxBelowSight * unitPx + 1f, weapon + ": not down on the gun's top");
                        }
                        Assert.IsTrue(view.TryGetMuzzleScreenPoint(out Vector2 muzzleNow));
                        Assert.Less(Vector2.Distance(flash, muzzleNow), 0.5f, weapon + ": the flash is where the muzzle is drawn (the kick included)");
                    }
                }
            }
            finally
            {
                router.ExternalSource = null;
                DevGunSelection.ClearSelection();
            }
        }
    }
}
