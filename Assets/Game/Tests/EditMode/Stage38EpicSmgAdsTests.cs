using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using Deadlock.Editor;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 38, EPIC SMG ADS (optical sight; v3, then the new design of 2026-11-08): the aim is the green dot measured on the image (not a front-sight tip),
    /// it lands on the screen centre on the iPhone, 16:9 and 4:3; the ADS emission core and glow share the ADS canvas
    /// and pivot, have their own settings (the hip's are untouched), breathe gently, follow the ADS image and switch with
    /// it; the dot stays visible with the glow off and on bright backgrounds; the other guns' ADS have no layers.
    /// </summary>
    public class Stage38EpicSmgAdsTests
    {
        const int W = 1774, H = 887;
        static readonly float[] Aspects = { 2622f / 1206f, 16f / 9f, 4f / 3f };

        static WeaponCatalogSetup.PlannedArt Plan => System.Array.Find(WeaponCatalogSetup.PlannedArts, p => p.WeaponId == EpicSmgSpec.WeaponId);

        static WeaponViewProfile Epic()
        {
            var view = WeaponCatalog.Load().Find(EpicSmgSpec.WeaponId)?.gun?.viewProfile;
            if (view == null || view.adsSprite == null || view.adsEmissionSprite == null)
                Assert.Inconclusive("the EPIC SMG's ADS image and its glow layers are not registered");
            return view;
        }

        /// <summary>The image's pixels, row 0 at the top (as the placement files count).</summary>
        static Color32[] Pixels(string path)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            Assert.AreEqual(W, tex.width, path);
            Assert.AreEqual(H, tex.height, path);
            var bottomUp = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            var c = new Color32[W * H];
            for (int y = 0; y < H; y++)
                System.Array.Copy(bottomUp, (H - 1 - y) * W, c, y * W, W);
            return c;
        }

        static bool Green(Color32 p) => p.a == 255 && p.g >= 96 && p.g >= p.r + 40 && p.g >= p.b + 40;

        /// <summary>The box (x0, y0, x1, y1; x1 / y1 excluded) of what is drawn within 32 px of the expected dot.</summary>
        static RectInt Drawn(Color32[] c, RectInt around)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = around.yMin; y < around.yMax; y++)
                for (int x = around.xMin; x < around.xMax; x++)
                    if (c[y * W + x].a > 0)
                    {
                        x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x + 1); y1 = Mathf.Max(y1, y + 1);
                    }
            return x1 < 0 ? default : new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        static readonly RectInt Dot = new RectInt(880, 432, 16, 12); // pixels x 880-895, y 432-443
        static readonly RectInt AroundDot = new RectInt(848, 400, 80, 76);

        [Test]
        public void Ads_AimIsTheGreenDotCentre_MeasuredOnTheImage_AndTheEmissionCoreSitsOnIt()
        {
            var view = Epic();
            var plan = Plan;
            var image = Pixels(plan.AdsPath);
            var core = Pixels(WeaponArtSetup.AdsEmissionPath(plan));
            Assert.AreEqual(Dot, Drawn(image, AroundDot), "the dot alone in the sight glass, x 880-895, y 432-443");
            int green = 0, holes = 0;
            for (int y = Dot.yMin; y < Dot.yMax; y++)
                for (int x = Dot.xMin; x < Dot.xMax; x++)
                {
                    holes += image[y * W + x].a == 255 ? 0 : 1;
                    green += Green(image[y * W + x]) ? 1 : 0;
                }
            Assert.AreEqual(0, holes, "the dot is whole");
            Assert.GreaterOrEqual(green * 2, Dot.width * Dot.height, "the dot is green");
            Vector2 centre = Dot.center;
            Assert.AreEqual(new Vector2(888f, 438f), centre);
            Assert.AreEqual(centre.x, view.adsSprite.pivot.x, 1e-3f, "ADS pivot = the dot centre");
            Assert.AreEqual(H - centre.y, view.adsSprite.pivot.y, 1e-3f, "ADS pivot = the dot centre");
            Assert.AreEqual(Dot, Drawn(core, AroundDot), "the emission core's dot is the image's");
            int cells = 0, off = 0, sameColour = 0;
            for (int cy = 0; cy < H; cy += 4)
                for (int cx = 0; cx < W; cx += 4)
                {
                    bool lit = false, same = true;
                    for (int y = cy; y < Mathf.Min(cy + 4, H); y++)
                        for (int x = cx; x < Mathf.Min(cx + 4, W); x++)
                        {
                            var e = core[y * W + x];
                            if (e.a == 0)
                                continue;
                            lit = true;
                            var b = image[y * W + x];
                            off += b.a == 255 ? 0 : 1;
                            same &= b.r == e.r && b.g == e.g && b.b == e.b;
                        }
                    cells += lit ? 1 : 0;
                    sameColour += lit && same ? 1 : 0;
                }
            Assert.AreEqual(253, cells, "the 253 cells of the emission core");
            Assert.AreEqual(0, off, "every core pixel on the gun");
            Assert.AreEqual(243, sameColour, "the amber cells are the image's own colours (only the 10 dot cells brighten)");
        }

        [Test]
        public void Ads_TheDotLandsOnTheScreenCentre_OnTheIphone_16x9_And4x3()
        {
            var view = Epic();
            var cam = new GameObject("TestViewCamera").AddComponent<Camera>();
            cam.fieldOfView = 50f;
            using var rig = new Rig(view, cam.transform);
            try
            {
                // The dot centre in the sprite's units (from its pixels, not from the pivot).
                Vector2 dot = new Vector2((Dot.center.x - view.adsSprite.pivot.x) / view.adsSprite.pixelsPerUnit,
                    ((H - Dot.center.y) - view.adsSprite.pivot.y) / view.adsSprite.pixelsPerUnit);
                foreach (float aspect in Aspects)
                {
                    cam.aspect = aspect;
                    rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 1f, Presence = 1f, Aspect = aspect, DeltaTime = 0.016f });
                    Vector3 v = cam.WorldToViewportPoint(rig.Ads.transform.TransformPoint(dot));
                    float halfPixel = 0.5f / 1206f;
                    Assert.AreEqual(0.5f, v.x, halfPixel / aspect, $"aspect {aspect:0.000}: the dot on the centre (x)");
                    Assert.AreEqual(0.5f, v.y, halfPixel, $"aspect {aspect:0.000}: the dot on the centre (y)");
                    Vector3 shot = cam.WorldToViewportPoint(cam.transform.position + cam.transform.forward * 10f);
                    Assert.AreEqual((Vector2)shot, (Vector2)v, $"aspect {aspect:0.000}: the dot on the aim the shots take (camera forward)");
                }
            }
            finally
            {
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        [Test]
        public void AdsLayers_ShareTheAdsCanvasAndPivot_OwnSettings_HipUntouched()
        {
            var view = Epic();
            var plan = Plan;
            Assert.AreEqual(WeaponArtSetup.AdsEmissionPath(plan), AssetDatabase.GetAssetPath(view.adsEmissionSprite));
            Assert.AreEqual(WeaponArtSetup.AdsGlowPath(plan), AssetDatabase.GetAssetPath(view.adsGlowSprite));
            foreach (var layer in new[] { view.adsEmissionSprite, view.adsGlowSprite })
            {
                Assert.AreEqual(view.adsSprite.pivot, layer.pivot, "same pivot (the dot)");
                Assert.AreEqual(view.adsSprite.pixelsPerUnit, layer.pixelsPerUnit, "same size on screen");
                Assert.AreEqual(view.adsSprite.rect.size, layer.rect.size, "same canvas");
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(layer));
                Assert.AreEqual(FilterMode.Point, importer.filterMode);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            }
            Assert.IsNotNull(view.adsGlowMaterial, "an additive ADS glow material");
            Assert.AreEqual(PixelWeaponArt.AdsGlowMaterialPath, AssetDatabase.GetAssetPath(view.adsGlowMaterial));
            Assert.AreEqual("Deadlock/Sprite Additive Glow", view.adsGlowMaterial.shader.name);
            Assert.AreNotSame(view.glowMaterial, view.adsGlowMaterial, "its own material, apart from the hip's");
            Assert.AreEqual(1f, view.adsGlowMaterial.GetFloat("_Gain"), 1e-4f);
            Assert.AreEqual(0.2f, view.adsGlowIntensity, 1e-4f);
            Assert.AreEqual(1f, view.adsEmissionIntensity, 1e-4f);
            Assert.AreEqual(0.3f, view.adsGlowPulse, 1e-4f);
            Assert.AreEqual(0.15f, view.adsEmissionPulse, 1e-4f);
            Assert.AreEqual(2.4f, view.adsGlowPulseSeconds, 1e-4f);
            // The hip glow as it was (Stage 38 glow breathing).
            Assert.AreEqual(14f, view.glowMaterial.GetFloat("_Gain"), 1e-4f);
            Assert.AreEqual(0.9f, view.glowPulse, 1e-4f);
            Assert.AreEqual(0.7f, view.emissionPulse, 1e-4f);
            Assert.AreEqual(2.4f, view.glowPulseSeconds, 1e-4f);
            StringAssert.EndsWith("epic_smg_hip_emission.png", AssetDatabase.GetAssetPath(view.hipEmissionSprite));
            StringAssert.EndsWith("epic_smg_hip_glow.png", AssetDatabase.GetAssetPath(view.hipGlowSprite));
        }

        [Test]
        public void AdsLayers_BreatheGently_FollowTheAdsImage_AndSwitchWithIt()
        {
            var view = Epic();
            using var rig = new Rig(view, null);
            var p = rig.Presenter;
            bool glowEnabled = WeaponViewPresenter.GlowEnabled;
            float period = view.adsGlowPulseSeconds;
            void At(float ads, float time, bool reload = false, float presence = 1f) => p.Update(new WeaponViewPresenter.Input
                { Ads = ads, Presence = presence, Aspect = 2622f / 1206f, Reloading = reload, ReloadProgress = reload ? 0.5f : 0f, DeltaTime = 0.016f, Time = time });
            try
            {
                Assert.AreSame(view.adsGlowMaterial, p.AdsGlow.sharedMaterial, "the ADS glow is added through its own material");
                Assert.AreNotSame(view.adsGlowMaterial, p.AdsEmission.sharedMaterial, "the core is drawn with sharp pixels");
                At(1f, 0f);
                Assert.IsTrue(p.AdsGlow.enabled && p.AdsEmission.enabled);
                Assert.AreEqual(0.2f, p.AdsGlow.color.a, 1e-4f, "glow at the top: intensity 0.20");
                Assert.AreEqual(1f, p.AdsEmission.color.a, 1e-4f, "core at the top: full");
                At(1f, period / 2f);
                Assert.AreEqual(0.2f * 0.7f, p.AdsGlow.color.a, 1e-4f, "glow at the bottom: 30 percent less");
                Assert.AreEqual(0.85f, p.AdsEmission.color.a, 1e-4f, "core at the bottom: 15 percent less (the dot stays bright)");
                At(1f, period);
                Assert.AreEqual(0.2f, p.AdsGlow.color.a, 1e-4f, "one breath per 2.4 s");
                float last = -1f;
                for (float t = 0f; t <= period; t += 1f / 60f)
                {
                    At(1f, t);
                    if (last >= 0f)
                        Assert.Less(Mathf.Abs(p.AdsGlow.color.a - last), 0.005f, "smooth, no steps");
                    last = p.AdsGlow.color.a;
                }
                // Kicked and mid-reload: the layers stay on the ADS image's pivot, as its children.
                p.Fire(0f);
                At(1f, 0f, reload: true);
                foreach (var layer in new[] { p.AdsEmission, p.AdsGlow })
                {
                    Assert.AreSame(rig.Ads.transform, layer.transform.parent, $"{layer.name}: a child of the ADS sprite");
                    Assert.AreEqual(Vector2.zero, (Vector2)layer.transform.localPosition);
                    Assert.AreEqual(Quaternion.identity, layer.transform.localRotation);
                    Assert.AreEqual(Vector3.one, layer.transform.localScale);
                    Assert.Greater(layer.sortingOrder, rig.Ads.sortingOrder, "over the gun");
                    Assert.AreEqual(rig.Ads.gameObject.layer, layer.gameObject.layer);
                }
                Assert.Less(p.AdsFlash.sortingOrder, rig.Ads.sortingOrder, "the ADS flash stays behind the gun");
                // Hip: the ADS layers are off and the hip's are on; through the cross-fade they follow the ADS image's share.
                At(0f, 0f);
                Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled, "hip: no ADS layer");
                Assert.IsTrue(p.Glow.enabled && p.Emission.enabled, "hip: its own layers");
                At(0.55f, 0f);
                Assert.That(p.AdsAlpha, Is.InRange(0.01f, 0.99f));
                Assert.AreEqual(p.AdsAlpha * 0.2f, p.AdsGlow.color.a, 1e-4f, "the ADS glow fades in with the ADS image");
                Assert.AreEqual(p.AdsAlpha, p.AdsEmission.color.a, 1e-4f);
                At(1f, 0f);
                Assert.IsFalse(p.Glow.enabled || p.Emission.enabled, "ADS: no hip layer");
                // Put away (switch): nothing is drawn.
                At(1f, 0f, presence: 0f);
                Assert.IsFalse(p.AdsGlow.enabled || p.AdsEmission.enabled || rig.Ads.enabled);
                // Glow switched off: the core and the image's dot stay; brightness 0: the plain image with its dot.
                WeaponViewPresenter.GlowEnabled = false;
                At(1f, 0f);
                Assert.IsFalse(p.AdsGlow.enabled, "glow off");
                Assert.IsTrue(p.AdsEmission.enabled && rig.Ads.enabled);
                view.adsEmissionIntensity = 0f;
                At(1f, 0f);
                Assert.IsFalse(p.AdsEmission.enabled, "no glow at all");
                Assert.IsTrue(rig.Ads.enabled);
                Assert.AreSame(view.adsSprite, rig.Ads.sprite, "the image keeps its own green dot");
            }
            finally
            {
                WeaponViewPresenter.GlowEnabled = glowEnabled;
                view.adsEmissionIntensity = 1f;
            }
        }

        static float Linear(byte c)
        {
            float s = c / 255f;
            return s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);
        }

        [Test]
        public void AdsGlow_AtItsBrightest_ClipsNoPixelOfTheSightGlass_AndTheDotStaysDistinct()
        {
            var view = Epic();
            var plan = Plan;
            var image = Pixels(plan.AdsPath);
            var glow = Pixels(WeaponArtSetup.AdsGlowPath(plan));
            float strength = view.adsGlowMaterial.GetFloat("_Gain") * view.adsGlowIntensity; // the top of the breath
            var glassPixels = Glass(image, out _);
            // Dark, bright and near-white backgrounds behind the sight glass (sRGB).
            foreach (var bg in new[] { new Color32(18, 20, 26, 255), new Color32(199, 189, 168, 255), new Color32(222, 226, 231, 255) })
            {
                int clipped = 0;
                foreach (int i in glassPixels)
                {
                    var g = glow[i];
                    float a = g.a / 255f * strength;
                    if (Linear(bg.r) + Linear(g.r) * a > 1f || Linear(bg.g) + Linear(g.g) * a > 1f || Linear(bg.b) + Linear(g.b) * a > 1f)
                        clipped++;
                }
                // The new design's thicker octagonal frame (2026-11-08) leaves a 152 x 128 window (v3's was 176 x 148).
                Assert.Greater(glassPixels.Count, 18000, "the sight glass is clear");
                Assert.AreEqual(0, clipped, $"background {bg}: no pixel of the sight glass turns white");
            }
            // The dot keeps a dark green rim and a bright centre (seen on dark and bright ground, glow on or off).
            int rim = 0, bright = 0;
            for (int y = Dot.yMin; y < Dot.yMax; y++)
                for (int x = Dot.xMin; x < Dot.xMax; x++)
                {
                    var c = image[y * W + x];
                    rim += c.g < 140 && Green(c) ? 1 : 0;
                    bright += c.g > 220 ? 1 : 0;
                }
            Assert.Greater(rim, 0, "a dark green rim (visible on bright ground)");
            Assert.Greater(bright, 0, "a bright centre (visible on dark ground)");
        }

        /// <summary>
        /// The sight glass: the transparent pixels reached from beside the dot without crossing the frame, inside a box
        /// well around the sight (it never reaches the box's edge when the frame is closed). Indices into the pixels.
        /// </summary>
        static List<int> Glass(Color32[] c, out RectInt box)
        {
            var bounds = new RectInt(760, 340, 260, 200);
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(438 * W + 860);
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % W, y = i / W;
                if (!bounds.Contains(new Vector2Int(x, y)) || c[i].a != 0 || !seen.Add(i))
                    continue;
                x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x + 1); y1 = Mathf.Max(y1, y + 1);
                queue.Enqueue(i + 1); queue.Enqueue(i - 1); queue.Enqueue(i + W); queue.Enqueue(i - W);
            }
            box = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return new List<int>(seen);
        }

        /// <summary>
        /// The ADS image of the new design (2026-11-08, the recreated design source on the 4 x 4 grid): every 4 x 4 cell one
        /// colour, alpha 0 or 255; the window of the octagonal frame is closed and clear but for the dot; the gun and the
        /// sleeves run off the bottom edge without a gap, the other edges are clear, and everything sits in the 4:3 view.
        /// </summary>
        [Test]
        public void Ads_NewDesign_OnTheGrid_ClearWindow_RunsOffTheBottomWithoutAGap()
        {
            Epic();
            var image = Pixels(Plan.AdsPath);
            int notUniform = 0, semi = 0;
            for (int cy = 0; cy < H; cy += 4)
                for (int cx = 0; cx < W; cx += 4)
                {
                    var corner = image[cy * W + cx];
                    bool same = true;
                    for (int y = cy; y < Mathf.Min(cy + 4, H); y++)
                        for (int x = cx; x < Mathf.Min(cx + 4, W); x++)
                        {
                            var p = image[y * W + x];
                            same &= p.r == corner.r && p.g == corner.g && p.b == corner.b && p.a == corner.a;
                            semi += p.a != 0 && p.a != 255 ? 1 : 0;
                        }
                    notUniform += same ? 0 : 1;
                }
            Assert.AreEqual(0, notUniform, "every 4 x 4 cell is one colour");
            Assert.AreEqual(0, semi, "alpha 0 or 255 only");
            var glass = Glass(image, out var box);
            Assert.AreEqual(new RectInt(812, 376, 152, 128), box, "the octagonal frame's window, closed (x 812-963, y 376-503)");
            int drawnInWindow = 0;
            for (int y = box.yMin; y < box.yMax; y++)
                for (int x = box.xMin; x < box.xMax; x++)
                    drawnInWindow += image[y * W + x].a != 0 && !Dot.Contains(new Vector2Int(x, y)) ? 1 : 0;
            // Inside the window's box only the dot and the frame's cut corners are drawn; the glass itself is clear.
            Assert.Greater(glass.Count, 18000);
            Assert.AreEqual(box.width * box.height - glass.Count - Dot.width * Dot.height, drawnInWindow, "only the dot in the glass");
            int first = -1, last = -1, gaps = 0;
            for (int x = 0; x < W; x++)
                if (image[(H - 1) * W + x].a != 0)
                {
                    if (first < 0)
                        first = x;
                    last = x;
                }
            for (int x = first; x <= last; x++)
                gaps += image[(H - 1) * W + x].a == 0 ? 1 : 0;
            Assert.Greater(last - first, 800, "the gun and the sleeves reach the bottom edge");
            Assert.AreEqual(0, gaps, "no gap along the bottom edge");
            for (int x = 0; x < W; x++)
                Assert.AreEqual(0, image[x].a, "the top edge is clear");
            for (int y = 0; y < H; y++)
            {
                Assert.AreEqual(0, image[y * W].a, "the left edge is clear");
                Assert.AreEqual(0, image[y * W + W - 1].a, "the right edge is clear");
                for (int x = 0; x < W; x++)
                    if (x < 372 || x > 1402)
                        Assert.AreEqual(0, image[y * W + x].a, "everything in the 4:3 view (x 372-1402)");
            }
        }

        [Test]
        public void OtherGuns_AdsHasNoLayers_AndKeepsItsFrontSight()
        {
            foreach (var id in new[] { "proto_rifle", "proto_smg", "proto_shotgun", "proto_handgun", "proto_lmg" })
            {
                var view = WeaponCatalog.Load().Find(id)?.gun?.viewProfile;
                if (view == null || view.adsSprite == null)
                    continue;
                Assert.IsNull(view.adsEmissionSprite, $"{id}: no ADS emission");
                Assert.IsNull(view.adsGlowSprite, $"{id}: no ADS glow");
                Assert.IsNull(view.adsGlowMaterial, $"{id}: no ADS glow material");
                using var rig = new Rig(view, null);
                rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 1f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f, Time = 0f });
                Assert.IsTrue(rig.Ads.enabled, $"{id}: its ADS image");
                Assert.IsFalse(rig.Presenter.AdsGlow.enabled || rig.Presenter.AdsEmission.enabled, $"{id}: nothing over it");
                Assert.That(((Vector2)rig.Ads.transform.localPosition).magnitude, Is.LessThan(1e-5f), $"{id}: its aim point on the centre");
            }
        }

        sealed class Rig : System.IDisposable
        {
            public readonly GameObject Root = new GameObject("AdsRig");
            public readonly SpriteRenderer Hip, Ads, HipFlash, AdsFlash;
            public readonly WeaponViewPresenter Presenter;

            public Rig(WeaponViewProfile profile, Transform camera)
            {
                if (camera != null)
                    Root.transform.SetParent(camera, false);
                Hip = Make("Hip", Root.transform);
                HipFlash = Make("MuzzleFlash", Hip.transform);
                Ads = Make("Ads", Root.transform);
                AdsFlash = Make("MuzzleFlash", Ads.transform);
                Presenter = new WeaponViewPresenter(Hip, Ads, HipFlash, AdsFlash);
                Presenter.SetProfile(profile);
            }

            static SpriteRenderer Make(string name, Transform parent)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                return go.AddComponent<SpriteRenderer>();
            }

            public void Dispose() => Object.DestroyImmediate(Root);
        }
    }
}
