using System;
using System.IO;
using Deadlock.View;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Skin assets for the scene builder: the skin sprite material (Deadlock/Skinned Sprite) and the skin catalog
    /// (Assets/Game/Data/Skins/SkinCatalog.asset). The catalog is created once with the standard look plus one test colour
    /// per target (provisional prototype values, not approved designs) and is then hand-edited data: later builds only
    /// refresh its material references and the equipment-screen images, and never overwrite the skins in it.
    /// Equipment-screen images (rifle side view, player front view) are shown whole: the displayed image is the
    /// alpha-cleaned derivative of the delivered original (the original is only read), with a region map from
    /// Tools/pixelart/make_image_regions.py; the SMG side view is the registered image itself, without a region map.
    /// Same import as the first-person art: Point, uncompressed, no mip maps, never
    /// downscaled; region maps as R8 data.
    /// </summary>
    public static class SkinSetup
    {
        public const string ShaderPath = "Assets/Game/Art/Skins/SkinnedSprite.shader";
        public const string MaterialPath = "Assets/Game/Art/Skins/Mat_SkinnedSprite.mat";
        public const string CatalogPath = "Assets/Game/Data/Skins/SkinCatalog.asset";
        public const string UiShaderPath = "Assets/Game/Art/Skins/SkinnedUI.shader";
        public const string UiMaterialPath = "Assets/Game/Art/Skins/Mat_SkinnedUI.mat";
        public const string RifleSidePath = "Assets/Game/Art/Weapons/Rifle/Side/rifle_side_design_opaque.png";
        public const string RifleSideRegionsPath = "Assets/Game/Art/Weapons/Rifle/Side/rifle_side_skin_regions.png";
        // SMG side view: the image registered by AssetPipeline (weapon.smg.side, 2026-10-30; the common weapon side-view
        // standard 1440 x 800, docs/WEAPON_SIDE_ASSET_SPEC.md), shown as delivered: no derivative, no region map (its
        // drawn colours).
        public const string SmgSidePath = "Assets/Game/Art/Weapons/SMG/Side/smg_side_design.png";
        // Player front view: the adopted glove colour (2026-10-21; a recolour of player_front_design.png inside the glove
        // mask, docs/review/equipment-art/glove-review). The delivered original and its first derivative are kept.
        public const string PlayerFrontPath = "Assets/Game/Art/Player/Front/player_front_gloves_adopted_opaque.png";
        public const string PlayerFrontRegionsPath = "Assets/Game/Art/Player/Front/player_front_gloves_adopted_skin_regions.png";
        const string RifleId = "proto_rifle";
        const string SmgId = SmgCandidateSetup.WeaponId;

        public static SkinCatalog Prepare()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new InvalidOperationException($"[DEADLOCK] Skin shader missing: {ShaderPath}");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
            if (catalog == null)
            {
                string folder = Path.GetDirectoryName(CatalogPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
                catalog = ScriptableObject.CreateInstance<SkinCatalog>();
                FillDefaults(catalog);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (catalog.material != material)
            {
                catalog.material = material;
                EditorUtility.SetDirty(catalog);
            }
            catalog.uiMaterial = LoadOrCreateMaterial(UiShaderPath, UiMaterialPath);
            catalog.playerFront = EquipmentSprite(PlayerFrontPath);
            catalog.playerFrontRegions = EquipmentRegions(PlayerFrontRegionsPath, catalog.playerFront);
            // The SMG candidate (development test choice): its own set with one test colour, next to the rifle. Added once
            // to the hand-edited catalog; its side view is set below.
            if (catalog.WeaponSet(SmgId) == null)
            {
                var list = new System.Collections.Generic.List<WeaponSkinSet>(catalog.weapons);
                int rifle = list.FindIndex(w => w != null && w.weaponId == RifleId);
                list.Insert(rifle + 1, new WeaponSkinSet
                {
                    weaponId = SmgId, label = "SMG（候補）",
                    skins = new[] { new WeaponSkin { skinId = "test_green", label = "色違い（試作）", colours = new[] { Colour(SkinRegion.WeaponBody, 95f, 0.22f) } } },
                });
                catalog.weapons = list.ToArray();
            }
            foreach (var set in catalog.weapons)
            {
                if (set != null && set.weaponId == RifleId)
                {
                    set.sideView = EquipmentSprite(RifleSidePath);
                    set.sideViewRegions = EquipmentRegions(RifleSideRegionsPath, set.sideView);
                }
                else if (set != null && set.weaponId == SmgId)
                {
                    set.sideView = EquipmentSprite(SmgSidePath);
                    set.sideViewRegions = null;
                }
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>Batch mode (-executeMethod): refreshes the catalog's images and materials only (no scene is rebuilt).</summary>
        public static void PrepareFromCommandLine() => Prepare();

        static Material LoadOrCreateMaterial(string shaderPath, string materialPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
                throw new InvalidOperationException($"[DEADLOCK] Skin shader missing: {shaderPath}");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>Equipment-screen image as a sprite (pivot centre); null with a warning when it is not there yet.</summary>
        static Sprite EquipmentSprite(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[DEADLOCK] Equipment-screen image missing: {path} (the screen shows its caption only)");
                return null;
            }
            AssetDatabase.ImportAsset(path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            PixelWeaponArt.Size(texture, out int width, out int height);
            PixelWeaponArt.Configure(path, new Vector2(0.5f, 0.5f), 100f, PixelWeaponArt.MaxSize(width, height));
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Texture2D EquipmentRegions(string path, Sprite image)
        {
            if (image == null || !File.Exists(path))
                return null;
            AssetDatabase.ImportAsset(path);
            var regions = PixelWeaponArt.ConfigureSkinRegions(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            if (regions.width != image.texture.width || regions.height != image.texture.height)
                throw new InvalidOperationException($"[DEADLOCK] {path} is {regions.width}x{regions.height} but its image is {image.texture.width}x{image.texture.height}");
            return regions;
        }

        /// <summary>First contents: one test colour for the rifle body and one for the hands / arms (prototype values).</summary>
        static void FillDefaults(SkinCatalog catalog)
        {
            catalog.weapons = new[]
            {
                new WeaponSkinSet
                {
                    weaponId = "proto_rifle", label = "ライフル",
                    skins = new[]
                    {
                        new WeaponSkin { skinId = "test_green", label = "色違い（試作）", colours = new[] { Colour(SkinRegion.WeaponBody, 95f, 0.22f) } },
                    },
                },
                // No weapon mask for the knife yet: standard only (its hand / arm still follows the player skin).
                new WeaponSkinSet { weaponId = "proto_knife", label = "ナイフ", skins = Array.Empty<WeaponSkin>() },
            };
            catalog.playerSkins = new[]
            {
                new PlayerSkin
                {
                    skinId = "test_navy", label = "色違い（試作）",
                    colours = new[]
                    {
                        Colour(SkinRegion.GloveBody, 215f, 0.18f),
                        Colour(SkinRegion.SleeveBase, 210f, 0.12f),
                        Colour(SkinRegion.SleeveCamo, 215f, 0.22f),
                    },
                },
            };
        }

        static SkinColour Colour(SkinRegion region, float hue, float saturation) =>
            new SkinColour { region = region, hue = hue, saturation = saturation };
    }
}
