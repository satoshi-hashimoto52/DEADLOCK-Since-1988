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
    /// Stage 38: the new guns' registered art (handgun, shotgun, LMG, EPIC SMG) as the game imports it — each view from
    /// its own registered image only (a view not delivered draws nothing, never another gun's image), crisp import, the
    /// pivots of the hand-written placements, the EPIC SMG's emission core and glow on the hip canvas, and the shared pose
    /// (<see cref="WeaponViewPresenter"/>) giving the offline view model's numbers.
    /// </summary>
    public class Stage38WeaponArtTests
    {
        static IEnumerable<WeaponCatalogSetup.PlannedArt> Plans => WeaponCatalogSetup.PlannedArts;

        static WeaponViewProfile ProfileOf(WeaponCatalogSetup.PlannedArt plan) =>
            AssetDatabase.LoadAssetAtPath<WeaponDefinition>(plan.DefinitionPath).viewProfile;

        static float[] Read(string placementPath, string field)
        {
            var json = File.ReadAllText(placementPath);
            int at = json.IndexOf($"\"{field}\"", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, $"{placementPath}: {field}");
            int open = json.IndexOf('[', at), close = json.IndexOf(']', open);
            return System.Array.ConvertAll(json.Substring(open + 1, close - open - 1).Split(','), s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture));
        }

        static void AssertCrisp(Sprite sprite, string label)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
            Assert.AreEqual(FilterMode.Point, importer.filterMode, label);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, label);
            Assert.IsFalse(importer.mipmapEnabled, label);
            Assert.GreaterOrEqual(importer.maxTextureSize, 2048, $"{label}: not downscaled (1774 px wide)");
            Assert.AreEqual(1774, sprite.rect.width, label);
            Assert.AreEqual(887, sprite.rect.height, label);
        }

        [Test]
        public void EachView_ShowsOnlyItsOwnRegisteredImage()
        {
            int imported = 0;
            foreach (var plan in Plans)
            {
                var view = ProfileOf(plan);
                bool hip = File.Exists(plan.HipPath), ads = File.Exists(plan.AdsPath);
                if (!hip && !ads)
                {
                    Assert.IsNull(view, $"{plan.WeaponId}: no image registered, no first-person art");
                    continue;
                }
                Assert.IsNotNull(view, $"{plan.WeaponId}: registered images are imported");
                imported++;
                if (hip)
                {
                    AssertCrisp(view.hipSprite, $"{plan.WeaponId} hip");
                    string shown = AssetDatabase.GetAssetPath(view.hipSprite);
                    Assert.IsTrue(shown == plan.HipPath || shown == WeaponArtSetup.GridPath(plan.HipPath), $"{plan.WeaponId} hip: {shown}");
                    float[] muzzle = Read(WeaponArtSetup.PlacementPath(plan.HipPath), "muzzle");
                    Assert.AreEqual(muzzle[0], view.hipSprite.pivot.x, 0.5f, $"{plan.WeaponId}: hip pivot = muzzle");
                    Assert.AreEqual(887f - muzzle[1], view.hipSprite.pivot.y, 0.5f, $"{plan.WeaponId}: hip pivot = muzzle");
                }
                else
                    Assert.IsNull(view.hipSprite, $"{plan.WeaponId}: the hip image is not delivered: nothing (never the rifle's baked one)");
                Assert.AreEqual(hip ? WeaponArtState.Approved : WeaponArtState.Placeholder,
                    WeaponArtStatus.Of(AssetDatabase.LoadAssetAtPath<WeaponDefinition>(plan.DefinitionPath), null, WeaponArtView.Hip));
                if (ads)
                {
                    AssertCrisp(view.adsSprite, $"{plan.WeaponId} ads");
                    float[] aim = Read(WeaponArtSetup.PlacementPath(plan.AdsPath), "aim");
                    Assert.AreEqual(aim[0], view.adsSprite.pivot.x, 0.5f, $"{plan.WeaponId}: ADS pivot = aim point (front-sight tip or optical dot) = screen centre");
                    Assert.AreEqual(887f - aim[1], view.adsSprite.pivot.y, 0.5f, $"{plan.WeaponId}: ADS pivot = aim point");
                    Assert.AreEqual(0f, view.adsMuzzleOffset.x, 0.001f, "flash on the centre line");
                    Assert.Less(view.adsMuzzleOffset.y, 0f, "flash below the sight tip");
                }
                else
                    Assert.IsNull(view.adsSprite, $"{plan.WeaponId}: no ADS image");
                foreach (var s in new[] { view.hipSprite, view.adsSprite, view.hipEmissionSprite, view.hipGlowSprite, view.adsEmissionSprite, view.adsGlowSprite })
                    if (s != null)
                        StringAssert.StartsWith($"Assets/Game/Art/Weapons/{Path.GetFileName(Path.GetDirectoryName(plan.HipPath))}/",
                            AssetDatabase.GetAssetPath(s), $"{plan.WeaponId}: its own images only");
                // The rifle's motion values are shared; no combat value comes from the art.
                var baked = AssetDatabase.LoadAssetAtPath<WeaponViewProfile>("Assets/Game/Data/WeaponView_ProtoRifle.asset");
                Assert.AreEqual(baked.kickOffset, view.kickOffset);
                Assert.AreEqual(baked.reloadRollDegrees, view.reloadRollDegrees);
            }
            Assert.Greater(imported, 0, "at least one new gun has registered first-person art");
        }

        [Test]
        public void EpicSmg_GlowLayers_ShareTheHipCanvasAndPivot()
        {
            var plan = System.Array.Find(WeaponCatalogSetup.PlannedArts, p => p.WeaponId == EpicSmgSpec.WeaponId);
            Assert.AreEqual("weapon.epic_smg.hip", plan.HipAssetId);
            StringAssert.Contains("/EpicSMG/", plan.HipPath);
            var view = ProfileOf(plan);
            if (!File.Exists(WeaponArtSetup.EmissionPath(plan)))
                Assert.Inconclusive("the EPIC SMG's emission image is not registered");
            foreach (var layer in new[] { view.hipEmissionSprite, view.hipGlowSprite })
            {
                Assert.IsNotNull(layer);
                AssertCrisp(layer, layer.name);
                Assert.AreEqual(view.hipSprite.pivot, layer.pivot, "same pivot (muzzle)");
                Assert.AreEqual(view.hipSprite.pixelsPerUnit, layer.pixelsPerUnit, "same size on screen");
                Assert.AreEqual(view.hipSprite.rect.size, layer.rect.size, "same canvas");
            }
            // ADS has glow images of its own (v3 optical sight, Stage38EpicSmgAdsTests): the hip layers are not reused there.
            Assert.AreNotSame(view.hipEmissionSprite, view.adsEmissionSprite);
            Assert.AreNotSame(view.hipGlowSprite, view.adsGlowSprite);
        }

        [Test]
        public void ShotgunAds_TheGameShowsTheGridRestoredDerivative_OriginalKept()
        {
            var plan = System.Array.Find(WeaponCatalogSetup.PlannedArts, p => p.WeaponId == "proto_shotgun");
            string grid = WeaponArtSetup.GridPath(plan.AdsPath);
            if (!File.Exists(plan.AdsPath))
            {
                // Not approved (2026-11-05): nothing is registered, so the game shows no ADS image for it (a stand-in),
                // and no derivative is made or used.
                Assert.IsFalse(File.Exists(grid), "no derivative of an unregistered image");
                var gun = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(plan.DefinitionPath);
                Assert.IsTrue(gun.viewProfile == null || gun.viewProfile.adsSprite == null, "no ADS image");
                Assert.AreEqual(WeaponArtState.Placeholder, WeaponArtStatus.Of(gun, null, WeaponArtView.Ads));
                return;
            }
            Assert.IsTrue(File.Exists(grid), "the derivative is next to the original");
            var art = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(WeaponArtSetup.DefinitionPath(plan));
            Assert.AreEqual(plan.AdsPath, AssetDatabase.GetAssetPath(art.ads.source), "source = the registered original");
            Assert.AreEqual(grid, AssetDatabase.GetAssetPath(art.ads.image), "image = the derivative");
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(grid)));
            Assert.AreEqual(1774, tex.width);
            Assert.AreEqual(887, tex.height);
            var px = tex.GetPixels32();
            int semi = 0, offGrid = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a != 0 && px[i].a != 255)
                    semi++;
                int x = i % tex.width, y = tex.height - 1 - i / tex.width; // from the top-left
                int cx = x - x % 4, cy = y - y % 4;
                var first = px[(tex.height - 1 - cy) * tex.width + cx];
                if (!px[i].Equals(first) && !(px[i].a == 0 && first.a == 0))
                    offGrid++;
            }
            Object.DestroyImmediate(tex);
            Assert.AreEqual(0, semi, "alpha 0 / 255 only");
            Assert.AreEqual(0, offGrid, "every 4 x 4 cell is one colour");
        }

        [Test]
        public void Import_RefusesAHipLayerOnAnotherCanvas_AndAHipMarkedNotDelivered()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>("Assets/Game/Data/WeaponArt/WeaponArt_ProtoRifle.asset");
            var bad = ScriptableObject.CreateInstance<WeaponArtDefinition>();
            try
            {
                bad.weapon = rifle.weapon;
                bad.baseProfile = rifle.baseProfile;
                bad.hip = rifle.hip;
                bad.ads = rifle.ads;
                bad.hipEmission = rifle.ads.image; // 1774 x 887, but the rifle hip is 1672 x 941
                var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { bad }, "test"));
                StringAssert.Contains("hipEmission", e.Message);
                bad.hipEmission = null;
                bad.hipNotDelivered = true;
                e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { bad }, "test"));
                StringAssert.Contains("hipNotDelivered", e.Message);
            }
            finally
            {
                Object.DestroyImmediate(bad);
            }
        }

        // ---- shared pose ---------------------------------------------------------------------------------------------

        sealed class Rig : System.IDisposable
        {
            public readonly GameObject Root = new GameObject("PoseRig");
            public readonly SpriteRenderer Hip, Ads, HipFlash, AdsFlash;
            public readonly WeaponViewPresenter Presenter;

            public Rig(WeaponViewProfile profile)
            {
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

        /// <summary>The offline view model's hip place before Stage 38 (written out again here as the reference).</summary>
        static Vector3 OldHipPlace(WeaponViewProfile p, float t, float aspect, float reloadCurve, float presence, out Quaternion rotation)
        {
            float half = Mathf.Tan(25f * Mathf.Deg2Rad);
            Vector2 holster = new Vector2(0.05f, -0.45f) * (1f - presence * presence * (3f - 2f * presence));
            float follow = aspect < p.referenceAspect ? p.hipRightEdgeFollowNarrow : p.hipRightEdgeFollow;
            float edgeShift = (aspect - p.referenceAspect) * half * follow;
            float hipScale = Mathf.Lerp(1f, p.hipScaleAt4x3, Mathf.InverseLerp(p.referenceAspect, 4f / 3f, aspect));
            Vector2 reloadOffset = new Vector2(0.02f, -p.reloadDrop) * reloadCurve;
            rotation = Quaternion.Euler(0f, 0f, p.reloadRollDegrees * reloadCurve);
            Vector2 shared = reloadOffset + holster;
            Vector2 hipRest = p.hipMuzzlePosition + new Vector2(edgeShift - 0.10f * half, -(0.09f + 0.05f * Mathf.InverseLerp(4f / 3f, 2622f / 1206f, aspect)) * 2f * half);
            Vector2 hipTarget = p.hipToAdsRelative ? hipRest + p.hipToAdsMuzzlePosition : p.hipToAdsMuzzlePosition;
            Vector2 pivot = Vector2.Lerp(hipRest, hipTarget, t) + shared;
            Vector2 hold = p.hipHoldOffset * hipScale;
            Vector2 pos = pivot + hold - (Vector2)(rotation * hold);
            return new Vector3(pos.x, pos.y, 1f);
        }

        [Test]
        public void SharedPose_GivesTheOfflineNumbers_OnEveryAspect()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset").viewProfile;
            using var rig = new Rig(rifle);
            foreach (float aspect in new[] { 16f / 9f, 2622f / 1206f, 4f / 3f })
                foreach (float t in new[] { 0f, 0.4f, 1f })
                    foreach (float reload in new[] { 0f, 0.5f })
                        foreach (float presence in new[] { 1f, 0.5f })
                        {
                            rig.Presenter.Update(new WeaponViewPresenter.Input
                            {
                                Ads = t, Presence = presence, Aspect = aspect, Reloading = reload > 0f, ReloadProgress = reload, DeltaTime = 0.016f, Time = 0f,
                            });
                            Vector3 want = OldHipPlace(rifle, t, aspect, reload > 0f ? Mathf.Sin(reload * Mathf.PI) : 0f, presence, out var rot);
                            string at = $"aspect {aspect:0.000} ads {t} reload {reload} presence {presence}";
                            Assert.That(Vector3.Distance(want, rig.Hip.transform.localPosition), Is.LessThan(1e-5f), at);
                            Assert.That(Quaternion.Angle(rot, rig.Hip.transform.localRotation), Is.LessThan(1e-3f), at);
                            if (t >= 1f && reload == 0f && presence >= 1f)
                                Assert.That(((Vector2)rig.Ads.transform.localPosition).magnitude, Is.LessThan(1e-5f), $"{at}: the front-sight tip on the centre");
                        }
        }

        [Test]
        public void GlowLayers_FollowTheHipImage_AndTheGunIsWholeWithoutThem()
        {
            var plan = System.Array.Find(WeaponCatalogSetup.PlannedArts, p => p.WeaponId == EpicSmgSpec.WeaponId);
            var view = ProfileOf(plan);
            if (view == null || view.hipEmissionSprite == null)
                Assert.Inconclusive("the EPIC SMG's emission image is not registered");
            using var rig = new Rig(view);
            try
            {
                rig.Presenter.Fire(0f);
                rig.Presenter.Update(new WeaponViewPresenter.Input
                {
                    Ads = 0.3f, Presence = 0.8f, Aspect = 2622f / 1206f, Reloading = true, ReloadProgress = 0.4f, DeltaTime = 0.016f, Time = 0f,
                });
                foreach (var layer in new[] { rig.Presenter.Emission, rig.Presenter.Glow })
                {
                    Assert.IsTrue(layer.enabled, layer.name);
                    Assert.AreSame(rig.Hip.transform, layer.transform.parent, "a child of the hip sprite");
                    Assert.AreEqual(Vector2.zero, (Vector2)layer.transform.localPosition, "on the hip sprite's pivot");
                    Assert.AreEqual(Quaternion.identity, layer.transform.localRotation);
                    Assert.AreEqual(Vector3.one, layer.transform.localScale);
                    Assert.Greater(layer.sortingOrder, rig.Hip.sortingOrder, "drawn over the gun");
                }
                WeaponViewPresenter.GlowEnabled = false;
                rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f });
                Assert.IsFalse(rig.Presenter.Glow.enabled, "glow off");
                Assert.IsTrue(rig.Presenter.Emission.enabled, "the core stays");
                Assert.IsTrue(rig.Hip.enabled);
                Assert.AreSame(view.hipSprite, rig.Hip.sprite, "the plain gun is drawn as always");
                view.emissionIntensity = 0f;
                rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f });
                Assert.IsFalse(rig.Presenter.Emission.enabled, "brightness 0: the plain gun only");
            }
            finally
            {
                WeaponViewPresenter.GlowEnabled = true;
                view.emissionIntensity = 1f;
            }
        }

        /// <summary>
        /// The barrel's angle in each hip image (degrees above the left horizontal, measured on the top edge of the barrel
        /// beyond the sights, 2026-11-06). The pixels are never changed, so neither are these.
        /// </summary>
        static readonly Dictionary<string, float> DrawnBarrel = new Dictionary<string, float>
        {
            { "proto_rifle", 31f }, { "proto_smg", 25.5f }, { "proto_shotgun", 14.5f }, { "proto_lmg", 13f }, { EpicSmgSpec.WeaponId, 8f },
        };

        static readonly float[] Aspects = { 16f / 9f, 2622f / 1206f, 4f / 3f };

        static WeaponViewProfile HipProfile(string id)
        {
            var view = WeaponCatalog.Load().Find(id)?.gun?.viewProfile;
            if (view == null || view.hipSprite == null)
                Assert.Inconclusive($"{id}: no hip image registered");
            return view;
        }

        /// <summary>The hip image's opaque pixels on its border (where arms and stock are cut off), in the sprite's units.</summary>
        static List<Vector2> CutEdge(Sprite sprite)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite.texture)));
            var c = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            float sx = sprite.rect.width / w, sy = sprite.rect.height / h;
            var edge = new List<Vector2>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x == 0 || y == 0 || x == w - 1 || y == h - 1) && c[y * w + x].a >= 128)
                        edge.Add(new Vector2((x * sx - sprite.pivot.x) / sprite.pixelsPerUnit, (y * sy - sprite.pivot.y) / sprite.pixelsPerUnit));
            Object.DestroyImmediate(tex);
            return edge;
        }

        [Test]
        public void HipBarrel_PointsAtTheCrosshair_OnTheIphone_AsTheShotsLeave()
        {
            foreach (var pair in DrawnBarrel)
            {
                var view = HipProfile(pair.Key);
                using var rig = new Rig(view);
                rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 2622f / 1206f, DeltaTime = 0f, Time = 0f });
                Vector2 muzzle = rig.Hip.transform.localPosition; // the hip pivot is the muzzle; the screen centre is (0, 0)
                float line = Mathf.Atan2(-muzzle.y, muzzle.x) * Mathf.Rad2Deg;   // the shot line's angle above the left horizontal
                float barrel = pair.Value - view.hipRollDegrees;                  // the drawn barrel's angle on screen (turned with the image)
                Assert.AreEqual(view.hipRollDegrees, rig.Hip.transform.localEulerAngles.z > 180f ? rig.Hip.transform.localEulerAngles.z - 360f : rig.Hip.transform.localEulerAngles.z,
                    0.01f, $"{pair.Key}: the image is turned by its roll at rest");
                Assert.That(Mathf.Abs(line - barrel), Is.LessThanOrEqualTo(5f), $"{pair.Key}: line {line:0.0}°, barrel {barrel:0.0}°");
                Assert.That(line, Is.InRange(25f, 35f), $"{pair.Key}: the shots leave the lower right as the rifle's");
            }
            Assert.AreEqual(0f, HipProfile("proto_rifle").hipRollDegrees, "the rifle keeps its drawn angle");
            Assert.AreEqual(0f, HipProfile("proto_smg").hipRollDegrees, "the SMG keeps its drawn angle");
        }

        [Test]
        public void HipImage_NoCutEdgeOnScreen_AtRestReloadingAndKicked_OnEveryAspect()
        {
            float half = Mathf.Tan(25f * Mathf.Deg2Rad);
            foreach (var id in DrawnBarrel.Keys)
            {
                var view = HipProfile(id);
                var edge = CutEdge(view.hipSprite);
                using var rig = new Rig(view);
                foreach (float aspect in Aspects)
                    foreach (string pose in new[] { "rest", "reload", "kick" })
                    {
                        if (pose == "kick")
                            rig.Presenter.Fire(0f);
                        rig.Presenter.Update(new WeaponViewPresenter.Input
                        {
                            Ads = 0f, Presence = 1f, Aspect = aspect, Reloading = pose == "reload", ReloadProgress = 0.5f, DeltaTime = 0f, Time = 0f,
                        });
                        int shown = 0;
                        foreach (var e in edge)
                        {
                            Vector3 q = rig.Hip.transform.TransformPoint(e); // view space, on the plane z = 1
                            if (Mathf.Abs(q.x) < half * aspect && Mathf.Abs(q.y) < half)
                                shown++;
                        }
                        Assert.AreEqual(0, shown, $"{id} {pose} at aspect {aspect:0.00}: cut-edge pixels on screen");
                        rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = aspect, DeltaTime = 1f, Time = 5f }); // the kick has gone
                    }
            }
        }

        [Test]
        public void TurnedHip_FlashAndGlowLayersTurnWithTheGun()
        {
            var view = HipProfile(EpicSmgSpec.WeaponId);
            Assert.That(view.hipRollDegrees, Is.LessThan(-5f), "the EPIC SMG's level barrel is turned up toward the crosshair");
            using var rig = new Rig(view);
            rig.Presenter.Fire(0f);
            rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 2622f / 1206f, DeltaTime = 0f, Time = 0f });
            Assert.IsTrue(rig.HipFlash.enabled);
            Assert.AreSame(rig.Hip.transform, rig.HipFlash.transform.parent, "the flash is the hip image's child");
            Assert.AreEqual((Vector3)view.hipMuzzleFlashOffset + new Vector3(0f, 0f, 0.01f), rig.HipFlash.transform.localPosition, "just beyond the bore in the image's own axes");
            Assert.Less(rig.HipFlash.sortingOrder, rig.Hip.sortingOrder, "still behind the gun");
            Vector3 flash = rig.HipFlash.transform.position - rig.Hip.transform.position;
            Assert.Less(flash.x, 0f, "beyond the muzzle, away from the gun");
            Assert.Greater(flash.y, 0f, "the barrel now points up: so does the flash's place");
            foreach (var layer in new[] { rig.Presenter.Emission, rig.Presenter.Glow })
                Assert.AreEqual(Quaternion.identity, layer.transform.localRotation, $"{layer.name}: turned with the hip image, no own turn");
        }

        [Test]
        public void LevelBarrelFlash_IsAJetOutOfTheBore_AlongTheDrawnBarrel_OthersKeepTheirStar_AllBehindTheGun()
        {
            foreach (var id in new[] { "proto_shotgun", "proto_lmg", EpicSmgSpec.WeaponId })
            {
                var view = HipProfile(id);
                Assert.IsTrue(view.hipMuzzleFlashAlongBarrel, $"{id}: a jet along the barrel");
                Assert.AreEqual(180f - DrawnBarrel[id], view.hipMuzzleFlashBarrelDegrees, 0.01f, $"{id}: along the measured barrel");
                using var rig = new Rig(view);
                float ppu = view.hipSprite.pixelsPerUnit; // image pixels per unit
                Vector2 dir = Quaternion.Euler(0f, 0f, view.hipMuzzleFlashBarrelDegrees) * Vector2.right; // out of the muzzle, sprite units
                Vector2 centre = view.hipMuzzleFlashOffset;                                               // from the muzzle pivot
                float ahead = Vector2.Dot(centre, dir) * ppu;
                float side = Mathf.Abs(centre.x * dir.y - centre.y * dir.x) * ppu;
                Assert.That(ahead, Is.InRange(5f, 25f), $"{id}: the jet's centre just beyond the muzzle ({ahead:0.0} px)");
                Assert.That(side, Is.LessThan(5f), $"{id}: on the barrel's line, not above it ({side:0.0} px off)");
                for (int shot = 0; shot < 20; shot++)
                {
                    rig.Presenter.Fire(0f);
                    rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 2622f / 1206f, DeltaTime = 0f, Time = 0f });
                    var f = rig.HipFlash.transform;
                    Assert.IsTrue(rig.HipFlash.enabled, id);
                    Assert.Less(rig.HipFlash.sortingOrder, rig.Hip.sortingOrder, $"{id}: behind the gun (its root hidden in the muzzle)");
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(f.localEulerAngles.z, view.hipMuzzleFlashBarrelDegrees)), Is.LessThanOrEqualTo(WeaponViewPresenter.FlashJitterDegrees + 0.01f),
                        $"{id}: points along the barrel");
                    Assert.AreEqual(view.hipMuzzleFlashScale * view.hipMuzzleFlashStretch.x, f.localScale.x, 1e-5f, $"{id}: its length");
                    Assert.AreEqual(view.hipMuzzleFlashScale * view.hipMuzzleFlashStretch.y, f.localScale.y, 1e-5f, $"{id}: its width");
                    // The jet's back end lies inside the muzzle (hidden by the gun): it starts at the bore, no gap.
                    float back = (rig.HipFlash.sprite.bounds.extents.x * f.localScale.x) * ppu;
                    Assert.Less(ahead - back, 0f, $"{id}: the root reaches into the muzzle");
                    Assert.Greater(view.hipMuzzleFlashStretch.x, view.hipMuzzleFlashStretch.y, $"{id}: longer along the barrel than across it");
                }
            }
            foreach (var id in new[] { "proto_rifle", "proto_smg" })
            {
                var view = HipProfile(id);
                Assert.IsFalse(view.hipMuzzleFlashAlongBarrel, $"{id}: the star on the muzzle as before");
                using var rig = new Rig(view);
                rig.Presenter.Fire(0f);
                Assert.Less(rig.HipFlash.sortingOrder, rig.Hip.sortingOrder, $"{id}: behind the gun (only beyond its outline shows)");
                Assert.AreEqual(Vector3.one * view.hipMuzzleFlashScale, rig.HipFlash.transform.localScale, id);
                Assert.AreEqual(Vector2.zero, (Vector2)rig.HipFlash.transform.localPosition, id);
            }
        }

        [Test]
        public void EpicSmgGlow_IsAdded_BreathesDeeply_CoreAndGlowApart_AndFollowsItsSettings()
        {
            var view = HipProfile(EpicSmgSpec.WeaponId);
            if (view.hipGlowSprite == null)
                Assert.Inconclusive("the EPIC SMG's glow image is not registered");
            Assert.IsNotNull(view.glowMaterial, "an additive glow material");
            Assert.AreEqual("Deadlock/Sprite Additive Glow", view.glowMaterial.shader.name);
            Assert.That(view.glowMaterial.GetFloat("_Gain"), Is.InRange(1f, 24f));
            Assert.AreEqual(2.4f, view.glowPulseSeconds, 1e-4f, "one breath every 2.4 s");
            Assert.That(1f - view.glowPulse, Is.InRange(0.05f, 0.15f), "the glow nearly goes out at the bottom");
            Assert.That(1f - view.emissionPulse, Is.InRange(0.25f, 0.4f), "the core stays faintly lit at the bottom");
            using var rig = new Rig(view);
            float intensity = view.glowIntensity, glowPulse = view.glowPulse, emissionPulse = view.emissionPulse;
            bool glowEnabled = WeaponViewPresenter.GlowEnabled;
            var p = rig.Presenter;
            void At(float time, float ads = 0f, bool reload = false) => p.Update(new WeaponViewPresenter.Input
                { Ads = ads, Presence = 1f, Aspect = 16f / 9f, Reloading = reload, ReloadProgress = reload ? 0.5f : 0f, DeltaTime = 0f, Time = time });
            try
            {
                Assert.AreSame(view.glowMaterial, p.Glow.sharedMaterial, "the glow is added, not painted over");
                Assert.AreNotSame(view.glowMaterial, p.Emission.sharedMaterial, "the core is drawn as before (sharp pixels)");
                float period = view.glowPulseSeconds;
                At(0f);
                Assert.AreEqual(view.glowIntensity, p.Glow.color.a, 1e-4f, "glow full at the top");
                Assert.AreEqual(view.emissionIntensity, p.Emission.color.a, 1e-4f, "core full at the top");
                At(period / 2f);
                Assert.AreEqual(view.glowIntensity * (1f - view.glowPulse), p.Glow.color.a, 1e-4f, "glow at its bottom");
                Assert.AreEqual(view.emissionIntensity * (1f - view.emissionPulse), p.Emission.color.a, 1e-4f, "core at its bottom");
                Assert.IsTrue(p.Glow.enabled && p.Emission.enabled, "lit without firing, also at the bottom");
                At(period);
                Assert.AreEqual(view.glowIntensity, p.Glow.color.a, 1e-4f, "back to full after one period");

                // Smooth: down through the first half, up through the second, never a jump between 60 fps frames.
                float previousGlow = 1f, previousCore = 1f;
                for (int k = 0; k <= 144; k++)
                {
                    At(k / 60f);
                    float glow = p.Glow.color.a, core = p.Emission.color.a;
                    if (k > 0)
                    {
                        Assert.Less(Mathf.Abs(glow - previousGlow), 0.03f, $"no step in the glow at frame {k}");
                        Assert.Less(Mathf.Abs(core - previousCore), 0.03f, $"no step in the core at frame {k}");
                        if (k <= 72) Assert.LessOrEqual(glow, previousGlow + 1e-5f, "dims through the first half");
                        else Assert.GreaterOrEqual(glow, previousGlow - 1e-5f, "brightens through the second half");
                    }
                    previousGlow = glow;
                    previousCore = core;
                }

                // Every frame: no new garbage (no material, texture or array made while it breathes).
                var frame = new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 1f / 60f, Time = 0.7f };
                At(0.7f);
                Assert.That(() => p.Update(frame), UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not), "no GC allocation per frame");

                // Apart: each layer follows its own depth.
                view.emissionPulse = 0f;
                At(period / 2f);
                Assert.AreEqual(view.emissionIntensity, p.Emission.color.a, 1e-4f, "core steady when its own pulse is 0");
                Assert.AreEqual(view.glowIntensity * (1f - glowPulse), p.Glow.color.a, 1e-4f, "while the glow still breathes");
                view.emissionPulse = emissionPulse;
                view.glowPulse = 0f;
                At(period / 2f);
                Assert.AreEqual(view.glowIntensity, p.Glow.color.a, 1e-4f, "glow steady when its own pulse is 0");
                Assert.AreEqual(view.emissionIntensity * (1f - emissionPulse), p.Emission.color.a, 1e-4f, "while the core still breathes");
                view.glowPulse = glowPulse;

                // Off switch: the glow goes, the core keeps breathing.
                WeaponViewPresenter.GlowEnabled = false;
                At(period / 2f);
                Assert.IsFalse(p.Glow.enabled, "GlowEnabled off: no glow");
                Assert.IsTrue(p.Emission.enabled, "the core stays");
                WeaponViewPresenter.GlowEnabled = true;

                // Kicked and mid-reload: the layers stay on the hip image and keep breathing as at rest.
                p.Fire(period / 2f);
                At(period / 2f, reload: true);
                Assert.AreSame(p.Hip.transform, p.Glow.transform.parent, "the glow moves with the hip image");
                Assert.AreSame(p.Hip.transform, p.Emission.transform.parent, "the core moves with the hip image");
                Assert.AreEqual(view.glowIntensity * (1f - glowPulse), p.Glow.color.a, 1e-4f, "kicked and reloading: the same breath");

                view.glowIntensity = 0.5f;
                At(0f);
                Assert.AreEqual(0.5f, p.Glow.color.a, 1e-4f, "the intensity setting scales it");
                view.glowIntensity = 0f;
                At(0f);
                Assert.IsFalse(p.Glow.enabled, "intensity 0: no glow");
                view.glowIntensity = intensity;
                At(0f, ads: 1f);
                Assert.IsFalse(p.Glow.enabled || p.Emission.enabled, "ADS: no glow layers (no ADS glow images)");
                foreach (var id in new[] { "proto_rifle", "proto_smg", "proto_shotgun", "proto_lmg" })
                {
                    var other = HipProfile(id);
                    Assert.IsNull(other.glowMaterial, $"{id}: no glow");
                    Assert.AreEqual(0f, other.glowPulse, id);
                    Assert.AreEqual(0f, other.emissionPulse, id);
                }
            }
            finally
            {
                view.glowIntensity = intensity;
                view.glowPulse = glowPulse;
                view.emissionPulse = emissionPulse;
                WeaponViewPresenter.GlowEnabled = glowEnabled;
            }
        }

        [Test]
        public void SharedPose_NoProfileOrNoHipImage_DrawsNothingThere()
        {
            using var rig = new Rig(null);
            rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f });
            Assert.IsFalse(rig.Hip.enabled || rig.Ads.enabled || rig.HipFlash.enabled || rig.AdsFlash.enabled);
            var handgun = System.Array.Find(WeaponCatalogSetup.PlannedArts, p => p.WeaponId == "proto_handgun");
            var view = ProfileOf(handgun);
            if (view == null || view.hipSprite != null)
                Assert.Inconclusive("the handgun has no ADS-only art");
            rig.Presenter.SetProfile(view);
            rig.Presenter.Fire(0f);
            rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 0f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f, Time = 0f });
            Assert.IsNull(rig.Hip.sprite, "no hip image: nothing at the hip");
            Assert.IsFalse(rig.HipFlash.enabled, "no flash where no gun is drawn");
            rig.Presenter.Update(new WeaponViewPresenter.Input { Ads = 1f, Presence = 1f, Aspect = 16f / 9f, DeltaTime = 0.016f, Time = 0f });
            Assert.IsTrue(rig.Ads.enabled);
            Assert.AreSame(view.adsSprite, rig.Ads.sprite, "its own ADS image when aimed");
        }
    }
}
