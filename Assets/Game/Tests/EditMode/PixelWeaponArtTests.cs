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
            Assert.AreEqual($"{Folder}/Spr_PixelRifleHip.png", AssetDatabase.GetAssetPath(view.hipSprite));
            Assert.AreEqual($"{Folder}/Spr_PixelRifleAds.png", AssetDatabase.GetAssetPath(view.adsSprite));
            Assert.AreEqual($"{Folder}/Spr_PixelMuzzleFlash.png", AssetDatabase.GetAssetPath(view.muzzleFlashSprite));
            Assert.AreEqual($"{Folder}/Spr_PixelKnife.png", AssetDatabase.GetAssetPath(melee.viewProfile.sprite));
            Assert.IsTrue(AssetDatabase.GetAssetPath(baked.hipSprite).StartsWith("Assets/Game/Art/Generated/"), "The baked profile is left as baked");

            // Same muzzle / sight geometry as the bake: the flash and the aim point stay where they were.
            // The ADS muzzle comes from pixel_weapons.json ("muzzle"); within half an art pixel of the bake's.
            float halfPixel = 0.5f / view.adsSprite.pixelsPerUnit;
            Assert.AreEqual(baked.adsMuzzleOffset.x, view.adsMuzzleOffset.x, halfPixel);
            Assert.AreEqual(baked.adsMuzzleOffset.y, view.adsMuzzleOffset.y, halfPixel);
            Assert.AreEqual(baked.hipMuzzlePosition, view.hipMuzzlePosition);
            Assert.AreEqual(baked.kickOffset, view.kickOffset);
            Assert.AreEqual(bakedMelee.gripPosition, melee.viewProfile.gripPosition);
            AssertSameFrame(baked.hipSprite, view.hipSprite);
            AssertSameFrame(baked.adsSprite, view.adsSprite);
        }

        /// <summary>World-space size and pivot match within one art pixel (the art is a third of the bake's resolution).</summary>
        static void AssertSameFrame(Sprite bake, Sprite art)
        {
            float artPixel = 1f / art.pixelsPerUnit;
            Assert.AreEqual(bake.bounds.min.x, art.bounds.min.x, artPixel, art.name);
            Assert.AreEqual(bake.bounds.min.y, art.bounds.min.y, artPixel, art.name);
            Assert.AreEqual(bake.bounds.max.x, art.bounds.max.x, artPixel, art.name);
            Assert.AreEqual(bake.bounds.max.y, art.bounds.max.y, artPixel, art.name);
        }
    }
}
