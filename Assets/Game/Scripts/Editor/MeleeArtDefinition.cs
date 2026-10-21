using Deadlock.Combat;
using Deadlock.View;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// First-person art of one melee weapon (editor-only data, read by <see cref="PixelWeaponArt"/> when scenes are built):
    /// one image of the weapon in the gloved hand (no ADS). Identified by <see cref="MeleeDefinition.weaponId"/>; files are
    /// asset references, never found by name. Only definitions listed in <see cref="WeaponArtRegistry.melee"/> are imported.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Melee Art Definition", fileName = "MeleeArt_")]
    public sealed class MeleeArtDefinition : ScriptableObject
    {
        [Tooltip("The melee weapon this art belongs to. Its weaponId is the art's id (unique in the registry).")]
        public MeleeDefinition melee;
        [Tooltip("Swing / feel values copied into the view profile on every import (the placement file may override some of them for this art).")]
        public MeleeViewProfile baseProfile;
        [Tooltip("Written by the import and assigned to the melee weapon. Empty: created next to this asset as MeleeView_<weaponId>_Art.")]
        public MeleeViewProfile viewProfile;
        [Tooltip("Source = the delivered original (only read), image = the displayed derivative, placement = hand-written grip / size / motion overrides.")]
        public WeaponArtDefinition.ViewArt art = new WeaponArtDefinition.ViewArt();

        public string WeaponId => melee != null ? melee.weaponId : null;
    }
}
