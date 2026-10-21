using System;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>The weapon art definitions the game imports (Assets/Game/Data/WeaponArt/WeaponArtRegistry.asset). Unfinished definitions stay out of it.</summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Weapon Art Registry", fileName = "WeaponArtRegistry")]
    public sealed class WeaponArtRegistry : ScriptableObject
    {
        public WeaponArtDefinition[] weapons = Array.Empty<WeaponArtDefinition>();
        [Tooltip("Melee weapons (one image each, no ADS).")]
        public MeleeArtDefinition[] melee = Array.Empty<MeleeArtDefinition>();
    }
}
