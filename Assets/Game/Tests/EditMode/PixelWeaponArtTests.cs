using Deadlock.Combat;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>The authored pixel-art weapon sprites: import settings, and that the weapons use them with the bake's geometry.</summary>
    public class PixelWeaponArtTests
    {
        const string Folder = "Assets/Game/Art/Weapons";
        static readonly string[] Sprites = { "Spr_PixelRifleHip", "Spr_PixelRifleAds", "Spr_PixelKnife", "Spr_PixelMuzzleFlash" };

        [Test]
        public void PixelSprites_AreCrisp_UncompressedAndWithoutMips()
        {
            foreach (var name in Sprites)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{Folder}/{name}.png");
                Assert.IsNotNull(importer, name);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, name);
                Assert.AreEqual(FilterMode.Point, importer.filterMode, $"{name}: Point filter");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, name);
                Assert.IsFalse(importer.mipmapEnabled, name);
            }
        }

        [Test]
        public void UiIcons_StayBilinear()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Game/Art/UI/Controls/icon_fire.png");
            Assert.AreEqual(FilterMode.Bilinear, importer.filterMode);
        }

        [Test]
        public void Weapons_UseThePixelProfiles_WithTheBakedGeometryAndFeel()
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset");
            var melee = AssetDatabase.LoadAssetAtPath<MeleeDefinition>("Assets/Game/Data/Melee_ProtoKnife.asset");
            var baked = AssetDatabase.LoadAssetAtPath<WeaponViewProfile>("Assets/Game/Data/WeaponView_ProtoRifle.asset");
            var bakedMelee = AssetDatabase.LoadAssetAtPath<MeleeViewProfile>("Assets/Game/Data/MeleeView_ProtoKnife.asset");
            var view = weapon.viewProfile;
            Assert.AreEqual("WeaponView_ProtoRifle_Pixel", view.name);
            Assert.AreEqual("MeleeView_ProtoKnife_Pixel", melee.viewProfile.name);
            Assert.AreEqual(ApprovedHip, AssetDatabase.GetAssetPath(view.hipSprite), "The approved hip image replaces the hip sprite");
            Assert.AreEqual(ApprovedAds, AssetDatabase.GetAssetPath(view.adsSprite), "The approved ADS image replaces the provisional one");
            Assert.AreEqual($"{Folder}/Spr_PixelMuzzleFlash.png", AssetDatabase.GetAssetPath(view.muzzleFlashSprite));
            Assert.AreEqual($"{Folder}/Spr_PixelKnife.png", AssetDatabase.GetAssetPath(melee.viewProfile.sprite));
            Assert.IsTrue(AssetDatabase.GetAssetPath(baked.hipSprite).StartsWith("Assets/Game/Art/Generated/"), "The baked profile is left as baked");

            // Feel values still come from the baked profile (one source of truth).
            Assert.AreEqual(baked.kickOffset, view.kickOffset);
            Assert.AreEqual(baked.reloadRollDegrees, view.reloadRollDegrees);
            Assert.AreEqual(bakedMelee.gripPosition, melee.viewProfile.gripPosition);
        }

        // The game imports the alpha-cleaned derivatives; the approved originals stay next to them unchanged.
        const string ApprovedHip = Folder + "/Rifle/rifle_hip_approved_opaque.png";
        const string ApprovedAds = Folder + "/Rifle/rifle_ads_approved_opaque.png";

        [Test]
        public void Derivatives_OnlyMakeAlphaBinary_SizeAndColoursKept()
        {
            foreach (var (original, derived) in new[] { ("rifle_hip_approved.png", ApprovedHip), ("rifle_ads_approved.png", ApprovedAds) })
            {
                var a = Load($"{Folder}/Rifle/{original}");
                var b = Load(derived);
                Assert.AreEqual(a.width, b.width, original);
                Assert.AreEqual(a.height, b.height, original);
                Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
                for (int i = 0; i < pa.Length; i++)
                {
                    Assert.IsTrue(pb[i].a == 0 || pb[i].a == 255, $"{derived}: alpha is binary");
                    if (pa[i].a >= 128)
                        Assert.IsTrue(pb[i].a == 255 && pb[i].r == pa[i].r && pb[i].g == pa[i].g && pb[i].b == pa[i].b, $"{derived}: drawn pixel {i} kept (dark outlines included)");
                    else
                        Assert.AreEqual(0, pb[i].a, $"{derived}: faint fringe {i} cleared");
                }
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(System.IO.File.ReadAllBytes(path));
            return texture;
        }

        [Test]
        public void ApprovedAds_AimOnTheScreenCentre_BottomCovered_FlashOnTheBoreLine()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ApprovedAds);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.GreaterOrEqual(importer.maxTextureSize, 2048, "Not downscaled (1774 px wide)");

            var view = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset").viewProfile;
            var sprite = view.adsSprite;
            Assert.AreEqual(1774, sprite.rect.width);
            Assert.AreEqual(887, sprite.rect.height);
            // Pivot (= screen centre when ADS is done) = top of the centre front-sight post (887, 390 from the top-left).
            Assert.AreEqual(887f, sprite.pivot.x, 0.5f);
            Assert.AreEqual(887f - 390f, sprite.pivot.y, 0.5f);

            float half = Mathf.Tan(25f * Mathf.Deg2Rad);
            // The drawn hands / receiver reach the image's bottom: it must stay below the screen's bottom with room for
            // the reload drop / roll and the kick, and the image must be as wide as the widest phone (iPhone 16 Pro).
            Assert.Less(sprite.bounds.min.y, -half - 0.1f);
            Assert.GreaterOrEqual(sprite.bounds.max.x, half * 2622f / 1206f);
            Assert.LessOrEqual(sprite.bounds.min.x, -half * 2622f / 1206f);
            // Flash: below the aim point on the centre line (front-sight base), not on the sight tip.
            Assert.AreEqual(0f, view.adsMuzzleOffset.x, 0.001f);
            Assert.Less(view.adsMuzzleOffset.y, -0.03f);
            Assert.Greater(view.adsMuzzleOffset.y, -0.1f);
            // While raising the sights the large hip image only moves down (no edge comes into view).
            Assert.IsTrue(view.hipToAdsRelative);
            Assert.AreEqual(0f, view.hipToAdsMuzzlePosition.x);
            Assert.Less(view.hipToAdsMuzzlePosition.y, 0f);
        }

        [Test]
        public void ApprovedHip_IsImportedCrisp_PivotOnTheMuzzle_AnchoredBottomRight()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(ApprovedHip);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.GreaterOrEqual(importer.maxTextureSize, 2048, "Not downscaled (1672 px wide)");
            Assert.AreEqual(ApprovedHip, AssetDatabase.GetAssetPath(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset").viewProfile.hipSprite));

            var view = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset").viewProfile;
            var sprite = view.hipSprite;
            Assert.AreEqual(1672, sprite.rect.width);
            Assert.AreEqual(941, sprite.rect.height);
            // Pivot = muzzle tip (972, 443 from the top-left): the flash sits on the muzzle.
            Assert.AreEqual(972f, sprite.pivot.x, 0.5f);
            Assert.AreEqual(941f - 443f, sprite.pivot.y, 0.5f);

            // At 16:9 the image's bottom-right is the screen's bottom-right, lowered by 0.05 (arms continue off-screen).
            float half = Mathf.Tan(25f * Mathf.Deg2Rad);
            Vector2 muzzle = view.hipMuzzlePosition;
            float right = muzzle.x + sprite.bounds.max.x;
            float bottom = muzzle.y + sprite.bounds.min.y;
            Assert.AreEqual(half * view.referenceAspect, right, 0.002f);
            Assert.AreEqual(-half - 0.05f, bottom, 0.002f);
            // The screen centre (aim) stays clear: the muzzle is right of and below it.
            Assert.Greater(muzzle.x, 0.15f);
            Assert.Less(muzzle.y, -0.05f);
            Assert.AreEqual(1f, view.hipRightEdgeFollow, "Wider screens: glued to the right edge");
            Assert.AreEqual(0f, view.hipRightEdgeFollowNarrow, "4:3: not pulled toward the centre");
            Assert.AreEqual(1f, view.hipScaleAt4x3, "4:3: not shrunk (the arms would lift off the bottom)");
        }
    }
}
