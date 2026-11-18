using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Combat;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Stage 38: the first-person art of the new guns (handgun, shotgun, LMG, EPIC SMG; <see cref="WeaponCatalogSetup.PlannedArts"/>).
    /// For each gun whose approved images AssetPipeline has registered at the planned paths, one
    /// <see cref="WeaponArtDefinition"/> (Assets/Game/Data/WeaponArt/WeaponArt_&lt;name&gt;.asset) names them — the hip and
    /// ADS originals, the image the game shows (a grid-restored derivative when one is there, else the original itself),
    /// the hand-written placement files, and the EPIC SMG's emission cores and glows (hip and ADS) — and is listed in the registry's
    /// adopted weapons. Nothing is made up: a view whose image is not registered stays empty (a missing hip is marked
    /// <see cref="WeaponArtDefinition.hipNotDelivered"/> / <see cref="WeaponArtDefinition.adsNotDelivered"/>, so the
    /// game draws no image of another gun there), and a gun with neither image gets no definition. The rifle's motion values (base profile) and muzzle flash
    /// are shared. Images and placement files are only read; then <see cref="PixelWeaponArt.ImportRegistered"/>,
    /// <see cref="SkinSetup.Prepare"/> (side views) and the catalog run as usual.
    /// Batch: -batchmode -quit -executeMethod Deadlock.Editor.WeaponArtSetup.PrepareFromBatch
    /// </summary>
    public static class WeaponArtSetup
    {
        const string RifleArtPath = "Assets/Game/Data/WeaponArt/WeaponArt_ProtoRifle.asset";
        const string DataFolder = "Assets/Game/Data/WeaponArt";

        /// <summary>The definition asset of a planned gun (e.g. WeaponArt_ProtoShotgun.asset).</summary>
        public static string DefinitionPath(WeaponCatalogSetup.PlannedArt plan) =>
            $"{DataFolder}/WeaponArt_{Path.GetFileNameWithoutExtension(plan.DefinitionPath).Replace("Weapon_", "").Replace("_Provisional", "")}.asset";

        public static string PlacementPath(string imagePath) => Path.ChangeExtension(imagePath, null) + ".placement.json";

        /// <summary>A derivative made from the registered original with the 4 x 4 grid restored (Tools/pixelart/restore_grid.py).</summary>
        public static string GridPath(string imagePath) => Path.ChangeExtension(imagePath, null) + "_grid.png";

        public static string EmissionPath(WeaponCatalogSetup.PlannedArt plan) => Path.ChangeExtension(plan.HipPath, null) + "_emission.png";
        public static string GlowPath(WeaponCatalogSetup.PlannedArt plan) => Path.ChangeExtension(plan.HipPath, null) + "_glow.png";
        public static string AdsEmissionPath(WeaponCatalogSetup.PlannedArt plan) => Path.ChangeExtension(plan.AdsPath, null) + "_emission.png";
        public static string AdsGlowPath(WeaponCatalogSetup.PlannedArt plan) => Path.ChangeExtension(plan.AdsPath, null) + "_glow.png";

        [MenuItem("DEADLOCK/Dev/Prepare Weapon Art (Stage 38)")]
        public static void PrepareFromMenu() => PrepareFromBatch();

        public static void PrepareFromBatch()
        {
            var prepared = Prepare();
            PixelWeaponArt.ImportRegistered();
            SkinSetup.Prepare();
            WeaponCatalogSetup.PrepareCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("[DEADLOCK] weapon art prepared (Stage 38): " + (prepared.Count == 0 ? "none registered yet" : string.Join(", ", prepared)));
        }

        /// <summary>Writes the definitions of the planned guns that have registered images and lists them; returns their ids.</summary>
        public static List<string> Prepare()
        {
            var rifleArt = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(RifleArtPath);
            if (rifleArt == null)
                throw new InvalidOperationException($"[DEADLOCK] weapon art needs {RifleArtPath}");
            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            if (registry == null)
                throw new InvalidOperationException($"[DEADLOCK] weapon art needs {PixelWeaponArt.RegistryPath}");
            var done = new List<string>();
            foreach (var plan in WeaponCatalogSetup.PlannedArts)
            {
                bool hip = File.Exists(plan.HipPath), ads = File.Exists(plan.AdsPath);
                if (!hip && !ads)
                    continue;
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(plan.DefinitionPath);
                if (weapon == null)
                    throw new InvalidOperationException($"[DEADLOCK] weapon art of {plan.WeaponId} needs {plan.DefinitionPath}");
                string path = DefinitionPath(plan);
                var art = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(path);
                if (art == null)
                {
                    art = ScriptableObject.CreateInstance<WeaponArtDefinition>();
                    AssetDatabase.CreateAsset(art, path);
                }
                art.weapon = weapon;
                art.baseProfile = rifleArt.baseProfile;
                art.muzzleFlash = rifleArt.muzzleFlash;
                art.hipNotDelivered = !hip;
                art.adsNotDelivered = !ads;
                art.hip = hip ? View(plan.HipPath) : new WeaponArtDefinition.ViewArt();
                art.ads = ads ? View(plan.AdsPath) : new WeaponArtDefinition.ViewArt();
                art.hipEmission = hip ? Optional<Texture2D>(EmissionPath(plan)) : null;
                art.hipGlow = hip ? Optional<Texture2D>(GlowPath(plan)) : null;
                art.adsEmission = ads ? Optional<Texture2D>(AdsEmissionPath(plan)) : null;
                art.adsGlow = ads ? Optional<Texture2D>(AdsGlowPath(plan)) : null;
                EditorUtility.SetDirty(art);
                if (!(registry.weapons ?? Array.Empty<WeaponArtDefinition>()).Contains(art))
                {
                    registry.weapons = (registry.weapons ?? Array.Empty<WeaponArtDefinition>()).Append(art).ToArray();
                    EditorUtility.SetDirty(registry);
                }
                done.Add($"{plan.WeaponId}(hip {(hip ? "yes" : "not delivered")}, ads {(ads ? "yes" : "not delivered")}" +
                         $"{(art.hipEmission != null ? ", emission" : "")}{(art.hipGlow != null ? ", glow" : "")}" +
                         $"{(art.adsEmission != null ? ", ADS emission" : "")}{(art.adsGlow != null ? ", ADS glow" : "")})");
            }
            AssetDatabase.SaveAssets();
            return done;
        }

        /// <summary>
        /// One view of a gun: the registered original (source), the image shown (its grid-restored derivative when there
        /// is one), the hand-written placement file next to it. A missing placement file is left empty: the import then
        /// names it instead of guessing one.
        /// </summary>
        static WeaponArtDefinition.ViewArt View(string imagePath)
        {
            var source = Load<Texture2D>(imagePath);
            var grid = Optional<Texture2D>(GridPath(imagePath));
            return new WeaponArtDefinition.ViewArt
            {
                source = source,
                image = grid != null ? grid : source,
                placement = Optional<TextAsset>(PlacementPath(imagePath)),
            };
        }

        static T Optional<T>(string path) where T : UnityEngine.Object
        {
            if (!File.Exists(path))
                return null;
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = Optional<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"[DEADLOCK] weapon art file missing: {path}");
            return asset;
        }
    }
}
