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
            for (int i = 0; i < knife.poses.Length; i++)
                yield return ($"knife pose {i + 1}", knife.poses[i].image, knife.poses[i].skinRegions, knife.viewProfile.poseSkinRegions[i]);
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
        public void KnifePoseRegionMaps_HandsArmsOnly_NeverOnTheKnife()
        {
            var knife = Registry.melee.Single(m => m.WeaponId == "proto_knife");
            Assert.AreEqual(3, knife.poses.Length, "three attack poses");
            for (int i = 0; i < knife.poses.Length; i++)
            {
                string name = $"knife pose {i + 1}";
                var map = Pixels(knife.poses[i].skinRegions, out int w, out int h);
                // The knife's own layer of the same image (Tools/pixelart/split_layers.py): steel, guard, handle, pommel.
                var weaponLayer = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Game/Art/Weapons/Knife/Poses/Layers/knife_pose{i + 1}_weapon.png");
                Assert.IsNotNull(weaponLayer, $"{name}: knife layer");
                var steel = Pixels(weaponLayer, out int lw, out int lh);
                Assert.AreEqual((w, h), (lw, lh), name);
                var counts = new Dictionary<int, int>();
                for (int p = 0; p < map.Length; p++)
                {
                    int region = map[p].r / 32;
                    if (region == 0)
                        continue;
                    Assert.Less(steel[p].a, 128, $"{name}: skin region {(SkinRegion)region} on the knife (pixel {p})");
                    counts[region] = counts.TryGetValue(region, out int c) ? c + 1 : 1;
                }
                Assert.IsFalse(counts.ContainsKey((int)SkinRegion.WeaponBody), $"{name}: no weapon skin region (the knife has no weapon skin)");
                foreach (var region in new[] { SkinRegion.GloveBody, SkinRegion.WristBand, SkinRegion.SleeveBase, SkinRegion.SleeveCamo })
                    Assert.Greater(counts.GetValueOrDefault((int)region), 0, $"{name}: {region} region");
            }
        }

        static SkinCatalog Catalog => AssetDatabase.LoadAssetAtPath<SkinCatalog>(SkinSetup.CatalogPath);

        [Test]
        public void EquipmentImages_Imported_AsCrispSprites_FullSize_WithRegionMaps()
        {
            var catalog = Catalog;
            var rifle = catalog.weapons.Single(w => w.weaponId == "proto_rifle");
            Assert.AreEqual("Deadlock/Skinned UI", catalog.uiMaterial.shader.name);
            foreach (var (name, sprite, regions, path, w, h) in new[]
                     {
                         ("rifle side", rifle.sideView, rifle.sideViewRegions, SkinSetup.RifleSidePath, 1860, 846),
                         ("player front", catalog.playerFront, catalog.playerFrontRegions, SkinSetup.PlayerFrontPath, 1024, 1536),
                     })
            {
                Assert.IsNotNull(sprite, name);
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(sprite), $"{name}: the displayed derivative");
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, name);
                Assert.AreEqual(FilterMode.Point, importer.filterMode, $"{name}: crisp pixels");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, name);
                Assert.IsFalse(importer.mipmapEnabled, name);
                Assert.IsTrue(importer.alphaIsTransparency, name);
                Assert.AreEqual((w, h), (sprite.texture.width, sprite.texture.height), $"{name}: not downscaled");
                Assert.IsNotNull(regions, $"{name}: region map");
                Assert.AreEqual(TextureFormat.R8, regions.format, name);
                Assert.AreEqual((w, h), (regions.width, regions.height), $"{name}: region map on the same canvas");
            }
        }

        /// <summary>
        /// The SMG side view is the image registered by AssetPipeline (weapon.smg.side): byte for byte the approved file
        /// (SHA-256), the common weapon side-view standard (1440 x 800), imported like the rifle's (crisp, full size), and
        /// drawn in its own colours (no region map).
        /// </summary>
        [Test]
        public void SmgSideView_IsTheRegisteredImage_Unchanged_ImportedLikeTheRifles()
        {
            var smg = Catalog.weapons.Single(w => w.weaponId == "proto_smg");
            Assert.IsNotNull(smg.sideView, "the SMG has a side view");
            Assert.AreEqual(SkinSetup.SmgSidePath, AssetDatabase.GetAssetPath(smg.sideView));
            Assert.AreEqual("Assets/Game/Art/Weapons/SMG/Side/smg_side_design.png", SkinSetup.SmgSidePath);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = System.IO.File.OpenRead(SkinSetup.SmgSidePath))
                Assert.AreEqual("b3bb6aa9714e5f2986ba7d1ee18ea952ff640c759032b94a70c5776a08cbc7fe",
                    System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(), "the approved file, unchanged");
            Assert.AreEqual((1440, 800), (smg.sideView.texture.width, smg.sideView.texture.height), "the standard, not downscaled");
            Assert.AreEqual(new Rect(0f, 0f, 1440f, 800f), smg.sideView.rect, "the whole canvas");
            Assert.IsNull(smg.sideViewRegions, "no region map: its drawn colours");
            var importer = (TextureImporter)AssetImporter.GetAtPath(SkinSetup.SmgSidePath);
            var rifle = (TextureImporter)AssetImporter.GetAtPath(SkinSetup.RifleSidePath);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(FilterMode.Point, importer.filterMode, "crisp pixels");
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);
            Assert.AreEqual(rifle.spritePixelsPerUnit, importer.spritePixelsPerUnit, "as the rifle's");
            Assert.AreEqual(rifle.spritePivot, importer.spritePivot);
            var rifleSet = Catalog.weapons.Single(w => w.weaponId == "proto_rifle");
            Assert.AreEqual(SkinSetup.RifleSidePath, AssetDatabase.GetAssetPath(rifleSet.sideView), "the rifle's side view is unchanged");

            // One scale in the ammo panel: both pictures fill its height (PlayMode), so the drawn guns' heights, as parts
            // of their canvases, must be close (the SMG a little smaller, never larger than the rifle by much).
            float rifleGun = DrawnHeightPart(SkinSetup.RifleSidePath), smgGun = DrawnHeightPart(SkinSetup.SmgSidePath);
            TestContext.WriteLine($"drawn gun height: rifle {rifleGun:0.000}, SMG {smgGun:0.000} of the canvas");
            Assert.AreEqual(1f, smgGun / rifleGun, 0.15f, "the SMG drawn at the rifle's scale");
        }

        /// <summary>Height of the drawn pixels (alpha &gt; 0), as a part of the canvas height (read from the file).</summary>
        static float DrawnHeightPart(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(t.LoadImage(File.ReadAllBytes(path)), path);
                var px = t.GetPixels32();
                int top = -1, bottom = -1;
                for (int y = 0; y < t.height; y++)
                    for (int x = 0; x < t.width; x++)
                        if (px[y * t.width + x].a > 0)
                        {
                            if (bottom < 0)
                                bottom = y;
                            top = y;
                            break;
                        }
                return (top - bottom + 1) / (float)t.height;
            }
            finally
            {
                Object.DestroyImmediate(t);
            }
        }

        [Test]
        public void EquipmentDerivatives_ChangeOnlyAlpha_OriginalsKept()
        {
            foreach (var (original, derived) in new[]
                     {
                         ("Assets/Game/Art/Weapons/Rifle/Side/rifle_side_design.png", SkinSetup.RifleSidePath),
                         ("Assets/Game/Art/Player/Front/player_front_gloves_adopted.png", SkinSetup.PlayerFrontPath),
                     })
            {
                var o = Pixels(AssetDatabase.LoadAssetAtPath<Texture2D>(original), out int ow, out int oh);
                var d = Pixels(AssetDatabase.LoadAssetAtPath<Texture2D>(derived), out int dw, out int dh);
                Assert.AreEqual((ow, oh), (dw, dh), derived);
                int notBinary = 0, colourChanged = 0, wrongThreshold = 0;
                for (int i = 0; i < d.Length; i++)
                {
                    if (d[i].a != 0 && d[i].a != 255)
                        notBinary++;
                    if (d[i].a == 255 && (o[i].r != d[i].r || o[i].g != d[i].g || o[i].b != d[i].b))
                        colourChanged++;
                    if ((o[i].a >= 128) != (d[i].a == 255))
                        wrongThreshold++;
                }
                Assert.AreEqual(0, notBinary, $"{derived}: binary alpha");
                Assert.AreEqual(0, colourChanged, $"{derived}: colours of the shown pixels kept");
                Assert.AreEqual(0, wrongThreshold, $"{derived}: alpha threshold 128");
            }
        }

        [Test]
        public void AdoptedGloveColour_ChangesOnlyTheGloves_OfTheDeliveredOriginal()
        {
            var original = Pixels(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Player/Front/player_front_design.png"), out int w, out int h);
            var adopted = Pixels(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Player/Front/player_front_gloves_adopted.png"), out int aw, out int ah);
            var glove = Pixels(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Player/Front/Masks/player_front_glove_body.png"), out _, out _);
            Assert.AreEqual((w, h), (aw, ah), "same canvas");
            int alpha = 0, outside = 0, inside = 0;
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i].a != adopted[i].a)
                    alpha++;
                bool changed = original[i].r != adopted[i].r || original[i].g != adopted[i].g || original[i].b != adopted[i].b;
                if (changed && glove[i].a == 0)
                    outside++;
                if (changed && glove[i].a != 0)
                    inside++;
            }
            Assert.AreEqual(0, alpha, "alpha kept");
            Assert.AreEqual(0, outside, "nothing outside the glove mask changed");
            Assert.Greater(inside, 0, "the gloves are recoloured");
        }

        [Test]
        public void EquipmentRegionMaps_RifleBodyOnly_PlayerHandsArmsOnly()
        {
            var catalog = Catalog;
            var rifle = catalog.weapons.Single(w => w.weaponId == "proto_rifle");
            var map = Pixels(rifle.sideViewRegions, out int rw, out _);
            var art = Pixels(rifle.sideView.texture, out _, out _);
            int body = 0, other = 0, onLine = 0;
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i].r == 0)
                    continue;
                if (map[i].r != 32)
                    other++;
                if (Mathf.Max(art[i].r, art[i].g, art[i].b) < 18)
                    onLine++;
                body++;
            }
            Assert.AreEqual(0, other, "rifle side view: only weapon_body");
            Assert.AreEqual(0, onLine, "rifle side view: not on line black");
            Assert.Greater(body, 0, "rifle furniture region present");

            map = Pixels(catalog.playerFrontRegions, out int pw, out int ph);
            art = Pixels(catalog.playerFront.texture, out _, out _);
            var allowed = new[] { 2, 4, 5, 6 };
            var counts = new Dictionary<int, int>();
            int notAllowed = 0, torso = 0, headOrLegs = 0, transparentOrLine = 0;
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i].r == 0)
                    continue;
                int region = map[i].r / 32;
                if (System.Array.IndexOf(allowed, region) < 0 || map[i].r % 32 != 0)
                    notAllowed++;
                int x = i % pw, yTop = ph - 1 - i / pw; // GetPixels32 rows start at the bottom
                if (x >= 400 && x <= 624)
                    torso++;
                if (yTop < 280 || yTop > 900)
                    headOrLegs++;
                if (art[i].a < 128 || Mathf.Max(art[i].r, art[i].g, art[i].b) < 18)
                    transparentOrLine++;
                counts[region] = counts.GetValueOrDefault(region) + 1;
            }
            Assert.AreEqual(0, notAllowed, "player front: only the hand / arm regions of the player skin");
            Assert.AreEqual(0, torso, "no skin region on the jacket torso / vest / trousers column");
            Assert.AreEqual(0, headOrLegs, "no skin region on the head or the legs / boots");
            Assert.AreEqual(0, transparentOrLine, "no skin region on transparent or line-black pixels");
            foreach (int r in allowed)
                Assert.Greater(counts.GetValueOrDefault(r), 0, $"player front region {(SkinRegion)r} present");
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
