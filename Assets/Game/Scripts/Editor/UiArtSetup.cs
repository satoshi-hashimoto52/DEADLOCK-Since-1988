using System.IO;
using Deadlock.UI;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Import settings and references for the UI art in Assets/Game/Art/UI (control icons, title logo, app icon).
    /// Run by the scene builder; replacing a PNG while keeping its .meta keeps every reference.
    /// Control icons are smooth white pictograms on transparency (512x512), shown at roughly 60-300 px: imported as
    /// UI sprites, bilinear (not the Point filter used for the pixel-art world), uncompressed (no block artefacts on
    /// the white edges), no mip maps, clamped, alpha-is-transparency (no light fringes when filtered), max 256 px
    /// (keeps downscaling to about 2x or less for the usual sizes, so outlines stay clean without mip maps).
    /// </summary>
    public static class UiArtSetup
    {
        public const string ControlsFolder = "Assets/Game/Art/UI/Controls";
        public const string LogoPath = "Assets/Game/Art/UI/Branding/deadlock_title_logo.png";
        public const string AppIconPath = "Assets/Game/Art/UI/Branding/deadlock_app_icon.png";
        public const string IconSetPath = "Assets/Game/Data/UI_ControlIcons.asset";

        public static ControlIconSet Prepare()
        {
            foreach (var path in Directory.GetFiles(ControlsFolder, "*.png"))
                ConfigureSprite(path.Replace('\\', '/'), 256);
            if (File.Exists(LogoPath))
                ConfigureSprite(LogoPath, 1024);
            if (File.Exists(AppIconPath))
                ConfigureAppIcon(AppIconPath);

            var set = AssetDatabase.LoadAssetAtPath<ControlIconSet>(IconSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ControlIconSet>();
                AssetDatabase.CreateAsset(set, IconSetPath);
            }
            set.fire = Icon("icon_fire");
            set.melee = Icon("icon_melee");
            set.ads = Icon("icon_ads");
            set.reload = Icon("icon_reload");
            set.jump = Icon("icon_jump");
            set.stand = Icon("icon_stand");
            set.crouch = Icon("icon_crouch");
            set.prone = Icon("icon_prone");
            set.slide = Icon("icon_slide");
            set.weaponSwap = Icon("icon_weapon_swap");
            set.menu = Icon("icon_menu");
            set.settings = Icon("icon_settings");
            set.titleLogo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
            return set;
        }

        static Sprite Icon(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ControlsFolder}/{name}.png");
            if (sprite == null)
                Debug.LogWarning($"[DEADLOCK] UI icon missing: {ControlsFolder}/{name}.png (text / plain button is used instead)");
            return sprite;
        }

        static void ConfigureSprite(string path, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maxSize;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }

        static void ConfigureAppIcon(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.isReadable = true; // the iOS icon pipeline scales it to every size
            importer.SaveAndReimport();
        }
    }
}
