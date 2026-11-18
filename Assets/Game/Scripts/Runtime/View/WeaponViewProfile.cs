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
        [Tooltip("ADS sprite. Its pivot is the aim point (the front-sight tip, or an optical sight's dot), which sits on the screen centre.")]
        public Sprite adsSprite;
        [Tooltip("Muzzle tip relative to the ADS sprite pivot.")]
        public Vector2 adsMuzzleOffset;
        [Tooltip("Muzzle flash sprite (optional; empty = the flash renderers keep the sprite the scene was built with).")]
        public Sprite muzzleFlashSprite;

        [Header("Glow layers of the hip image (imported, optional)")]
        [Tooltip("Emission core drawn over the hip image: same canvas, pivot and size as the hip sprite (it moves with it). Empty = none.")]
        public Sprite hipEmissionSprite;
        [Tooltip("Soft glow around the emission core (may be semi-transparent): same canvas, pivot and size as the hip sprite. Empty = none.")]
        public Sprite hipGlowSprite;
        [Tooltip("Brightness of the emission core (its opacity over the hip image). 0 = the plain gun.")]
        [Range(0f, 1f)] public float emissionIntensity = 1f;
        [Tooltip("Brightness of the glow (its opacity). 0 = off; the gun and its core are drawn as without it.")]
        [Range(0f, 1f)] public float glowIntensity = 1f;
        [Tooltip("Material of the glow layer, e.g. additive (Deadlock/Sprite Additive Glow): the glow lightens what is under it instead of being painted over it. Empty = the hip image's material (normal blend).")]
        public Material glowMaterial;
        [Tooltip("Slow pulse (breathing) of the glow: its brightness dips by up to this share and comes back. 0 = steady.")]
        [Range(0f, 1f)] public float glowPulse;
        [Tooltip("Breathing of the emission core, with the glow (same length and phase): its brightness dips by up to this share and comes back. 0 = steady.")]
        [Range(0f, 1f)] public float emissionPulse;
        [Tooltip("Length of one pulse of the glow and the emission core (seconds).")]
        public float glowPulseSeconds = 2.4f;

        [Header("Glow layers of the ADS image (imported, optional; set apart from the hip's)")]
        [Tooltip("Emission core drawn over the ADS image: same canvas, pivot and size as the ADS sprite (it moves with it). Empty = none.")]
        public Sprite adsEmissionSprite;
        [Tooltip("Soft glow around the ADS emission core (may be semi-transparent): same canvas, pivot and size as the ADS sprite. Empty = none.")]
        public Sprite adsGlowSprite;
        [Tooltip("Brightness of the ADS emission core (its opacity over the ADS image). 0 = the plain gun.")]
        [Range(0f, 1f)] public float adsEmissionIntensity = 1f;
        [Tooltip("Brightness of the ADS glow (its opacity, times the material's gain). 0 = off.")]
        [Range(0f, 1f)] public float adsGlowIntensity = 1f;
        [Tooltip("Material of the ADS glow layer, e.g. additive (Deadlock/Sprite Additive Glow) with its own gain. Empty = the ADS image's material (normal blend).")]
        public Material adsGlowMaterial;
        [Tooltip("Breathing of the ADS glow: its brightness dips by up to this share and comes back. 0 = steady.")]
        [Range(0f, 1f)] public float adsGlowPulse;
        [Tooltip("Breathing of the ADS emission core, with the ADS glow (same length and phase). 0 = steady.")]
        [Range(0f, 1f)] public float adsEmissionPulse;
        [Tooltip("Length of one pulse of the ADS glow and emission core (seconds).")]
        public float adsGlowPulseSeconds = 2.4f;

        [Header("Skins (imported)")]
        [Tooltip("Region map of the hip image (same canvas, Tools/pixelart/make_skin_regions.py). Empty = the hip image keeps its drawn colours.")]
        public Texture2D hipSkinRegions;
        [Tooltip("Region map of the ADS image. Empty = the ADS image keeps its drawn colours.")]
        public Texture2D adsSkinRegions;

        [Header("Hip pose (baked for 16:9)")]
        [Tooltip("Where the hip sprite pivot (muzzle) sits for a 16:9 screen.")]
        public Vector2 hipMuzzlePosition = new Vector2(0.06f, -0.12f);
        [Tooltip("Lasting roll of the hip image around its hold point (degrees, + = anticlockwise): a gun whose barrel is drawn near level is turned so the barrel points at the crosshair as the shots do. 0 = as drawn.")]
        public float hipRollDegrees;
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
        [Tooltip("Hip flash centre relative to the muzzle (units, image axes): e.g. a little beyond the muzzle along the barrel. Zero = on the muzzle. Every flash is drawn behind its gun image, so it shows only beyond the gun's outline.")]
        public Vector2 hipMuzzleFlashOffset = Vector2.zero;
        [Tooltip("On: the hip flash is a jet out of the muzzle along the drawn barrel (turned to hipMuzzleFlashBarrelDegrees, stretched by hipMuzzleFlashStretch, a little jitter per shot) instead of a star turned at random. Its centre is hipMuzzleFlashOffset (just beyond the bore), so the core touches the bore and the back half is hidden behind the gun.")]
        public bool hipMuzzleFlashAlongBarrel;
        [Tooltip("Direction the drawn barrel points out of the muzzle, in the hip image (degrees anticlockwise from its +x axis; e.g. 165 = left and a little up).")]
        public float hipMuzzleFlashBarrelDegrees = 180f;
        [Tooltip("Size of the jet against hipMuzzleFlashScale: along the barrel (x) and across it (y).")]
        public Vector2 hipMuzzleFlashStretch = Vector2.one;
        public float adsMuzzleFlashScale = 0.35f;
        [Tooltip("On (an optical sight): the ADS flash sits at its muzzle point under the sight glass, behind the housing, so only a burst at the glass's lower edge shows and the aiming dot stays clear. Off = held within AdsFlashMaxBelowSight of the aim point (a front sight hides its middle).")]
        public bool adsMuzzleFlashUnderSight;
        [Tooltip("Reload: lowered and rolled over the actual reload time.")]
        public float reloadDrop = 0.10f;
        public float reloadRollDegrees = 14f;
        [Tooltip("Point the weapon turns around (reload roll, fire kick), relative to each sprite's pivot: roughly the hands.")]
        public Vector2 hipHoldOffset = new Vector2(0.22f, -0.2f);
        public Vector2 adsHoldOffset = new Vector2(0f, -0.3f);
    }
}
