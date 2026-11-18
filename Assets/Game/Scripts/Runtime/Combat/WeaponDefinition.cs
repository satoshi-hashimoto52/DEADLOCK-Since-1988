using Deadlock.View;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Static tuning data for one gun. Default values are provisional prototype values (see docs/PROGRESS.md); the
    /// confirmed values of the rifle and the SMG live in their assets (DECISIONS D-053). New fields keep the earlier
    /// behaviour at their defaults (one projectile per shot, no movement penalty, the switcher's own switch time).
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon Definition", fileName = "Weapon_")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string weaponId = "proto_rifle";
        public string displayName = "PROTO RIFLE";
        [Tooltip("First-person sprites and their motion. Presentation only.")]
        public WeaponViewProfile viewProfile;
        [Tooltip("Kind of gun: decides the loadout slot (Handgun: Slot 2 only; the others: Slot 1 only). D-052.")]
        public WeaponCategory category = WeaponCategory.Rifle;
        [Tooltip("Label only: the reserve is never shared between guns, even of the same type (D-054). Types are provisional.")]
        public AmmoType ammoType = AmmoType.Rifle;

        [Header("Damage / Fire")]
        [Min(0f)] public float damage = 25f;
        [Tooltip("Seconds between shots.")]
        [Min(0.01f)] public float fireInterval = 0.1f;
        public bool automatic = true;
        [Tooltip("Projectiles per trigger pull (shotgun: 8, D-053). Each one is aimed with its own spread and hits on its own; " +
                 "one round of ammunition is used per pull. Damage is per projectile.")]
        [Min(1)] public int projectilesPerShot = 1;

        [Header("Ammo")]
        [Min(1)] public int magazineSize = 30;
        [Tooltip("Two magazines (D-054, AmmoRules).")]
        [Min(0)] public int startReserve = 60;
        [Tooltip("Three magazines (D-054, AmmoRules).")]
        [Min(0)] public int reserveMax = 90;
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

        [Header("Handling (per weapon; 1 and 0 = earlier behaviour, values provisional)")]
        [Tooltip("Walking speed with this gun out (multiplies the ADS slowdown). 1: no change.")]
        [Range(0.1f, 1.5f)] public float moveSpeedMultiplier = 1f;
        [Tooltip("Spread while walking at full walking speed, as a multiple of the hip / ADS spread (scaled by speed). 1: no change.")]
        [Range(1f, 5f)] public float movingSpreadMultiplier = 1f;
        [Tooltip("Seconds of a weapon switch toward this gun. 0: the switcher's own time (0.35 s).")]
        [Min(0f)] public float switchTime = 0f;

        [Header("Recoil direction / damage falloff (optional; off and empty = earlier behaviour)")]
        [Tooltip("Data-driven recoil direction and strength. Disabled: recoilPitchDegrees with random left / right yaw.")]
        public RecoilProfile recoilProfile = new RecoilProfile();
        [Tooltip("Damage multiplier over distance (shot origin to hit point). Empty: full damage at every distance up to maxRange.")]
        public DamageFalloffProfile damageFalloff = new DamageFalloffProfile();
    }
}
