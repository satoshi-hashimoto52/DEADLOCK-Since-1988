using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Placeholder art of the ruins test field, generated procedurally like <see cref="PlaceholderArtGenerator"/>:
    /// coarse 32 px world textures (2 m per tile, point filtered) of old black-grey stone. Simple on purpose: the test
    /// field checks structure and play first; finished ruin art comes later.
    /// </summary>
    public static class RuinsArtGenerator
    {
        public const string Folder = "Assets/Game/Art/Generated/Ruins";

        public static string Wall => $"{Folder}/Tex_RuinWall.png";
        public static string Floor => $"{Folder}/Tex_RuinFloor.png";
        public static string Pillar => $"{Folder}/Tex_RuinPillar.png";
        public static string Frame => $"{Folder}/Tex_RuinFrame.png";

        public static void GenerateAll()
        {
            Directory.CreateDirectory(Folder);
            Save(Wall, WallTexture());
            Save(Floor, FloorTexture());
            Save(Pillar, PillarTexture());
            Save(Frame, FrameTexture());
        }

        static void Save(string path, Texture2D texture)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Color32 Vary(System.Random rng, Color32 c, int amount)
        {
            int d = rng.Next(-amount, amount + 1);
            return new Color32(Clamp(c.r + d), Clamp(c.g + d), Clamp(c.b + d + d / 3), 255);
        }

        static byte Clamp(int v) => (byte)Mathf.Clamp(v, 0, 255);

        static Color32 Shade(Color32 c, float f) => new Color32(Clamp((int)(c.r * f)), Clamp((int)(c.g * f)), Clamp((int)(c.b * f)), 255);

        /// <summary>Uneven courses of dark stone blocks, deep joints, a few chipped corners and cracks.</summary>
        static Texture2D WallTexture()
        {
            const int size = 32;
            var rng = new System.Random(71);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var stone = new Color32(68, 68, 77, 255); // was 58, 58, 66
            var joint = new Color32(20, 20, 26, 255);
            int[] courses = { 0, 7, 15, 24, 32 };
            for (int c = 0; c < courses.Length - 1; c++)
            {
                int y0 = courses[c], y1 = courses[c + 1];
                int offset = rng.Next(0, 12);
                int x = -offset;
                while (x < size)
                {
                    int w = rng.Next(9, 15);
                    var block = Vary(rng, stone, 8);
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = Mathf.Max(0, x); xx < Mathf.Min(size, x + w); xx++)
                        {
                            bool edge = yy == y0 || xx == x || xx == Mathf.Max(0, x);
                            var col = edge ? joint : Vary(rng, yy == y1 - 1 ? Shade(block, 0.8f) : block, 5);
                            tex.SetPixel(xx, size - 1 - yy, col);
                        }
                    // chipped corner
                    if (rng.Next(3) == 0 && x + 1 >= 0 && x + 2 < size)
                    {
                        tex.SetPixel(x + 1, size - 1 - (y0 + 1), joint);
                        tex.SetPixel(x + 2, size - 1 - (y0 + 1), Shade(block, 0.6f));
                    }
                    x += w;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                int cx = rng.Next(2, size - 2), cy = rng.Next(2, size - 2);
                for (int s = 0; s < 5; s++)
                {
                    tex.SetPixel(cx, cy, Shade(stone, 0.45f));
                    cx = Mathf.Clamp(cx + rng.Next(-1, 2), 0, size - 1);
                    cy = Mathf.Clamp(cy + 1, 0, size - 1);
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Large worn flagstones (2 x 2 per tile), dark joints, lighter worn centres.</summary>
        static Texture2D FloorTexture()
        {
            const int size = 32;
            var rng = new System.Random(72);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var stone = new Color32(64, 63, 71, 255); // was 52, 51, 58
            var joint = new Color32(18, 18, 22, 255);
            for (int by = 0; by < 2; by++)
                for (int bx = 0; bx < 2; bx++)
                {
                    var block = Vary(rng, stone, 7);
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            int px = bx * 16 + x, py = by * 16 + y;
                            bool edge = x == 0 || y == 0;
                            float wear = 1f - (Mathf.Abs(x - 8f) + Mathf.Abs(y - 8f)) / 16f;
                            var col = edge ? joint : Vary(rng, Shade(block, 0.92f + 0.16f * wear), 6);
                            tex.SetPixel(px, py, col);
                        }
                }
            for (int i = 0; i < 2; i++)
            {
                int cx = rng.Next(2, size - 2), cy = rng.Next(2, size - 2);
                for (int s = 0; s < 6; s++)
                {
                    tex.SetPixel(cx, cy, joint);
                    cx = Mathf.Clamp(cx + 1, 0, size - 1);
                    cy = Mathf.Clamp(cy + rng.Next(-1, 2), 0, size - 1);
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Worn pillar stone: vertical grain, rounded wear marks, slightly lighter than the walls.</summary>
        static Texture2D PillarTexture()
        {
            const int size = 32;
            var rng = new System.Random(73);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var stone = new Color32(80, 79, 89, 255); // was 72, 71, 80
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float grain = (x % 6 == 0) ? 0.82f : 1f;
                    tex.SetPixel(x, y, Vary(rng, Shade(stone, grain), 6));
                }
            // horizontal drum joints and worn pits
            foreach (int y in new[] { 0, 15 })
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color32(26, 26, 32, 255));
            for (int i = 0; i < 6; i++)
            {
                int cx = rng.Next(1, size - 1), cy = rng.Next(1, size - 1);
                tex.SetPixel(cx, cy, Shade(stone, 0.5f));
                tex.SetPixel(cx + 1 < size ? cx + 1 : cx, cy, Shade(stone, 0.65f));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Lighter dressed stone for door frames, stair openings, step nosings and the parapet coping: smooth blocks with
        /// thin joints, clearly lighter than the walls so openings and edges read in the dark without glowing.
        /// </summary>
        static Texture2D FrameTexture()
        {
            const int size = 32;
            var rng = new System.Random(74);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var stone = new Color32(112, 108, 114, 255);
            var joint = new Color32(54, 52, 58, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = y % 16 == 0 || (x + (y / 16) * 8) % 16 == 0;
                    tex.SetPixel(x, y, edge ? joint : Vary(rng, stone, 5));
                }
            for (int i = 0; i < 4; i++)
                tex.SetPixel(rng.Next(1, size - 1), rng.Next(1, size - 1), Shade(stone, 0.7f));
            tex.Apply();
            return tex;
        }
    }
}
