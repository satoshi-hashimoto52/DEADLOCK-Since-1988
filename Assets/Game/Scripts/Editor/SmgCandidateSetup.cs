using System;
using System.Linq;
using Deadlock.Combat;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// The SMG candidate for the development test choice (not adopted): its weapon definition, its first-person art
    /// definition (hip candidate v2, ADS comparison candidate gap3, the SMG-only review placements, the region maps)
    /// and its place in the registry's candidates. Performance values: the rifle's, copied again on every build and named
    /// as such — for checking the display only, not an SMG balance decision. The rifle's own definition is only read.
    /// </summary>
    public static class SmgCandidateSetup
    {
        public const string WeaponId = "proto_smg";
        public const string WeaponPath = "Assets/Game/Data/Weapon_ProtoSMG_Candidate.asset";
        public const string ArtPath = "Assets/Game/Data/WeaponArt/WeaponArt_ProtoSMG_Candidate.asset";
        const string RiflePath = "Assets/Game/Data/Weapon_ProtoRifle.asset";
        const string RifleArtPath = "Assets/Game/Data/WeaponArt/WeaponArt_ProtoRifle.asset";
        const string Smg = "Assets/Game/Art/Weapons/SMG";

        public static WeaponDefinition Prepare()
        {
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RiflePath);
            var rifleArt = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(RifleArtPath);
            if (rifle == null || rifleArt == null)
                throw new InvalidOperationException($"[DEADLOCK] SMG candidate needs {RiflePath} and {RifleArtPath}");

            var smg = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (smg == null)
            {
                smg = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(smg, WeaponPath);
            }
            // Rifle-equivalent performance (every value copied), own identity; the view profile comes from the art import.
            var view = smg.viewProfile;
            EditorUtility.CopySerialized(rifle, smg);
            smg.name = System.IO.Path.GetFileNameWithoutExtension(WeaponPath);
            smg.weaponId = WeaponId;
            smg.displayName = "SMG CANDIDATE (RIFLE VALUES)";
            smg.viewProfile = view;
            EditorUtility.SetDirty(smg);

            var art = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(ArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<WeaponArtDefinition>();
                AssetDatabase.CreateAsset(art, ArtPath);
            }
            art.weapon = smg;
            art.baseProfile = rifleArt.baseProfile;
            art.muzzleFlash = rifleArt.muzzleFlash;
            art.hip.source = Load<Texture2D>($"{Smg}/smg_hip_candidate_v2.png");
            art.hip.image = Load<Texture2D>($"{Smg}/smg_hip_candidate_v2_opaque.png");
            art.hip.placement = Load<TextAsset>($"{Smg}/smg_hip_candidate_v2.placement.json");
            art.hip.skinRegions = Load<Texture2D>($"{Smg}/Regions/Hip/smg_hip_skin_regions.png");
            // The ADS comparison candidate's alpha is already binary: it is shown as is (no derivative).
            art.ads.source = Load<Texture2D>($"{Smg}/smg_ads_front_sight_gap3.png");
            art.ads.image = Load<Texture2D>($"{Smg}/smg_ads_front_sight_gap3.png");
            art.ads.placement = Load<TextAsset>($"{Smg}/smg_ads_front_sight_gap3.placement.json");
            art.ads.skinRegions = Load<Texture2D>($"{Smg}/Regions/ADS/smg_ads_skin_regions.png");
            EditorUtility.SetDirty(art);

            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            if (registry != null && !(registry.candidates ?? Array.Empty<WeaponArtDefinition>()).Contains(art))
            {
                registry.candidates = (registry.candidates ?? Array.Empty<WeaponArtDefinition>()).Append(art).ToArray();
                EditorUtility.SetDirty(registry);
            }
            AssetDatabase.SaveAssets();
            return smg;
        }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"[DEADLOCK] SMG candidate file missing: {path}");
            return asset;
        }
    }
}
