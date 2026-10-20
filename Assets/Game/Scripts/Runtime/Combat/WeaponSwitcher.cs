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
    /// Main gun ⇄ sub melee weapon. A switch takes <see cref="switchTime"/> (first half: lower the old weapon,
    /// second half: raise the new one); neither weapon can attack meanwhile. Switching away from the gun cancels a
    /// reload in progress without adding any rounds (provisional prototype rule). Requests during a switch or a melee
    /// swing are ignored.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class WeaponSwitcher : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [SerializeField] GunController gun;
        [SerializeField] MeleeWeapon melee;
        [SerializeField, Min(0.01f)] float switchTime = 0.35f;

        float switchStart;
        WeaponSlot from;

        public WeaponSlot Current { get; private set; } = WeaponSlot.Gun;
        public bool IsSwitching { get; private set; }
        public float SwitchTime => switchTime;

        public event Action<WeaponSlot> Equipped;
        /// <summary>A switch toward this slot has started.</summary>
        public event Action<WeaponSlot> SwitchStarted;

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
                if (Time.time - switchStart >= switchTime)
                    Finish();
                return;
            }

            if (frame.SelectGunPressed)
                Request(WeaponSlot.Gun);
            else if (frame.SelectMeleePressed)
                Request(WeaponSlot.Melee);
            else if (frame.ToggleWeaponPressed)
                Request(Current == WeaponSlot.Gun ? WeaponSlot.Melee : WeaponSlot.Gun);
        }

        public bool Request(WeaponSlot target)
        {
            if (IsSwitching || target == Current || melee.IsSwinging)
                return false;
            from = Current;
            Current = target;
            IsSwitching = true;
            switchStart = Time.time;
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
            gun.SetEquipped(slot == WeaponSlot.Gun);
            melee.SetEquipped(slot == WeaponSlot.Melee);
            Equipped?.Invoke(slot);
        }

        void Finish()
        {
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
            float t = Mathf.Clamp01((Time.time - switchStart) / switchTime);
            if (slot == from)
                return Mathf.Clamp01(1f - t * 2f);
            if (slot == Current)
                return Mathf.Clamp01(t * 2f - 1f);
            return 0f;
        }
    }
}
