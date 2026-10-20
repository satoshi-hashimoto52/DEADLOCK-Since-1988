using System;
using System.IO;
using Deadlock.View;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Imports the authored pixel-art weapon sprites in Assets/Game/Art/Weapons (made by Tools/pixelart/draw_weapons.py)
    /// and makes the "_Pixel" view profiles that use them. Separate from <see cref="WeaponSpriteBaker"/>: the baker and
    /// the scene builder never write into Assets/Game/Art/Weapons, so re-baking cannot overwrite the art.
    /// Sprites: Point filter (crisp pixels), uncompressed, no mip maps, clamped, pivots / pixels-per-unit from
    /// pixel_weapons.json (a sprite entry may carry its own "pixelsPerUnit" for art drawn at another resolution). The _Pixel profiles copy every feel / position value from the baked profiles (one source of
    /// truth) and swap only the sprites; the art keeps the bake's pivots and scale, so muzzle / sight offsets still hold.
    /// When the art folder is missing the builder keeps the baked profiles.
    /// </summary>
    public static class PixelWeaponArt
    {
        public const string Folder = "Assets/Game/Art/Weapons";
        public const string RifleProfilePath = "Assets/Game/Data/WeaponView_ProtoRifle_Pixel.asset";
        public const string KnifeProfilePath = "Assets/Game/Data/MeleeView_ProtoKnife_Pixel.asset";
        const string HipName = "Spr_PixelRifleHip";
        const string AdsName = "Spr_PixelRifleAds";
        const string KnifeName = "Spr_PixelKnife";
        const string FlashName = "Spr_PixelMuzzleFlash";

        [Serializable]
        class SpriteInfo
        {
            public string name;
            public float[] size;
            public float[] pivot;
            [Tooltip("ADS only, optional: muzzle tip in pixels from the top-left; sets the flash position. Empty = the baked offset.")]
            public float[] muzzle;
            [Tooltip("Optional per-sprite pixels-per-unit (art drawn at another resolution); 0 = the shared value.")]
            public float pixelsPerUnit;
        }

        [Serializable]
        class ArtInfo
        {
            public SpriteInfo[] sprites;
            public float pixelsPerUnit;
            public float flashPixelsPerUnit;
        }

        /// <summary>Imports the art and returns the pixel profiles, or the baked ones when the art is missing.</summary>
        public static (WeaponViewProfile, MeleeViewProfile) Prepare(WeaponViewProfile baked, MeleeViewProfile bakedMelee)
        {
            string jsonPath = $"{Folder}/pixel_weapons.json";
            if (!File.Exists(jsonPath))
                return (baked, bakedMelee);
            var info = JsonUtility.FromJson<ArtInfo>(File.ReadAllText(jsonPath));
            foreach (var sprite in info.sprites)
            {
                float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit
                    : sprite.name == FlashName ? info.flashPixelsPerUnit : info.pixelsPerUnit;
                Configure($"{Folder}/{sprite.name}.png", new Vector2(sprite.pivot[0], sprite.pivot[1]), ppu);
            }
            Sprite hip = Load(HipName), ads = Load(AdsName), knife = Load(KnifeName), flash = Load(FlashName);
            if (hip == null || ads == null || knife == null)
                return (baked, bakedMelee);

            var rifle = Derive(baked, RifleProfilePath);
            rifle.hipSprite = hip;
            rifle.adsSprite = ads;
            var adsInfo = System.Array.Find(info.sprites, s => s.name == AdsName);
            if (adsInfo?.muzzle != null && adsInfo.muzzle.Length == 2 && adsInfo.size != null && adsInfo.size.Length == 2)
            {
                // Muzzle relative to the ADS pivot, in units (y up).
                float ppu = adsInfo.pixelsPerUnit > 0f ? adsInfo.pixelsPerUnit : info.pixelsPerUnit;
                rifle.adsMuzzleOffset = new Vector2(
                    (adsInfo.muzzle[0] - adsInfo.pivot[0] * adsInfo.size[0]) / ppu,
                    ((1f - adsInfo.pivot[1]) * adsInfo.size[1] - adsInfo.muzzle[1]) / ppu);
            }
            rifle.muzzleFlashSprite = flash;
            EditorUtility.SetDirty(rifle);
            var melee = Derive(bakedMelee, KnifeProfilePath);
            melee.sprite = knife;
            EditorUtility.SetDirty(melee);
            AssetDatabase.SaveAssets();
            return (rifle, melee);
        }

        static T Derive<T>(T source, string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(asset, path);
            }
            else
                EditorUtility.CopySerialized(source, asset);
            asset.name = Path.GetFileNameWithoutExtension(path);
            return asset;
        }

        static Sprite Load(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{name}.png");
            if (sprite == null)
                Debug.LogWarning($"[DEADLOCK] Pixel weapon art missing: {Folder}/{name}.png (baked sprites are used instead)");
            return sprite;
        }

        static void Configure(string path, Vector2 pivot, float pixelsPerUnit)
        {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spritePixelsPerUnit = pixelsPerUnit;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
    }
}
