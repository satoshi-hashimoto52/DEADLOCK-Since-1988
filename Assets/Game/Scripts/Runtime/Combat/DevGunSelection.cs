using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Development test choice of the gun (Menu → Settings → "Gun (test)"): the rifle or the SMG candidate. Not a weapon
    /// selection of the game: the SMG is a candidate before adoption, and its performance values are the rifle's, copied
    /// for checking (not an SMG balance decision). The choice lasts for the app session (scene loads and retries keep
    /// it, a restart goes back to the first option) and is never saved, so no stored setting changes. Offline scenes only.
    /// </summary>
    public sealed class DevGunSelection : MonoBehaviour
    {
        [SerializeField] GunController gun;
        [Tooltip("The guns offered; the first is the scene's own gun.")]
        [SerializeField] WeaponDefinition[] options = Array.Empty<WeaponDefinition>();

        static string selectedId;

        public IReadOnlyList<WeaponDefinition> Options => options;
        public WeaponDefinition Current => gun.Definition;
        public static string SelectedId => selectedId;

        public event Action Changed;

        // After every Awake / OnEnable of the scene, so the view model is listening when the gun is swapped.
        void Start()
        {
            if (!string.IsNullOrEmpty(selectedId))
                Apply(selectedId);
        }

        /// <summary>Switches to the gun with this id (fresh gun state) and keeps the choice for this session.</summary>
        public bool Select(string weaponId)
        {
            foreach (var option in options)
                if (option != null && option.weaponId == weaponId)
                {
                    selectedId = weaponId;
                    Apply(weaponId);
                    return true;
                }
            return false;
        }

        /// <summary>The next option after the current gun (the menu button cycles through them).</summary>
        public WeaponDefinition Next()
        {
            int i = Array.IndexOf(options, gun.Definition);
            return options.Length == 0 ? null : options[(i + 1 + options.Length) % options.Length];
        }

        void Apply(string weaponId)
        {
            foreach (var option in options)
                if (option != null && option.weaponId == weaponId && option != gun.Definition)
                {
                    gun.SetDefinition(option);
                    Changed?.Invoke();
                    return;
                }
        }

        /// <summary>Tests: back to the scene's own gun on the next load.</summary>
        public static void ClearSelection() => selectedId = null;
    }
}
