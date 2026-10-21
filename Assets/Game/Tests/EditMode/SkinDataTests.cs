using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Editor;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Skin data: region maps imported as point-sampled R8 data on the same canvas as their image and referenced by the
    /// view profiles; region maps hold only known regions and keep line black / weapon / hands apart; the catalog keeps
    /// weapon and player skins to their own regions.
    /// </summary>
    public class SkinDataTests
    {
        static WeaponArtRegistry Registry => AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);

        static IEnumerable<(string name, Texture2D image, Texture2D regions, Texture2D profileRegions)> Views()
        {
            var rifle = Registry.weapons.Single(w => w.WeaponId == "proto_rifle");
            yield return ("rifle hip", rifle.hip.image, rifle.hip.skinRegions, rifle.viewProfile.hipSkinRegions);
            yield return ("rifle ads", rifle.ads.image, rifle.ads.skinRegions, rifle.viewProfile.adsSkinRegions);
            var knife = Registry.melee.Single(m => m.WeaponId == "proto_knife");
            yield return ("knife idle", knife.art.image, knife.art.skinRegions, knife.viewProfile.skinRegions);
        }

        static Color32[] Pixels(Texture2D asset, out int width, out int height)
        {
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(asset)));
            width = copy.width;
            height = copy.height;
            var pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        [Test]
        public void RegionMaps_Imported_AsPointR8Data_SameCanvas_InTheProfiles()
        {
            foreach (var (name, image, regions, profileRegions) in Views())
            {
                Assert.IsNotNull(regions, $"{name}: region map set in the art definition");
                Assert.AreSame(regions, profileRegions, $"{name}: the view profile uses the definition's region map");
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(regions));
                Assert.AreEqual(TextureImporterType.SingleChannel, importer.textureType, name);
                Assert.IsFalse(importer.sRGBTexture, $"{name}: data, not colour");
                Assert.AreEqual(FilterMode.Point, importer.filterMode, name);
                Assert.IsFalse(importer.mipmapEnabled, name);
                Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale, $"{name}: never resized");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, name);
                Assert.AreEqual(TextureFormat.R8, regions.format, $"{name}: one 8-bit channel");
                Assert.AreEqual(image.width, regions.width, $"{name}: same width as the image");
                Assert.AreEqual(image.height, regions.height, $"{name}: same height as the image");
            }
        }

        [Test]
        public void RegionMaps_OnlyKnownRegions_NeverOnLineBlackOrTransparentPixels()
        {
            foreach (var (name, image, regions, _) in Views())
            {
                var map = Pixels(regions, out int w, out int h);
                var art = Pixels(image, out int iw, out int ih);
                Assert.AreEqual((iw, ih), (w, h), name);
                var counts = new Dictionary<int, int>();
                for (int i = 0; i < map.Length; i++)
                {
                    int value = map[i].r;
                    if (value == 0)
                        continue;
                    Assert.AreEqual(0, value % 32, $"{name}: region value {value} at pixel {i}");
                    int region = value / 32;
                    Assert.That(region, Is.InRange(1, 6), $"{name}: unknown region {region}");
                    Assert.GreaterOrEqual(art[i].a, 128, $"{name}: region on a transparent pixel ({i})");
                    Assert.GreaterOrEqual(Mathf.Max(art[i].r, art[i].g, art[i].b), 18, $"{name}: region on line black ({i})");
                    counts[region] = counts.TryGetValue(region, out int c) ? c + 1 : 1;
                }
                Assert.Greater(counts.GetValueOrDefault((int)SkinRegion.GloveBody), 0, $"{name}: glove region");
                Assert.Greater(counts.GetValueOrDefault((int)SkinRegion.SleeveBase), 0, $"{name}: sleeve region");
                bool rifle = name.StartsWith("rifle");
                Assert.AreEqual(rifle, counts.GetValueOrDefault((int)SkinRegion.WeaponBody) > 0, $"{name}: weapon body region only where a weapon mask exists");
            }
        }

        [Test]
        public void Catalog_WeaponAndPlayerSkins_StayInTheirOwnRegions_UniqueIds_Material()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(SkinSetup.CatalogPath);
            Assert.IsNotNull(catalog, SkinSetup.CatalogPath);
            Assert.IsNotNull(catalog.material);
            Assert.AreEqual("Deadlock/Skinned Sprite", catalog.material.shader.name);
            var weaponIds = catalog.weapons.Select(s => s.weaponId).ToList();
            CollectionAssert.AllItemsAreUnique(weaponIds);
            CollectionAssert.Contains(weaponIds, "proto_rifle");
            foreach (var set in catalog.weapons)
            {
                CollectionAssert.AllItemsAreUnique(set.skins.Select(s => s.skinId).ToList(), set.weaponId);
                foreach (var skin in set.skins)
                {
                    Assert.AreNotEqual(SkinCatalog.StandardId, skin.skinId, "standard is implicit");
                    foreach (var c in skin.colours)
                        Assert.IsTrue(SkinCatalog.IsWeaponRegion(c.region), $"{set.weaponId}/{skin.skinId}: weapon skin colours only weapon regions ({c.region})");
                }
            }
            Assert.Greater(catalog.weapons.Single(s => s.weaponId == "proto_rifle").skins.Length, 0, "the rifle has a test colour");
            Assert.Greater(catalog.playerSkins.Length, 0, "a test hand / arm colour exists");
            CollectionAssert.AllItemsAreUnique(catalog.playerSkins.Select(s => s.skinId).ToList());
            foreach (var skin in catalog.playerSkins)
            {
                Assert.AreNotEqual(SkinCatalog.StandardId, skin.skinId, "standard is implicit");
                foreach (var c in skin.colours)
                    Assert.IsTrue(SkinCatalog.IsPlayerRegion(c.region), $"{skin.skinId}: player skin colours only hand / arm regions ({c.region})");
            }
        }
    }
}
