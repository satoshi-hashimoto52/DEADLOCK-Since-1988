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
    /// refresh its material reference and never overwrite the skins in it.
    /// </summary>
    public static class SkinSetup
    {
        public const string ShaderPath = "Assets/Game/Art/Skins/SkinnedSprite.shader";
        public const string MaterialPath = "Assets/Game/Art/Skins/Mat_SkinnedSprite.mat";
        public const string CatalogPath = "Assets/Game/Data/Skins/SkinCatalog.asset";

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
            AssetDatabase.SaveAssets();
            return catalog;
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
