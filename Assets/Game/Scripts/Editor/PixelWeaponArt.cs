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
            ApplyApprovedHip(rifle);
            ApplyApprovedAds(rifle);
            EditorUtility.SetDirty(rifle);
            var melee = Derive(bakedMelee, KnifeProfilePath);
            melee.sprite = knife;
            EditorUtility.SetDirty(melee);
            AssetDatabase.SaveAssets();
            return (rifle, melee);
        }

        // ---- approved hip art (Assets/Game/Art/Weapons/Rifle) ---------------------------------------------------------

        public const string RifleFolder = Folder + "/Rifle";
        public const string ApprovedHipPath = RifleFolder + "/rifle_hip_approved.png";
        public const string ApprovedHipPlacementPath = Folder + "/Rifle/rifle_hip_approved.placement.json";
        static readonly float HalfHeight = Mathf.Tan(25f * Mathf.Deg2Rad); // view-model camera, z = 1

        [Serializable]
        class HipPlacement
        {
            public string image;              // image to import (e.g. the alpha-cleaned derivative); empty = the original
            public float[] muzzle;            // muzzle tip, pixels from the top-left (the flash sits here)
            public float[] hold;              // hold point (kick / reload turn around it), pixels from the top-left
            public float screenHeightPixels;  // how many image pixels make the full screen height (sets the size)
            public float anchorAspect;        // screen aspect at which the image's bottom-right = the screen's (0 = reference)
            public float[] offset;            // extra shift of the whole image in units (e.g. down: arms run further off the bottom)
            public float hipRightEdgeFollow;
            public float hipRightEdgeFollowNarrow;
            public float hipScaleAt4x3;
            public float hipMuzzleFlashScale;
            public float[] hipToAds;          // optional: hip move while raising the sights, offset in units (relative)
        }

        /// <summary>
        /// The approved hand-made hip image, when present, replaces the hip sprite. The image is never written by any tool;
        /// only its import settings are set. Size and place come from the placement file next to it: the image's
        /// bottom-right corner sits on the screen's bottom-right corner at the anchor aspect (arms / stock continue
        /// off the bottom and right edges; on narrower screens the right side runs off the screen instead of the gun
        /// moving toward the centre), the pivot is the muzzle (the flash follows it).
        /// </summary>
        static void ApplyApprovedHip(WeaponViewProfile rifle)
        {
            if (!File.Exists(ApprovedHipPlacementPath))
                return;
            var place = JsonUtility.FromJson<HipPlacement>(File.ReadAllText(ApprovedHipPlacementPath));
            string path = string.IsNullOrEmpty(place.image) ? ApprovedHipPath : $"{RifleFolder}/{place.image}";
            if (!File.Exists(path))
                return;
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            float ppu = place.screenHeightPixels / (2f * HalfHeight);
            var pivot = new Vector2(place.muzzle[0] / width, 1f - place.muzzle[1] / height);
            Configure(path, pivot, ppu, 2048);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                return;
            rifle.hipSprite = sprite;
            // hipMuzzlePosition is the 16:9 place; with no edge follow it is the place on every screen.
            float halfWidth = HalfHeight * (place.anchorAspect > 0f ? place.anchorAspect : rifle.referenceAspect);
            rifle.hipMuzzlePosition = new Vector2(halfWidth - (width - place.muzzle[0]) / ppu, -HalfHeight + (height - place.muzzle[1]) / ppu);
            if (place.offset != null && place.offset.Length == 2)
                rifle.hipMuzzlePosition += new Vector2(place.offset[0], place.offset[1]);
            rifle.hipHoldOffset = new Vector2((place.hold[0] - place.muzzle[0]) / ppu, (place.muzzle[1] - place.hold[1]) / ppu);
            rifle.hipRightEdgeFollow = place.hipRightEdgeFollow;
            rifle.hipRightEdgeFollowNarrow = place.hipRightEdgeFollowNarrow;
            rifle.hipScaleAt4x3 = place.hipScaleAt4x3;
            if (place.hipMuzzleFlashScale > 0f)
                rifle.hipMuzzleFlashScale = place.hipMuzzleFlashScale;
            if (place.hipToAds != null && place.hipToAds.Length == 2)
            {
                rifle.hipToAdsMuzzlePosition = new Vector2(place.hipToAds[0], place.hipToAds[1]);
                rifle.hipToAdsRelative = true;
            }
        }

        public const string ApprovedAdsPath = RifleFolder + "/rifle_ads_approved.png";
        public const string ApprovedAdsPlacementPath = Folder + "/Rifle/rifle_ads_approved.placement.json";

        [Serializable]
        class AdsPlacement
        {
            public string image;              // image to import (e.g. the alpha-cleaned derivative); empty = the original
            public float[] aim;               // front-sight tip = pivot = screen centre, pixels from the top-left
            public float[] muzzle;            // flash reference (the flash is drawn in front of the sprite)
            public float[] hold;              // optional: turn point for kick / reload, pixels from the top-left
            public float screenHeightPixels;  // image pixels per full screen height (same dot size as the hip)
            public float adsMuzzleFlashScale;
        }

        /// <summary>
        /// The ADS image made for the approved rifle (see docs/review/weapon-art-20261020/approved-rifle), when present,
        /// replaces the provisional ADS sprite. Like the hip image it is never written by a tool.
        /// </summary>
        static void ApplyApprovedAds(WeaponViewProfile rifle)
        {
            if (!File.Exists(ApprovedAdsPlacementPath))
                return;
            var place = JsonUtility.FromJson<AdsPlacement>(File.ReadAllText(ApprovedAdsPlacementPath));
            string path = string.IsNullOrEmpty(place.image) ? ApprovedAdsPath : $"{RifleFolder}/{place.image}";
            if (!File.Exists(path))
                return;
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            float ppu = place.screenHeightPixels / (2f * HalfHeight);
            Configure(path, new Vector2(place.aim[0] / width, 1f - place.aim[1] / height), ppu, 4096);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                return;
            rifle.adsSprite = sprite;
            rifle.adsMuzzleOffset = new Vector2((place.muzzle[0] - place.aim[0]) / ppu, (place.aim[1] - place.muzzle[1]) / ppu);
            if (place.hold != null && place.hold.Length == 2)
                rifle.adsHoldOffset = new Vector2((place.hold[0] - place.aim[0]) / ppu, (place.aim[1] - place.hold[1]) / ppu);
            if (place.adsMuzzleFlashScale > 0f)
                rifle.adsMuzzleFlashScale = place.adsMuzzleFlashScale;
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

        static void Configure(string path, Vector2 pivot, float pixelsPerUnit, int maxSize = 1024)
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
            importer.maxTextureSize = maxSize;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
    }
}
