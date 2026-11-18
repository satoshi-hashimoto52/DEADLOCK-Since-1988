using System;
using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Combat
{
    public enum WeaponSlot
    {
        Gun,
        Melee,
    }

    /// <summary>
    /// Gun ⇄ knife, and main gun (Slot 1) ⇄ sub gun (Slot 2). A switch takes <see cref="switchTime"/>, or the drawn gun's
    /// own <see cref="WeaponDefinition.switchTime"/> when set (D-052; 0 in every current gun) (first half: lower the old
    /// weapon, second half: raise the new one); neither weapon can attack meanwhile. Switching away from a gun cancels a
    /// reload in progress without adding any rounds (provisional prototype rule); each gun keeps its own rounds
    /// (<see cref="GunController.Inventory"/>). Requests during a switch or a melee swing are ignored.
    /// Controls (Stage 32): key 1 main gun, key 2 knife (both as before), key 3 sub gun; the mobile weapon button
    /// toggles gun ⇄ knife (back to the gun last held) as before, the sub-gun button toggles main ⇄ sub gun. Taking the
    /// sub gun is refused when none is carried. A gun ⇄ gun switch raises <see cref="GunSwapStarted"/>, not
    /// <see cref="SwitchStarted"/> (that one stays gun ⇄ knife, as the online avatar sends it).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class WeaponSwitcher : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [SerializeField] GunController gun;
        [SerializeField] MeleeWeapon melee;
        [SerializeField, Min(0.01f)] float switchTime = 0.35f;

        float switchStart;
        float currentSwitchTime;
        WeaponSlot from;
        // Gun ⇄ gun: the gun taken in hand at the switch's midpoint (when the old one is fully lowered).
        LoadoutSlot? gunSwapTarget;

        public WeaponSlot Current { get; private set; } = WeaponSlot.Gun;
        public bool IsSwitching { get; private set; }
        public float SwitchTime => switchTime;

        public event Action<WeaponSlot> Equipped;
        /// <summary>A switch toward this slot has started.</summary>
        public event Action<WeaponSlot> SwitchStarted;
        /// <summary>A main gun ⇄ sub gun switch toward this slot has started (the gun stays out: lowered, swapped, raised).</summary>
        public event Action<LoadoutSlot> GunSwapStarted;

        /// <summary>A gun ⇄ gun switch is in progress.</summary>
        public bool IsSwappingGun => IsSwitching && gunSwapTarget.HasValue;

        void Start()
        {
            gun.SetEquipped(Current == WeaponSlot.Gun);
            melee.SetEquipped(Current == WeaponSlot.Melee);
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            if (IsSwitching)
            {
                float elapsed = Time.time - switchStart;
                if (gunSwapTarget.HasValue && elapsed >= currentSwitchTime * 0.5f)
                {
                    gun.SelectGun(gunSwapTarget.Value); // the old gun is down: the new one comes up
                    gunSwapTarget = null;
                }
                if (elapsed >= currentSwitchTime)
                    Finish();
                return;
            }

            if (frame.SelectGunPressed)
                RequestGun(LoadoutSlot.Primary);
            else if (frame.SelectMeleePressed)
                Request(WeaponSlot.Melee);
            else if (frame.SelectSecondaryPressed)
                RequestGun(LoadoutSlot.Secondary);
            else if (frame.ToggleWeaponPressed)
                Request(Current == WeaponSlot.Gun ? WeaponSlot.Melee : WeaponSlot.Gun);
            else if (frame.SwapGunPressed)
                RequestGun(gun.ActiveSlot == LoadoutSlot.Primary ? LoadoutSlot.Secondary : LoadoutSlot.Primary);
        }

        /// <summary>
        /// Takes the gun of a loadout slot in hand: from the knife (that gun comes out), or from the other gun (lowered,
        /// swapped, raised). False (nothing happens) when no gun is in that slot, that gun is already out, or a switch or
        /// a swing is in progress. <see cref="LoadoutSlot.Knife"/> is the knife.
        /// </summary>
        public bool RequestGun(LoadoutSlot slot)
        {
            if (slot == LoadoutSlot.Knife)
                return Request(WeaponSlot.Melee);
            if (IsSwitching || melee.IsSwinging || !gun.Inventory.Has(slot))
                return false;
            if (Current == WeaponSlot.Melee)
            {
                gun.SelectGun(slot); // put away: it simply comes out as this gun
                return Request(WeaponSlot.Gun);
            }
            if (gun.ActiveSlot == slot)
                return false;
            from = WeaponSlot.Gun;
            IsSwitching = true;
            switchStart = Time.time;
            currentSwitchTime = SwitchTimeOf(gun.Inventory.Gun(slot));
            gunSwapTarget = slot;
            gun.SetEquipped(false);
            GunSwapStarted?.Invoke(slot);
            return true;
        }

        float SwitchTimeOf(WeaponDefinition gunDefinition) =>
            gunDefinition != null && gunDefinition.switchTime > 0f ? gunDefinition.switchTime : switchTime;

        public bool Request(WeaponSlot target)
        {
            if (IsSwitching || target == Current || melee.IsSwinging)
                return false;
            from = Current;
            Current = target;
            IsSwitching = true;
            switchStart = Time.time;
            currentSwitchTime = SwitchTimeOf(gun.Definition);
            gun.SetEquipped(false);
            melee.SetEquipped(false);
            SwitchStarted?.Invoke(target);
            return true;
        }

        /// <summary>Equips a slot at once, without a switch (respawn).</summary>
        public void ForceSlot(WeaponSlot slot)
        {
            Current = slot;
            IsSwitching = false;
            gunSwapTarget = null;
            gun.SetEquipped(slot == WeaponSlot.Gun);
            melee.SetEquipped(slot == WeaponSlot.Melee);
            Equipped?.Invoke(slot);
        }

        void Finish()
        {
            if (gunSwapTarget.HasValue)
                gun.SelectGun(gunSwapTarget.Value);
            gunSwapTarget = null;
            IsSwitching = false;
            gun.SetEquipped(Current == WeaponSlot.Gun);
            melee.SetEquipped(Current == WeaponSlot.Melee);
            Equipped?.Invoke(Current);
        }

        /// <summary>How far a weapon is raised into view (0 = put away, 1 = ready), for the first-person views.</summary>
        public float Presence(WeaponSlot slot)
        {
            if (!IsSwitching)
                return slot == Current ? 1f : 0f;
            float t = Mathf.Clamp01((Time.time - switchStart) / currentSwitchTime);
            if (from == Current)
                return slot == Current ? Mathf.Abs(1f - t * 2f) : 0f; // gun ⇄ gun: down, swapped, up
            if (slot == from)
                return Mathf.Clamp01(1f - t * 2f);
            if (slot == Current)
                return Mathf.Clamp01(t * 2f - 1f);
            return 0f;
        }
    }
}
