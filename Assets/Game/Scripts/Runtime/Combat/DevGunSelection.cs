using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Development test choice of the gun (Menu → Settings → "Gun (test)"): the rifle or the SMG candidate. Not a weapon
    /// selection of the game: the SMG's art is a candidate before adoption; its damage, fire rate and magazine are the
    /// confirmed SMG values (D-053), its other values provisional. The choice lasts for the app session (scene loads and retries keep
    /// it, a restart goes back to the first option) and is never saved, so no stored setting changes. Offline scenes only.
    /// <para>
    /// Development equipment (Stage 33, before the gacha exists): any main gun of the <see cref="WeaponCatalog"/> in
    /// Slot 1, the handgun (or nothing) in Slot 2, the knife kept in Slot 3, each checked by <see cref="Loadout"/> so an
    /// invalid combination is refused. Session only like the choice above: it is not an owned item, writes no saved data
    /// and gives nothing of the gacha's. Off in a release build (<see cref="Enabled"/>). The menu button above and the
    /// weapon lab keep using <see cref="Options"/> (rifle ⇄ SMG) as before.
    /// </para>
    /// </summary>
    public sealed class DevGunSelection : MonoBehaviour
    {
        [SerializeField] GunController gun;
        [Tooltip("The guns offered; the first is the scene's own gun.")]
        [SerializeField] WeaponDefinition[] options = Array.Empty<WeaponDefinition>();

        [Tooltip("Weapons offered by the development equipment. Empty: the catalog in Resources.")]
        [SerializeField] WeaponCatalog catalog;

        static string selectedId;
        // Development equipment of Slot 2: null = not chosen this session (the scene's own), "" = empty.
        static string selectedSecondaryId;

        public IReadOnlyList<WeaponDefinition> Options => options;
        public WeaponDefinition Current => gun.Definition;
        public static string SelectedId => selectedId;
        public static string SelectedSecondaryId => selectedSecondaryId;

        public event Action Changed;

        /// <summary>
        /// Development equipment is available: Editor and development builds only, unless switched off. A release build
        /// (the official game) never offers it.
        /// </summary>
        public static bool Enabled => Debug.isDebugBuild && !SwitchedOff;

        /// <summary>Turns the development equipment off (as in a release build), for checks.</summary>
        public static bool SwitchedOff { get; set; }

        public WeaponCatalog Catalog => catalog != null ? catalog : catalog = WeaponCatalog.Load();
        public GunController Gun => gun;

        /// <summary>Guns offered for Slot 1 (the catalog's main guns; without a catalog, <see cref="Options"/>).</summary>
        public IReadOnlyList<WeaponDefinition> MainGunChoices => Catalog != null ? Catalog.GunsFor(LoadoutSlot.Primary) : options;

        /// <summary>Guns offered for Slot 2 (the catalog's handguns).</summary>
        public IReadOnlyList<WeaponDefinition> SubGunChoices =>
            Catalog != null ? Catalog.GunsFor(LoadoutSlot.Secondary) : (IReadOnlyList<WeaponDefinition>)Array.Empty<WeaponDefinition>();

        public WeaponDefinition MainGun => gun.Inventory.Gun(LoadoutSlot.Primary);
        public WeaponDefinition SubGun => gun.Inventory.Gun(LoadoutSlot.Secondary);
        public MeleeDefinition Knife
        {
            get
            {
                var melee = GetComponentInChildren<MeleeWeapon>();
                return melee != null ? melee.Definition : null;
            }
        }

        // After every Awake / OnEnable of the scene, so the view model is listening when the gun is swapped.
        void Start()
        {
            if (!string.IsNullOrEmpty(selectedId))
                Apply(selectedId);
            if (selectedSecondaryId != null && Enabled)
                TryEquipSub(selectedSecondaryId == "" ? null : Find(SubGunChoices, selectedSecondaryId), out _, remember: false);
        }

        /// <summary>
        /// Development equipment: puts a main gun in Slot 1 (fresh rounds; drawn if Slot 1 is in hand) and keeps the choice
        /// for this session. Refused (false, nothing changes) when switched off, during a weapon switch, for a gun not in
        /// the catalog's Slot 1 choices, or when the loadout would be invalid.
        /// </summary>
        public bool TryEquipMain(WeaponDefinition next, out string error)
        {
            if (!CanChange(out error))
                return false;
            if (next == null || Find(MainGunChoices, next.weaponId) != next)
            {
                error = next == null ? "Slot 1 needs a main gun" : $"{next.weaponId} is not offered for Slot 1";
                return false;
            }
            if (!Loadout.TryCreate(next, SubGun, Knife, out _, out error))
                return false;
            selectedId = next.weaponId;
            if (next != MainGun)
            {
                if (gun.ActiveSlot == LoadoutSlot.Primary)
                    gun.SetDefinition(next);
                else
                    gun.EquipGun(LoadoutSlot.Primary, next);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Development equipment: puts a handgun in Slot 2 (fresh rounds), or empties Slot 2 (null; the main gun is taken
        /// in hand first if the sub gun was out). Refused like <see cref="TryEquipMain"/>.
        /// </summary>
        public bool TryEquipSub(WeaponDefinition next, out string error) => TryEquipSub(next, out error, remember: true);

        bool TryEquipSub(WeaponDefinition next, out string error, bool remember)
        {
            if (!CanChange(out error))
                return false;
            if (next != null && Find(SubGunChoices, next.weaponId) != next)
            {
                error = $"{next.weaponId} is not offered for Slot 2";
                return false;
            }
            if (!Loadout.TryCreate(MainGun, next, Knife, out _, out error))
                return false;
            if (remember)
                selectedSecondaryId = next != null ? next.weaponId : "";
            if (next == null)
            {
                if (SubGun == null)
                    return true;
                if (gun.ActiveSlot == LoadoutSlot.Secondary)
                    gun.SelectGun(LoadoutSlot.Primary);
                gun.Inventory.Remove(LoadoutSlot.Secondary);
            }
            else if (next != SubGun)
            {
                gun.EquipGun(LoadoutSlot.Secondary, next);
            }
            Changed?.Invoke();
            return true;
        }

        bool CanChange(out string error)
        {
            error = null;
            if (!Enabled)
                error = "development equipment is off";
            else
            {
                var switcher = GetComponentInChildren<WeaponSwitcher>();
                if (switcher != null && switcher.IsSwitching)
                    error = "a weapon switch is in progress";
            }
            return error == null;
        }

        static WeaponDefinition Find(IReadOnlyList<WeaponDefinition> list, string weaponId)
        {
            foreach (var d in list)
                if (d != null && d.weaponId == weaponId)
                    return d;
            return null;
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
            // A main gun chosen in the development equipment (not one of the options).
            var main = Enabled && Find(options, weaponId) == null ? Find(MainGunChoices, weaponId) : null;
            if (main != null && main != gun.Definition)
            {
                gun.SetDefinition(main);
                Changed?.Invoke();
            }
        }

        /// <summary>Tests: back to the scene's own guns (Slot 1 and Slot 2) on the next load.</summary>
        public static void ClearSelection()
        {
            selectedId = null;
            selectedSecondaryId = null;
        }
    }
}
