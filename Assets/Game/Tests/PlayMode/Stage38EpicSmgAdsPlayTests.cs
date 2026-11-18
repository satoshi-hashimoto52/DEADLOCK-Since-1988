using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 38, EPIC SMG ADS (optical sight; v3, then the new design of 2026-11-08) in the firing range: aimed, the green dot (the ADS image's pivot, its
    /// centre measured on the image) is on the screen centre of the iPhone, 16:9 and 4:3 views, and every shot leaves along
    /// the camera's aim within the gun's spread, so the dot and the shots agree; the ADS glow layers show with the ADS
    /// image (not the hip's) and follow it through hip ⇄ ADS, a shot (its flash under the glass, the dot clear) and a
    /// switch; the other guns' ADS keep their front sight on the centre and no layer over it.
    /// </summary>
    public class Stage38EpicSmgAdsPlayTests : FiringRangeTestBase
    {
        const string Epic = "proto_epic_smg";
        static readonly (string name, int w, int h)[] Screens = { ("iphone", 2622, 1206), ("16x9", 1920, 1080), ("4x3", 2048, 1536) };

        public override void TearDown()
        {
            Time.captureDeltaTime = 0f;
            router.ExternalSource = null;
            DevGunSelection.ClearSelection();
            base.TearDown();
        }

        WeaponViewModel View => Object.FindAnyObjectByType<WeaponViewModel>();

        /// <summary>Equips a main gun of the catalog as the dev loadout does (any gun, not only the rifle / SMG pair).</summary>
        void Equip(DevGunSelection sel, string id)
        {
            Assert.IsTrue(sel.TryEquipMain(sel.MainGunChoices.First(d => d.weaponId == id), out string error), $"{id}: {error}");
        }

        Camera ViewCamera(WeaponViewModel view) =>
            Camera.allCameras.First(c => c != view.WorldCamera && (c.cullingMask & (1 << view.Presenter.Ads.gameObject.layer)) != 0);

        IEnumerator Aim(bool aimed, bool fire = false)
        {
            router.ExternalSource = () => new PlayerInputFrame { AdsActive = aimed, FireHeld = fire, FirePressed = fire };
            yield return null;
        }

        [UnityTest]
        public IEnumerator EpicSmgAds_TheDotIsOnTheScreenCentre_OnEveryAspect_WhereTheShotsGo()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            var sel = player.GetComponent<DevGunSelection>();
            Equip(sel, Epic);
            yield return Wait(1f);
            var view = View;
            var p = view.Presenter;
            Assert.IsNotNull(p.Profile.adsSprite, "the EPIC SMG has its ADS image");
            yield return Aim(true);
            yield return Wait(0.8f);
            Assert.AreEqual(1f, p.AdsAlpha, 1e-4f, "fully aimed");
            var vm = ViewCamera(view);
            var world = view.WorldCamera;
            var sprite = p.Ads.sprite;
            // The dot centre (888, 438) of the 1774 x 887 image, in the sprite's units from its pivot: the pivot itself.
            Vector2 dot = new Vector2((888f - sprite.pivot.x) / sprite.pixelsPerUnit, ((887f - 438f) - sprite.pivot.y) / sprite.pixelsPerUnit);
            Assert.Less(dot.magnitude, 1e-4f, "the ADS pivot is the dot centre");
            try
            {
                foreach (var (name, w, h) in Screens)
                {
                    var rt = new RenderTexture(w, h, 24);
                    world.targetTexture = rt;
                    vm.aspect = (float)w / h;
                    yield return null;
                    Vector3 v = vm.WorldToViewportPoint(p.Ads.transform.TransformPoint(dot));
                    Assert.AreEqual(w * 0.5f, v.x * w, 0.5f, $"{name}: the dot on the centre (x, pixels)");
                    Assert.AreEqual(h * 0.5f, v.y * h, 0.5f, $"{name}: the dot on the centre (y, pixels)");
                    Vector3 aim = world.WorldToViewportPoint(world.transform.position + world.transform.forward * 30f);
                    Assert.AreEqual(0.5f, aim.x, 1e-4f, $"{name}: the camera's aim is the centre too");
                    Assert.AreEqual(0.5f, aim.y, 1e-4f);
                    world.targetTexture = null;
                    rt.Release();
                    vm.ResetAspect();
                }
            }
            finally
            {
                world.targetTexture = null;
                vm.ResetAspect();
            }
            // Shots aimed: each leaves along the camera's aim of its moment, within the gun's spread.
            var shots = new List<(float angle, float spread)>();
            System.Action<int, Vector3> spawned = (id, origin) =>
            {
                if (projectiles.TryPredictEnd(id, out var end, out var direction))
                    shots.Add((Vector3.Angle(direction, world.transform.forward), gun.CurrentSpreadDegrees));
            };
            projectiles.Spawned += spawned;
            try
            {
                for (int k = 0; k < 6; k++)
                {
                    yield return Aim(true, fire: true);
                    yield return Aim(true);
                    if (k == 0)
                    {
                        Assert.IsTrue(p.AdsFlash.enabled, "the flash shows aimed");
                        Assert.Less(p.AdsFlash.sortingOrder, p.Ads.sortingOrder, "behind the gun");
                        // Under the sight glass (its lower edge y 511): the flash's tips stop short of the dot.
                        float flashTop = vm.WorldToScreenPoint(p.AdsFlash.transform.position).y
                                         + p.AdsFlash.bounds.extents.y * vm.pixelHeight / (2f * Mathf.Tan(25f * Mathf.Deg2Rad));
                        float dotBottom = vm.WorldToScreenPoint(p.Ads.transform.TransformPoint(new Vector2(0f, -6f / sprite.pixelsPerUnit))).y;
                        Assert.Less(flashTop, dotBottom, "the flash never covers the dot");
                        Assert.IsTrue(p.AdsGlow.enabled && p.AdsEmission.enabled, "the glow layers stay on while firing");
                    }
                    yield return Wait(0.3f);
                }
            }
            finally
            {
                projectiles.Spawned -= spawned;
            }
            Assert.GreaterOrEqual(shots.Count, 6);
            foreach (var (angle, spread) in shots)
                Assert.LessOrEqual(angle, spread + 0.05f, "the shot leaves along the aim (the dot) within the spread (as ShotAimTests)");
        }

        [UnityTest]
        public IEnumerator EpicSmgAds_GlowLayersFollowTheAdsImage_ThroughHipAdsAndASwitch()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            var sel = player.GetComponent<DevGunSelection>();
            Equip(sel, Epic);
            yield return Wait(1f);
            var p = View.Presenter;
            Assert.IsTrue(p.Glow.enabled && p.Emission.enabled, "hip: the hip's layers");
            Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled, "hip: no ADS layer");
            Time.captureDeltaTime = 1f / 60f; // frames of a 60 fps screen: the cross-fade takes several of them
            yield return Aim(true);
            bool sawCrossFade = false;
            for (int k = 0; k < 60 && p.AdsAlpha < 1f; k++)
            {
                // The view is drawn in LateUpdate with that frame's clock, after this coroutine runs: what is read after
                // the yield was drawn at the clock of this frame (before the yield), not of the next one.
                float drawnAt = Time.time;
                yield return null;
                if (p.AdsAlpha > 0.001f && p.AdsAlpha < 0.999f)
                {
                    sawCrossFade = true;
                    Assert.AreEqual(p.AdsAlpha * p.Profile.adsGlowIntensity * WeaponViewPresenter.AdsGlowPulse(p.Profile, drawnAt), p.AdsGlow.color.a, 1e-3f,
                        "the ADS glow fades in with the ADS image");
                }
                foreach (var layer in new[] { p.AdsGlow, p.AdsEmission })
                {
                    Assert.AreSame(p.Ads.transform, layer.transform.parent);
                    Assert.AreEqual(Vector2.zero, (Vector2)layer.transform.localPosition, "on the ADS image's pivot");
                }
            }
            Assert.IsTrue(sawCrossFade, "the cross-fade was seen");
            Time.captureDeltaTime = 0f;
            yield return Wait(0.5f);
            Assert.IsTrue(p.AdsGlow.enabled && p.AdsEmission.enabled && p.Ads.enabled, "aimed: the ADS layers");
            Assert.IsFalse(p.Glow.enabled || p.Emission.enabled, "aimed: no hip layer");
            Assert.That(p.AdsGlow.color.a, Is.InRange(0.2f * 0.7f - 1e-3f, 0.2f + 1e-3f), "the ADS glow's own, gentle breath");
            // A switch while aimed: the old gun goes down with its layers, the new one has none of the EPIC's.
            Equip(sel, "proto_smg");
            for (int k = 0; k < 90; k++)
            {
                yield return null;
                if (!p.Ads.enabled)
                    Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled, "no layer without its image");
            }
            yield return Wait(0.5f);
            Assert.AreEqual("proto_smg", gun.Definition.weaponId);
            Assert.IsNull(p.AdsGlow.sprite, "the SMG has no ADS glow");
            Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled);
            yield return Aim(false);
            yield return Wait(0.6f);
            Assert.AreEqual(0f, p.AdsAlpha, 1e-4f);
        }

        static Color32[] Grab(Camera world, Camera vm, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                world.targetTexture = rt;
                vm.aspect = (float)w / h;
                world.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = prev;
                return tex.GetPixels32(); // row 0 at the bottom
            }
            finally
            {
                world.targetTexture = null;
                vm.ResetAspect();
                rt.Release();
                Object.Destroy(tex);
            }
        }

        static bool Differs(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 6;

        /// <summary>
        /// The new ADS design (2026-11-08) as drawn in the firing range on the iPhone, 16:9 and 4:3 screens, aimed: the
        /// screen centre is the green dot (glow on and off); the glass of the octagonal frame shows the world unchanged
        /// around it; the frame's posts are drawn; the gun runs off the bottom edge of the screen (no cut above it).
        /// </summary>
        [UnityTest]
        public IEnumerator EpicSmgAds_NewDesign_DrawnOnEveryScreen_DotOnTheCentre_ClearGlass_NoCutAtTheBottom()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            var sel = player.GetComponent<DevGunSelection>();
            Equip(sel, Epic);
            yield return Wait(1f);
            var view = View;
            var p = view.Presenter;
            yield return Aim(true);
            yield return Wait(0.8f);
            Assert.AreEqual(1f, p.AdsAlpha, 1e-4f, "fully aimed");
            var vm = ViewCamera(view);
            var world = view.WorldCamera;
            bool glow = WeaponViewPresenter.GlowEnabled;
            try
            {
                foreach (var (name, w, h) in Screens)
                {
                    float s = h / 772f; // the ADS canvas: 772 image pixels to the screen height
                    WeaponViewPresenter.GlowEnabled = true;
                    yield return null;
                    var lit = Grab(world, vm, w, h);
                    WeaponViewPresenter.GlowEnabled = false;
                    yield return null;
                    var drawn = Grab(world, vm, w, h);
                    var layers = new[] { p.Ads, p.AdsEmission, p.AdsGlow };
                    var on = System.Array.ConvertAll(layers, r => r.enabled);
                    foreach (var r in layers)
                        r.enabled = false;
                    var bare = Grab(world, vm, w, h);
                    for (int k = 0; k < layers.Length; k++)
                        layers[k].enabled = on[k];
                    int cx = w / 2, cy = h / 2;
                    foreach (var (img, what) in new[] { (lit, "glow on"), (drawn, "glow off") })
                    {
                        // The dot (16 x 12 image px) on the centre: its near-white middle there, green around it.
                        var c = img[cy * w + cx];
                        Assert.Greater(c.g, 200, $"{name} {what}: the screen centre is the dot's bright middle ({c})");
                        int green = 0, all = 0, rx = Mathf.RoundToInt(7 * s), ry = Mathf.RoundToInt(5 * s);
                        for (int y = cy - ry; y <= cy + ry; y++)
                            for (int x = cx - rx; x <= cx + rx; x++)
                            {
                                var q = img[y * w + x];
                                all++;
                                green += q.g > 96 && q.g > q.r + 30 && q.g > q.b + 30 ? 1 : 0;
                            }
                        Assert.Greater(green * 3, all, $"{name} {what}: the dot around the centre is green ({green} / {all})");
                    }
                    // The glass around the dot (image pixels from the dot centre, inside the window x 812-963, y 376-503).
                    int glassSeen = 0, changed = 0;
                    for (int dy = -55; dy <= 55; dy += 5)
                        for (int dx = -68; dx <= 68; dx += 4)
                        {
                            if (Mathf.Abs(dx) <= 14 && Mathf.Abs(dy) <= 12)
                                continue; // the dot
                            if (Mathf.Abs(dx) + Mathf.Abs(dy) > 100)
                                continue; // the frame's cut corners
                            int x = cx + Mathf.RoundToInt(dx * s), y = cy - Mathf.RoundToInt(dy * s);
                            glassSeen++;
                            changed += Differs(drawn[y * w + x], bare[y * w + x]) ? 1 : 0;
                        }
                    Assert.Greater(glassSeen, 500);
                    Assert.AreEqual(0, changed, $"{name}: the sight glass shows the world unchanged");
                    // The posts of the frame (x 748-811 and 964-1023 on the image), below the side lamps, are drawn.
                    foreach (int dx in new[] { -100, 105 })
                    {
                        int x = cx + Mathf.RoundToInt(dx * s), y = cy - Mathf.RoundToInt(30 * s);
                        Assert.IsTrue(Differs(drawn[y * w + x], bare[y * w + x]), $"{name}: the frame's post at {dx} image px");
                    }
                    // The bottom row of the screen: the gun across the middle, no cut above the edge.
                    int gunAtBottom = 0;
                    for (int x = cx - Mathf.RoundToInt(150 * s); x <= cx + Mathf.RoundToInt(150 * s); x++)
                        gunAtBottom += Differs(drawn[x], bare[x]) ? 1 : 0;
                    Assert.AreEqual(2 * Mathf.RoundToInt(150 * s) + 1, gunAtBottom, $"{name}: the gun runs off the bottom of the screen");
                }
            }
            finally
            {
                WeaponViewPresenter.GlowEnabled = glow;
            }
        }

        [UnityTest]
        public IEnumerator OtherGuns_Ads_FrontSightOnTheCentre_NoLayers()
        {
            DevGunSelection.ClearSelection();
            yield return LoadScene(InputMode.Desktop);
            var sel = player.GetComponent<DevGunSelection>();
            foreach (var id in new[] { "proto_rifle", "proto_smg", "proto_lmg" })
            {
                Equip(sel, id);
                yield return Aim(false);
                yield return Wait(0.8f);
                yield return Aim(true);
                yield return Wait(0.8f);
                var view = View;
                var p = view.Presenter;
                Assert.AreEqual(1f, p.AdsAlpha, 1e-4f, id);
                Vector3 v = ViewCamera(view).WorldToViewportPoint(p.Ads.transform.position);
                Assert.AreEqual(0.5f, v.x, 1e-4f, $"{id}: its front-sight tip on the centre");
                Assert.AreEqual(0.5f, v.y, 1e-4f, $"{id}: its front-sight tip on the centre");
                Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled, $"{id}: no layer over its ADS image");
                Assert.IsFalse(p.Profile.adsMuzzleFlashUnderSight, $"{id}: its flash is held under the front sight as before");
            }
        }
    }
}
