using System;
using Deadlock.Combat;
using Deadlock.View;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// First-person art of one weapon (editor-only data, read by <see cref="PixelWeaponArt"/> when scenes are built).
    /// The weapon is identified by its <see cref="WeaponDefinition.weaponId"/>; images and placement files are referenced
    /// as assets (by GUID), never found by file name. Editor-only on purpose: the original images referenced here must
    /// not be pulled into player builds (the game only uses the view profile written from this).
    /// Only definitions listed in <see cref="WeaponArtRegistry"/> are imported.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon Art Definition", fileName = "WeaponArt_")]
    public sealed class WeaponArtDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class ViewArt
        {
            [Tooltip("The approved original (kept as is; no tool writes it). Must have the same pixel size as the image.")]
            public Texture2D source;
            [Tooltip("The image the game shows (e.g. the alpha-cleaned derivative of the source, Tools/pixelart/make_opaque.py). Only its import settings are set.")]
            public Texture2D image;
            [Tooltip("Hand-written placement file (pixel coordinates from the image's top-left): hip = muzzle, hold, size, screen anchoring; ADS = aim, muzzle, hold, size.")]
            public TextAsset placement;
            [Tooltip("Optional skin region map of the image (same pixel size; Tools/pixelart/make_skin_regions.py). Empty = this view keeps its drawn colours.")]
            public Texture2D skinRegions;
        }

        [Tooltip("The weapon this art belongs to. Its weaponId is the art's id (must be unique in the registry).")]
        public WeaponDefinition weapon;
        [Tooltip("Motion / feel values (kick, bob, reload, ADS cross-fade...) copied into the view profile on every import.")]
        public WeaponViewProfile baseProfile;
        [Tooltip("Written by the import and assigned to the weapon. Empty: created next to this asset as WeaponView_<weaponId>_Art.")]
        public WeaponViewProfile viewProfile;
        [Tooltip("Muzzle flash sprite (optional, may be shared by weapons).")]
        public Sprite muzzleFlash;
        public ViewArt hip = new ViewArt();
        public ViewArt ads = new ViewArt();

        public string WeaponId => weapon != null ? weapon.weaponId : null;
    }
}
