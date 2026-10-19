using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Generates the placeholder art for the prototype procedurally (no external assets):
    /// low-resolution point-filtered world textures, target sprite, muzzle flash and UI shapes.
    /// The first-person weapon sprites are pre-rendered separately by <see cref="WeaponSpriteBaker"/>.
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
        /// <summary>First-person weapon sprites, written by <see cref="WeaponSpriteBaker"/> (not by this class).</summary>
        public static string WeaponHip => $"{Folder}/Spr_WeaponHip.png";
        public static string WeaponAds => $"{Folder}/Spr_WeaponAds.png";
        public static string MuzzleFlash => $"{Folder}/Spr_MuzzleFlash.png";
        public static string UiCircle => $"{Folder}/Ui_Circle.png";
        public static string UiRing => $"{Folder}/Ui_Ring.png";
        public static string UiHitMarker => $"{Folder}/Ui_HitMarker.png";
        public static string UiWhite => $"{Folder}/Ui_White.png";
        public static string ZombieWalkA => $"{Folder}/Spr_Zombie_WalkA.png";
        public static string ZombieWalkB => $"{Folder}/Spr_Zombie_WalkB.png";
        public static string ZombieAttack => $"{Folder}/Spr_Zombie_Attack.png";
        public static string ZombieDead => $"{Folder}/Spr_Zombie_Dead.png";

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
            SaveSprite(MuzzleFlash, MuzzleFlashTexture(), 1024f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiCircle, CircleTexture(128, 0f), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiRing, CircleTexture(256, 0.82f), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiHitMarker, HitMarkerTexture(), 100f, new Vector2(0.5f, 0.5f), FilterMode.Bilinear);
            SaveSprite(UiWhite, SolidNoise(4, new Color32(255, 255, 255, 255), 0, 0), 100f, new Vector2(0.5f, 0.5f), FilterMode.Point);
            // Zombies: 32 px per metre like the targets, feet at the pivot.
            SaveSprite(ZombieWalkA, ZombieTexture(ZombiePose.WalkA), 32f, new Vector2(0.5f, 0f), FilterMode.Point);
            SaveSprite(ZombieWalkB, ZombieTexture(ZombiePose.WalkB), 32f, new Vector2(0.5f, 0f), FilterMode.Point);
            SaveSprite(ZombieAttack, ZombieTexture(ZombiePose.Attack), 32f, new Vector2(0.5f, 0f), FilterMode.Point);
            SaveSprite(ZombieDead, ZombieDeadTexture(), 32f, new Vector2(0.5f, 0f), FilterMode.Point);
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

        enum ZombiePose { WalkA, WalkB, Attack }

        static readonly Color32 ZSkin = new Color32(118, 146, 104, 255);
        static readonly Color32 ZSkinDark = new Color32(84, 108, 74, 255);
        static readonly Color32 ZShirt = new Color32(62, 66, 84, 255);
        static readonly Color32 ZPants = new Color32(72, 56, 44, 255);
        static readonly Color32 ZEye = new Color32(230, 50, 30, 255);
        static readonly Color32 ZBlood = new Color32(120, 20, 18, 255);
        static readonly Color32 ZOutline = new Color32(18, 22, 18, 255);

        /// <summary>Small pixel painter: fill shapes into a buffer, then outline every filled pixel next to empty space.</summary>
        sealed class Pixels
        {
            public readonly int W, H;
            readonly Color32?[] px;

            public Pixels(int w, int h)
            {
                W = w;
                H = h;
                px = new Color32?[w * h];
            }

            public void Rect(int x0, int y0, int x1, int y1, Color32 c)
            {
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++)
                    px[y * W + x] = c;
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
            {
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f)
                        px[y * W + x] = c;
                }
            }

            public void Dot(int x, int y, Color32 c)
            {
                if (x >= 0 && y >= 0 && x < W && y < H)
                    px[y * W + x] = c;
            }

            public Texture2D ToTexture()
            {
                var tex = NewTexture(W, H);
                bool Filled(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && px[y * W + x].HasValue;
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!Filled(x, y)) { tex.SetPixel(x, y, new Color32(0, 0, 0, 0)); continue; }
                    bool edge = !Filled(x - 1, y) || !Filled(x + 1, y) || !Filled(x, y - 1) || !Filled(x, y + 1);
                    tex.SetPixel(x, y, edge ? ZOutline : px[y * W + x].Value);
                }
                tex.Apply();
                return tex;
            }
        }

        /// <summary>32x64 front-facing zombie (texture y grows upward, feet at y = 0).</summary>
        static Texture2D ZombieTexture(ZombiePose pose)
        {
            var p = new Pixels(32, 64);
            // Legs (alternate stride for the walk frames).
            int leftLift = pose == ZombiePose.WalkB ? 3 : 0, rightLift = pose == ZombiePose.WalkA ? 3 : 0;
            p.Rect(10, 0 + leftLift, 15, 27, ZPants);
            p.Rect(17, 0 + rightLift, 22, 27, ZPants);
            p.Rect(10, 0 + leftLift, 15, 2 + leftLift, ZSkinDark);
            p.Rect(17, 0 + rightLift, 22, 2 + rightLift, ZSkinDark);
            // Torso (torn shirt with skin showing).
            p.Rect(8, 26, 24, 47, ZShirt);
            p.Rect(12, 30, 15, 34, ZSkin);
            p.Rect(19, 38, 22, 41, ZSkin);
            p.Rect(9, 27, 11, 29, ZBlood);
            // Head.
            p.Ellipse(16f, 54f, 6.5f, 7.5f, ZSkin);
            p.Rect(12, 46, 20, 48, ZSkinDark); // neck shadow
            p.Dot(13, 55, ZEye); p.Dot(14, 55, ZEye);
            p.Dot(18, 55, ZEye); p.Dot(19, 55, ZEye);
            if (pose == ZombiePose.Attack)
            {
                p.Rect(14, 49, 18, 51, ZBlood); // open mouth
                // Arms raised high for the swipe.
                p.Rect(3, 40, 7, 62, ZSkin);
                p.Rect(25, 40, 29, 62, ZSkin);
                p.Rect(3, 40, 7, 46, ZShirt);
                p.Rect(25, 40, 29, 46, ZShirt);
            }
            else
            {
                p.Rect(15, 50, 17, 50, ZSkinDark);
                // Arms reaching forward: foreshortened, hands in front of the chest.
                int sway = pose == ZombiePose.WalkA ? 1 : -1;
                p.Rect(5, 38 + sway, 9, 47, ZShirt);
                p.Rect(23, 38 - sway, 27, 47, ZShirt);
                p.Ellipse(9.5f, 38f + sway, 3.5f, 3f, ZSkin);
                p.Ellipse(22.5f, 38f - sway, 3.5f, 3f, ZSkin);
            }
            return p.ToTexture();
        }

        /// <summary>64x32 zombie lying on the ground.</summary>
        static Texture2D ZombieDeadTexture()
        {
            var p = new Pixels(64, 32);
            p.Ellipse(32f, 4f, 30f, 3.5f, ZBlood);  // pool
            p.Rect(6, 3, 30, 11, ZPants);            // legs
            p.Rect(28, 3, 50, 14, ZShirt);           // torso
            p.Ellipse(55f, 9f, 6.5f, 6f, ZSkin);     // head
            p.Rect(34, 14, 46, 17, ZSkin);           // arm across
            p.Dot(56, 10, ZOutline); p.Dot(58, 10, ZOutline);
            return p.ToTexture();
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

        // ---------------------------------------------------------------- high-res sprites (weapon sprites: WeaponSpriteBaker)

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
