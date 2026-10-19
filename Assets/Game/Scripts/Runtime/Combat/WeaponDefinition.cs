using Deadlock.View;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Static tuning data for one gun. All default values are provisional prototype values (see docs/PROGRESS.md),
    /// not confirmed game balance.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon Definition", fileName = "Weapon_")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "proto_rifle";
        public string displayName = "PROTO RIFLE";
        [Tooltip("First-person sprites and their motion. Presentation only.")]
        public WeaponViewProfile viewProfile;

        [Header("Damage / Fire")]
        [Min(0f)] public float damage = 25f;
        [Tooltip("Seconds between shots.")]
        [Min(0.01f)] public float fireInterval = 0.1f;
        public bool automatic = true;

        [Header("Ammo")]
        [Min(1)] public int magazineSize = 30;
        [Min(0)] public int startReserve = 90;
        [Min(0)] public int reserveMax = 180;
        [Min(0.05f)] public float reloadTime = 1.8f;

        [Header("Ballistics")]
        [Tooltip("Projectile speed in m/s. Hits are resolved while the projectile flies, not at the moment of firing.")]
        [Min(1f)] public float projectileSpeed = 250f;
        [Min(1f)] public float maxRange = 200f;

        [Header("Accuracy / Recoil")]
        [Min(0f)] public float hipSpreadDegrees = 1.6f;
        [Min(0f)] public float adsSpreadDegrees = 0.15f;
        [Min(0f)] public float recoilPitchDegrees = 0.7f;
        [Min(0f)] public float recoilYawDegrees = 0.25f;
        [Range(0f, 1f)] public float adsRecoilMultiplier = 0.55f;

        [Header("ADS")]
        [Range(30f, 110f)] public float hipFov = 75f;
        [Range(20f, 110f)] public float adsFov = 55f;
        [Tooltip("Seconds to go from hip to fully aimed.")]
        [Min(0.01f)] public float adsTime = 0.18f;
        [Range(0.1f, 1f)] public float adsMoveSpeedMultiplier = 0.6f;
        [Range(0.1f, 1f)] public float adsLookSensitivityMultiplier = 0.65f;
    }
}
