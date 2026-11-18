using System;
using System.Linq;
using Deadlock.Combat;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// The SMG candidate for the development test choice (art not adopted): its weapon definition, its first-person art
    /// definition (hip candidate v2, ADS comparison candidate gap3, the SMG-only review placements, the region maps)
    /// and its place in the registry's candidates. Performance: the confirmed SMG values (D-053: 17 damage, 900 RPM,
    /// 35 rounds; reserve two / three magazines, D-054) are written on every build. Every other value (spread, recoil,
    /// reload time, ADS, ...) is provisional: copied from the rifle only when the asset is first created, and from then on
    /// left as tuned — a rebuild never puts the rifle's values back. The rifle's own definition is only read.
    /// </summary>
    public static class SmgCandidateSetup
    {
        public const string WeaponId = "proto_smg";
        public const string WeaponPath = "Assets/Game/Data/Weapon_ProtoSMG_Candidate.asset";
        public const string ArtPath = "Assets/Game/Data/WeaponArt/WeaponArt_ProtoSMG_Candidate.asset";
        const string RiflePath = "Assets/Game/Data/Weapon_ProtoRifle.asset";
        const string RifleArtPath = "Assets/Game/Data/WeaponArt/WeaponArt_ProtoRifle.asset";
        const string Smg = "Assets/Game/Art/Weapons/SMG";

        public const float Damage = 17f;
        public const float Rpm = 900f;
        public const int MagazineSize = 35;

        public static WeaponDefinition Prepare()
        {
            var rifleArt = AssetDatabase.LoadAssetAtPath<WeaponArtDefinition>(RifleArtPath);
            if (rifleArt == null)
                throw new InvalidOperationException($"[DEADLOCK] SMG candidate needs {RifleArtPath}");
            var smg = PreparePerformance();

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

        /// <summary>
        /// The SMG's weapon definition: created from a copy of the rifle when missing, then the confirmed values written.
        /// Values not confirmed are kept as they are in the asset.
        /// </summary>
        public static WeaponDefinition PreparePerformance()
        {
            var smg = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (smg == null)
            {
                var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RiflePath);
                if (rifle == null)
                    throw new InvalidOperationException($"[DEADLOCK] SMG candidate needs {RiflePath}");
                smg = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(smg, WeaponPath);
                // The starting point of the values not confirmed; the view profile comes from the art import.
                EditorUtility.CopySerialized(rifle, smg);
                smg.name = System.IO.Path.GetFileNameWithoutExtension(WeaponPath);
                smg.viewProfile = null;
            }
            ApplyConfirmedValues(smg);
            EditorUtility.SetDirty(smg);
            return smg;
        }

        /// <summary>Writes the SMG's identity and confirmed values (D-053, D-054) and nothing else.</summary>
        public static void ApplyConfirmedValues(WeaponDefinition smg)
        {
            smg.weaponId = WeaponId;
            smg.displayName = "SMG CANDIDATE";
            smg.category = WeaponCategory.Smg;
            smg.ammoType = AmmoType.Pistol; // label only, provisional (Q-012)
            smg.damage = Damage;
            smg.fireInterval = 60f / Rpm;
            smg.automatic = true;
            smg.projectilesPerShot = 1;
            smg.magazineSize = MagazineSize;
            AmmoRules.Apply(smg);
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
