using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Presentation data for one weapon's first-person sprites: sprites, where they sit on screen and how they move.
    /// Positions are in view-model camera space at z = 1 (vertical FOV 50°): the screen spans ±tan(25°) ≈ ±0.466 units
    /// vertically and ±0.466 × aspect horizontally. Purely visual: no combat values live here.
    /// The sprite/pivot fields are written by WeaponSpriteBaker; the feel values are hand-tuned.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon View Profile", fileName = "WeaponView_")]
    public sealed class WeaponViewProfile : ScriptableObject
    {
        [Header("Sprites (baked)")]
        [Tooltip("Hip sprite. Its pivot is the muzzle tip.")]
        public Sprite hipSprite;
        [Tooltip("ADS sprite. Its pivot is the front-sight tip, which sits on the screen centre (= aim point).")]
        public Sprite adsSprite;
        [Tooltip("Muzzle tip relative to the ADS sprite pivot.")]
        public Vector2 adsMuzzleOffset;
        [Tooltip("Muzzle flash sprite (optional; empty = the flash renderers keep the sprite the scene was built with).")]
        public Sprite muzzleFlashSprite;

        [Header("Skins (imported)")]
        [Tooltip("Region map of the hip image (same canvas, Tools/pixelart/make_skin_regions.py). Empty = the hip image keeps its drawn colours.")]
        public Texture2D hipSkinRegions;
        [Tooltip("Region map of the ADS image. Empty = the ADS image keeps its drawn colours.")]
        public Texture2D adsSkinRegions;

        [Header("Hip pose (baked for 16:9)")]
        [Tooltip("Where the hip sprite pivot (muzzle) sits for a 16:9 screen.")]
        public Vector2 hipMuzzlePosition = new Vector2(0.06f, -0.12f);
        public float referenceAspect = 16f / 9f;
        [Tooltip("How much the hip weapon follows the right screen edge when the screen is wider than the reference (0 = stays, 1 = glued to edge).")]
        [Range(0f, 1f)] public float hipRightEdgeFollow = 0.3f;
        [Tooltip("The same for screens narrower than the reference (iPad 4:3). 0 = keep the 16:9 place and let the right side run off the screen.")]
        [Range(0f, 1f)] public float hipRightEdgeFollowNarrow = 0.3f;
        [Tooltip("Hip sprite scale at 4:3 relative to the reference aspect (keeps the centre clear on iPad).")]
        [Range(0.5f, 1f)] public float hipScaleAt4x3 = 0.92f;

        [Header("Hip → ADS transition")]
        [Tooltip("Where the hip muzzle travels to while raising the sights.")]
        public Vector2 hipToAdsMuzzlePosition = new Vector2(0.0f, -0.05f);
        [Tooltip("On: hipToAdsMuzzlePosition is an offset from the hip place (e.g. straight down), so a large hip image keeps its edges off the screen while raising the sights.")]
        public bool hipToAdsRelative;
        [Tooltip("ADS sprite start offset (below its final place).")]
        public Vector2 adsStartOffset = new Vector2(0f, -0.10f);
        [Range(0f, 1f)] public float crossfadeStart = 0.45f;
        [Range(0f, 1f)] public float crossfadeEnd = 0.62f;

        [Header("Feel")]
        public float bobAmount = 0.006f;
        [Tooltip("Extra hand bob in step with the camera while sprinting (CameraBob): lowest point and side sway, view units. 0 = none.")]
        public Vector2 sprintBob = Vector2.zero;
        public float bobFrequency = 1.8f;
        [Tooltip("Fire kick: the weapon is pushed down/back toward the player.")]
        public Vector2 kickOffset = new Vector2(0.008f, -0.022f);
        public float kickRollDegrees = -2.5f;
        public float kickScale = 0.03f;
        public float kickRecoverSpeed = 12f;
        [Range(0f, 1f)] public float adsKickMultiplier = 0.35f;
        public float muzzleFlashTime = 0.045f;
        public float hipMuzzleFlashScale = 0.32f;
        public float adsMuzzleFlashScale = 0.35f;
        [Tooltip("Reload: lowered and rolled over the actual reload time.")]
        public float reloadDrop = 0.10f;
        public float reloadRollDegrees = 14f;
        [Tooltip("Point the weapon turns around (reload roll, fire kick), relative to each sprite's pivot: roughly the hands.")]
        public Vector2 hipHoldOffset = new Vector2(0.22f, -0.2f);
        public Vector2 adsHoldOffset = new Vector2(0f, -0.3f);
    }
}
