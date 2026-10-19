using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Generates the placeholder art for the prototype procedurally (no external assets):
    /// low-resolution point-filtered world textures and high-resolution bilinear weapon sprites,
    /// matching the intended contrast between a coarse world and a crisp first-person weapon.
    /// </summary>
    public static class PlaceholderArtGenerator
    {
        public const string Folder = "Assets/Game/Art/Generated";

        public static string WallBrick => $"{Folder}/Tex_WallBrick.png";
        public static string FloorTile => $"{Folder}/Tex_FloorTile.png";
        public static string Crate => $"{Folder}/Tex_Crate.png";
        public static string Concrete => $"{Folder}/Tex_Concrete.png";
        public static string AmmoBox => $"{Folder}/Tex_AmmoBox.png";
        public static string ImpactWall => $"{Folder}/Tex_ImpactWall.png";
        public static string ImpactTarget => $"{Folder}/Tex_ImpactTarget.png";
        public static string TargetSprite => $"{Folder}/Spr_Target.png";
        public static string WeaponHip => $"{Folder}/Spr_WeaponHip.png";
        public static string WeaponAds => $"{Folder}/Spr_WeaponAds.png";
        public static string MuzzleFlash => $"{Folder}/Spr_MuzzleFlash.png";
        public static string UiCircle => $"{Folder}/Ui_Circle.png";
        public static string UiRing => $"{Folder}/Ui_Ring.png";
        public static string UiHitMarker => $"{Folder}/Ui_HitMarker.png";
        public static string UiWhite => $"{Folder}/Ui_White.png";

        /// <summary>Pixels per unit of the hip sprite (1024 px wide =&gt; 1 unit at the view-model camera).</summary>
        public const float HipPixelsPerUnit = 1024f;
        public const float AdsPixelsPerUnit = 1150f;
        /// <summary>Muzzle position in the hip sprite, in pixels from the top-left.</summary>
        public static readonly Vector2 HipMuzzlePixel = new Vector2(58f, 281f);
        /// <summary>Front-sight tip in the ADS sprite (pixels from the top-left). Used as the sprite pivot = aim point.</summary>
        public static readonly Vector2 AdsSightTipPixel = new Vector2(512f, 400f);

        public static void GenerateAll()
        {
            Directory.CreateDirectory(Folder);

            SaveWorldTexture(WallBrick, BrickTexture());
            SaveWorldTexture(FloorTile, FloorTexture());
            SaveWorldTexture(Crate, CrateTexture());
            SaveWorldTexture(Concrete, ConcreteTexture());
            SaveWorldTexture(AmmoBox, AmmoBoxTexture());
            SaveWorldTexture(ImpactWall, SolidNoise(8, new Color32(28, 24, 22, 255), 10, 1));
            SaveWorldTexture(ImpactTarget, SolidNoise(8, new Color32(200, 40, 30, 255), 30, 2));

            SaveSprite(TargetSprite, TargetTexture(), 32f, new Vector2(0.5f, 0.5f), FilterMode.Point);
            SaveSprite(WeaponHip, HipWeaponTexture(), HipPixelsPerUnit, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(WeaponAds, AdsWeaponTexture(), AdsPixelsPerUnit,
                new Vector2(AdsSightTipPixel.x / 1024f, 1f - AdsSightTipPixel.y / 1024f), FilterMode.Bilinear);
            SaveSprite(MuzzleFlash, MuzzleFlashTexture(), 1024f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiCircle, CircleTexture(128, 0f), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiRing, CircleTexture(256, 0.82f), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiHitMarker, HitMarkerTexture(), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiWhite, SolidNoise(4, new Color32(255, 255, 255, 255), 0, 0), 100f, new Vector2(0.5f, 0.5f), FilterMode.Point);
        }

        // ---------------------------------------------------------------- import

        static void SaveWorldTexture(string path, Texture2D texture)
        {
            WritePng(path, texture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static void SaveSprite(string path, Texture2D texture, float pixelsPerUnit, Vector2 pivot, FilterMode filter)
        {
            WritePng(path, texture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = filter;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = filter == FilterMode.Point
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        static void WritePng(string path, Texture2D texture)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // ---------------------------------------------------------------- low-res world textures

        static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false);

        static Color32 Vary(System.Random rng, Color32 c, int amount)
        {
            int d = rng.Next(-amount, amount + 1);
            return new Color32(ClampByte(c.r + d), ClampByte(c.g + d), ClampByte(c.b + d), c.a);
        }

        static byte ClampByte(int v) => (byte)Mathf.Clamp(v, 0, 255);

        static Color32 Shade(Color32 c, float f) =>
            new Color32(ClampByte((int)(c.r * f)), ClampByte((int)(c.g * f)), ClampByte((int)(c.b * f)), c.a);

        static Texture2D BrickTexture()
        {
            var rng = new System.Random(11);
            const int size = 32;
            var tex = NewTexture(size, size);
            var mortar = new Color32(58, 52, 48, 255);
            var brickColors = new[] { new Color32(124, 62, 46, 255), new Color32(110, 54, 42, 255), new Color32(136, 72, 52, 255) };
            var colorOfBrick = new Dictionary<(int, int), Color32>();
            for (int y = 0; y < size; y++)
            {
                int row = y / 8;
                int offset = row % 2 == 0 ? 0 : 8;
                for (int x = 0; x < size; x++)
                {
                    int bx = (x + offset) % size;
                    bool mortarLine = y % 8 == 0 || bx % 16 == 0;
                    Color32 c;
                    if (mortarLine)
                    {
                        c = Vary(rng, mortar, 6);
                    }
                    else
                    {
                        var key = (row, (x + offset) / 16);
                        if (!colorOfBrick.TryGetValue(key, out var baseColor))
                            colorOfBrick[key] = baseColor = brickColors[rng.Next(brickColors.Length)];
                        c = Vary(rng, baseColor, 10);
                        if (y % 8 == 1) c = Shade(c, 0.8f); // lower edge shadow (texture y grows upward)
                        if (y % 8 == 7) c = Shade(c, 1.15f);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        static Texture2D FloorTexture()
        {
            var rng = new System.Random(23);
            const int size = 32;
            var tex = NewTexture(size, size);
            var tile = new Color32(92, 90, 84, 255);
            var grout = new Color32(52, 50, 48, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x % 16 == 0 || y % 16 == 0;
                Color32 c = line ? Vary(rng, grout, 5) : Vary(rng, tile, 9);
                if (!line && (x % 16 == 1 || y % 16 == 15)) c = Shade(c, 1.12f);
                tex.SetPixel(x, y, c);
            }
            // a few stains
            for (int i = 0; i < 6; i++)
            {
                int cx = rng.Next(size), cy = rng.Next(size);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = (cx + dx + size) % size, py = (cy + dy + size) % size;
                    tex.SetPixel(px, py, Shade(tex.GetPixel(px, py), 0.82f));
                }
            }
            tex.Apply();
            return tex;
        }

        static Texture2D CrateTexture()
        {
            var rng = new System.Random(31);
            const int size = 32;
            var tex = NewTexture(size, size);
            var wood = new Color32(138, 98, 56, 255);
            var dark = new Color32(76, 52, 30, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool frame = x < 3 || x > 28 || y < 3 || y > 28;
                bool plankGap = y % 8 == 0;
                bool brace = Mathf.Abs(x - y) <= 1 || Mathf.Abs(x - (31 - y)) <= 1;
                Color32 c = frame || brace ? Vary(rng, Shade(wood, 0.85f), 8) : Vary(rng, wood, 12);
                if (plankGap && !frame) c = Vary(rng, dark, 6);
                if (frame && (x == 0 || y == 0 || x == 31 || y == 31)) c = dark;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D ConcreteTexture()
        {
            var rng = new System.Random(41);
            const int size = 32;
            var tex = NewTexture(size, size);
            var concrete = new Color32(112, 116, 118, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color32 c = Vary(rng, concrete, 12);
                if (y == 0 || y == 16) c = Shade(c, 0.7f);
                if ((x == 4 || x == 27) && (y == 4 || y == 20)) c = new Color32(70, 72, 74, 255); // bolts
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D AmmoBoxTexture()
        {
            var rng = new System.Random(53);
            const int size = 16;
            var tex = NewTexture(size, size);
            var olive = new Color32(84, 96, 48, 255);
            var stripe = new Color32(214, 178, 60, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color32 c = Vary(rng, olive, 8);
                if (y >= 6 && y <= 8) c = Vary(rng, stripe, 10);
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) c = Shade(olive, 0.6f);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D SolidNoise(int size, Color32 color, int amount, int seed)
        {
            var rng = new System.Random(seed);
            var tex = NewTexture(size, size);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, Vary(rng, color, amount));
            tex.Apply();
            return tex;
        }

        static Texture2D TargetTexture()
        {
            // 32x64 humanoid practice silhouette with rings, transparent background.
            const int w = 32, h = 64;
            var tex = NewTexture(w, h);
            var clear = new Color32(0, 0, 0, 0);
            var body = new Color32(46, 52, 60, 255);
            var outline = new Color32(16, 16, 20, 255);
            var ring = new Color32(230, 220, 190, 255);
            var bull = new Color32(200, 40, 30, 255);
            bool Inside(int x, int y)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float headDx = (fx - 16f) / 5.5f, headDy = (fy - 54f) / 6.5f;
                if (headDx * headDx + headDy * headDy <= 1f) return true;          // head
                if (fy >= 44f && fy < 48f && Mathf.Abs(fx - 16f) <= 4f) return true; // neck
                if (fy >= 2f && fy < 46f)
                {
                    float halfWidth = Mathf.Lerp(9f, 14f, Mathf.InverseLerp(2f, 40f, fy));
                    if (fy > 40f) halfWidth = Mathf.Lerp(14f, 9f, Mathf.InverseLerp(40f, 46f, fy));
                    return Mathf.Abs(fx - 16f) <= halfWidth;
                }
                return false;
            }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Inside(x, y)) { tex.SetPixel(x, y, clear); continue; }
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                Color32 c = edge ? outline : body;
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 30f));
                if (!edge)
                {
                    if (r < 2.5f) c = bull;
                    else if (Mathf.Abs(r - 6f) < 0.8f || Mathf.Abs(r - 10.5f) < 0.8f) c = ring;
                }
                float headR = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 54f));
                if (!edge && Mathf.Abs(headR - 3f) < 0.7f) c = ring;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------- high-res sprites

        /// <summary>
        /// Tiny vector rasterizer: shapes are painted in order into an ID buffer at 2x resolution,
        /// boundaries between different shapes become dark outlines, then the result is box-filtered down (anti-aliasing).
        /// Coordinates are in output pixels with the origin at the top-left.
        /// </summary>
        sealed class Canvas2x
        {
            readonly int w, h;
            readonly int[] ids;
            readonly List<Func<float, float, Color>> shaders = new List<Func<float, float, Color>>();
            readonly List<bool> outlined = new List<bool>();

            public Canvas2x(int width, int height)
            {
                w = width * 2;
                h = height * 2;
                ids = new int[w * h];
                for (int i = 0; i < ids.Length; i++) ids[i] = -1;
            }

            public void Polygon(Vector2[] points, Func<float, float, Color> shader, bool outline = true)
            {
                int id = Register(shader, outline);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in points)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
                int x0 = Mathf.Max(0, Mathf.FloorToInt(minX * 2)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(maxX * 2));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minY * 2)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(maxY * 2));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (InsidePolygon(points, (x + 0.5f) / 2f, (y + 0.5f) / 2f))
                        ids[y * w + x] = id;
            }

            public void Ellipse(Vector2 center, Vector2 radius, Func<float, float, Color> shader, bool outline = true, float innerScale = 0f)
            {
                int id = Register(shader, outline);
                int x0 = Mathf.Max(0, (int)((center.x - radius.x) * 2)), x1 = Mathf.Min(w - 1, (int)((center.x + radius.x) * 2) + 1);
                int y0 = Mathf.Max(0, (int)((center.y - radius.y) * 2)), y1 = Mathf.Min(h - 1, (int)((center.y + radius.y) * 2) + 1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = ((x + 0.5f) / 2f - center.x) / radius.x;
                    float dy = ((y + 0.5f) / 2f - center.y) / radius.y;
                    float d = dx * dx + dy * dy;
                    if (d <= 1f && d >= innerScale * innerScale)
                        ids[y * w + x] = id;
                }
            }

            int Register(Func<float, float, Color> shader, bool outline)
            {
                shaders.Add(shader);
                outlined.Add(outline);
                return shaders.Count - 1;
            }

            static bool InsidePolygon(Vector2[] poly, float x, float y)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    if ((poly[i].y > y) != (poly[j].y > y) &&
                        x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                        inside = !inside;
                }
                return inside;
            }

            public Texture2D Resolve(Color outlineColor, int outlineWidth = 3)
            {
                var full = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int id = ids[y * w + x];
                    if (id < 0) { full[y * w + x] = Color.clear; continue; }
                    Color c = shaders[id]((x + 0.5f) / 2f, (y + 0.5f) / 2f);
                    if (outlined[id] && IsNearBoundary(x, y, id, outlineWidth))
                        c = new Color(outlineColor.r, outlineColor.g, outlineColor.b, c.a);
                    full[y * w + x] = c;
                }

                int ow = w / 2, oh = h / 2;
                var tex = NewTexture(ow, oh);
                var pixels = new Color[ow * oh];
                for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        Color c = full[(y * 2 + sy) * w + x * 2 + sx];
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); // premultiplied average
                    }
                    Color avg = sum / 4f;
                    Color straight = avg.a > 0f ? new Color(avg.r / avg.a, avg.g / avg.a, avg.b / avg.a, avg.a) : Color.clear;
                    pixels[(oh - 1 - y) * ow + x] = straight; // flip: canvas origin is top-left
                }
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            bool IsNearBoundary(int x, int y, int id, int radius)
            {
                for (int dy = -radius; dy <= radius; dy += radius)
                for (int dx = -radius; dx <= radius; dx += radius)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int other = ids[ny * w + nx];
                    // Only outline against shapes painted below (or empty space), so overlapping parts read as layered.
                    if (other != id && (other < id))
                        return true;
                }
                return false;
            }
        }

        static Vector2[] P(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }

        /// <summary>Metal with a top-lit vertical gradient between y0 and y1 plus a fine brushed noise.</summary>
        static Func<float, float, Color> Metal(Color baseColor, float y0, float y1, float light = 0.35f) => (x, y) =>
        {
            float t = Mathf.InverseLerp(y0, y1, y);
            float n = Mathf.PerlinNoise(x * 0.08f, y * 0.9f) * 0.06f;
            float f = 1f + light * (1f - t) - light * 0.6f * t + n;
            return new Color(baseColor.r * f, baseColor.g * f, baseColor.b * f, 1f);
        };

        static Func<float, float, Color> Cloth(Color baseColor, float scale = 0.05f) => (x, y) =>
        {
            float n = Mathf.PerlinNoise(x * scale, y * scale) * 0.25f + Mathf.PerlinNoise(x * 0.6f, y * 0.6f) * 0.06f;
            float f = 0.88f + n;
            return new Color(baseColor.r * f, baseColor.g * f, baseColor.b * f, 1f);
        };

        static Func<float, float, Color> Flat(Color c) => (x, y) => c;

        static readonly Color GunMetal = new Color(0.25f, 0.26f, 0.28f);
        static readonly Color GunDark = new Color(0.13f, 0.13f, 0.15f);
        static readonly Color Polymer = new Color(0.20f, 0.19f, 0.17f);
        static readonly Color Glove = new Color(0.30f, 0.24f, 0.17f);
        static readonly Color Sleeve = new Color(0.27f, 0.31f, 0.20f);
        static readonly Color OutlineColor = new Color(0.04f, 0.04f, 0.05f);

        static Texture2D HipWeaponTexture()
        {
            // 1024x512. Rifle seen from behind-right, muzzle toward the upper-left (screen centre).
            var c = new Canvas2x(1024, 512);
            c.Polygon(P(760, 512, 870, 420, 1024, 395, 1024, 512), Cloth(Sleeve));                      // right sleeve
            c.Polygon(P(690, 512, 700, 380, 830, 330, 1024, 300, 1024, 512), Metal(Polymer, 300, 512, 0.25f)); // stock / grip
            c.Polygon(P(560, 400, 660, 388, 640, 512, 528, 512), Metal(GunDark, 388, 512, 0.2f));        // magazine
            c.Polygon(P(420, 300, 820, 248, 870, 330, 720, 420, 460, 395), Metal(GunMetal, 248, 420));  // receiver
            c.Polygon(P(600, 300, 720, 285, 725, 310, 605, 325), Flat(GunDark));                         // ejection port
            c.Polygon(P(240, 298, 470, 268, 490, 372, 270, 382), Metal(Polymer, 268, 382));              // handguard
            for (int i = 0; i < 4; i++)                                                                     // handguard vents
            {
                float x = 290 + i * 45;
                c.Polygon(P(x, 318 - i * 5, x + 25, 315 - i * 5, x + 27, 340 - i * 5, x + 2, 343 - i * 5), Flat(GunDark), false);
            }
            c.Polygon(P(52, 268, 250, 284, 252, 302, 54, 292), Metal(GunMetal, 268, 302, 0.5f));          // barrel
            c.Polygon(P(40, 262, 92, 266, 94, 298, 42, 296), Metal(GunDark, 262, 298, 0.4f));             // muzzle device
            c.Polygon(P(118, 236, 134, 236, 140, 276, 116, 276), Metal(GunDark, 236, 276));              // front sight post
            c.Polygon(P(520, 252, 770, 222, 776, 244, 526, 274), Metal(GunDark, 222, 274, 0.4f));        // top rail
            c.Polygon(P(700, 222, 760, 214, 768, 246, 708, 254), Metal(GunDark, 214, 254, 0.3f));        // rear sight
            c.Ellipse(new Vector2(360, 384), new Vector2(88, 52), Cloth(Glove, 0.04f));                  // support hand
            c.Polygon(P(300, 400, 420, 380, 470, 512, 320, 512), Cloth(Sleeve));                        // support sleeve
            c.Polygon(P(700, 395, 770, 360, 840, 400, 820, 470, 730, 470), Cloth(Glove, 0.04f));         // firing hand
            c.Polygon(P(64, 270, 248, 285, 248, 288, 64, 273), Flat(new Color(0.55f, 0.57f, 0.6f)), false); // barrel highlight
            c.Polygon(P(430, 302, 815, 252, 818, 258, 433, 308), Flat(new Color(0.45f, 0.47f, 0.5f)), false); // receiver highlight
            return c.Resolve(OutlineColor);
        }

        static Texture2D AdsWeaponTexture()
        {
            // 1024x1024 sight picture. The front sight post tip is at AdsSightTipPixel (= sprite pivot = aim point).
            var c = new Canvas2x(1024, 1024);
            c.Polygon(P(0, 1024, 0, 800, 200, 740, 330, 800, 320, 1024), Cloth(Glove, 0.03f));            // support hand
            c.Polygon(P(720, 1024, 780, 890, 1024, 850, 1024, 1024), Cloth(Sleeve));                    // firing sleeve
            c.Polygon(P(290, 1024, 440, 560, 584, 560, 734, 1024), Metal(GunMetal, 560, 1024, 0.45f));   // receiver / handguard top
            c.Polygon(P(470, 560, 554, 560, 600, 1024, 424, 1024), Metal(GunDark, 560, 1024, 0.3f));    // rail
            for (int i = 0; i < 7; i++)                                                                     // rail slots
            {
                float y = 600 + i * 60, half = 46 + i * 7;
                c.Polygon(P(512 - half, y, 512 + half, y, 512 + half + 4, y + 16, 512 - half - 4, y + 16), Flat(new Color(0.08f, 0.08f, 0.09f)), false);
            }
            c.Polygon(P(462, 470, 562, 470, 584, 560, 440, 560), Metal(GunDark, 470, 560));               // front sight base
            c.Polygon(P(448, 470, 470, 382, 486, 382, 480, 470), Metal(GunDark, 382, 470));              // left ear
            c.Polygon(P(576, 470, 554, 382, 538, 382, 544, 470), Metal(GunDark, 382, 470));              // right ear
            c.Polygon(P(504, 400, 520, 400, 523, 472, 501, 472), Flat(new Color(0.05f, 0.05f, 0.06f)));   // front post (tip at 512,400)
            c.Polygon(P(360, 690, 664, 690, 720, 860, 304, 860), Metal(GunDark, 690, 860, 0.25f));        // rear sight base
            // Rear aperture: large, close to the eye, centred on the front post tip.
            c.Ellipse(new Vector2(512, 400), new Vector2(330, 330), Metal(new Color(0.09f, 0.09f, 0.1f), 70, 730, 0.25f), true, 0.7f);
            return c.Resolve(OutlineColor);
        }

        static Texture2D MuzzleFlashTexture()
        {
            const int size = 256;
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx);
                float spikes = 0.35f + 0.65f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3.5f)), 6f);
                float alpha = Mathf.Clamp01(1f - r / spikes);
                alpha = alpha * alpha;
                Color hot = Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.97f, 0.8f), alpha);
                pixels[y * size + x] = new Color(hot.r, hot.g, hot.b, alpha);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static Texture2D CircleTexture(int size, float inner)
        {
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float outerA = Mathf.Clamp01((1f - r) * half);
                float innerA = inner > 0f ? Mathf.Clamp01((r - inner) * half) : 1f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, outerA * innerA);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static Texture2D HitMarkerTexture()
        {
            const int size = 64;
            var tex = NewTexture(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f - size / 2f, fy = y + 0.5f - size / 2f;
                float d = Mathf.Min(Mathf.Abs(fx - fy), Mathf.Abs(fx + fy)) / Mathf.Sqrt(2f);
                float r = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fy));
                bool arm = r > 9f && r < 28f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, arm ? Mathf.Clamp01(2.5f - d) : 0f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
