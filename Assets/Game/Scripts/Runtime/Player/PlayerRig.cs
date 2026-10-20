using Deadlock.Combat;
using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Player
{
    /// <summary>
    /// The local player's parts in one place (input, body, weapons, health, view). Used by systems that drive or mirror
    /// the local player from outside, such as the online avatar.
    /// </summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [SerializeField] FirstPersonMotor motor;
        [SerializeField] PostureController posture;
        [SerializeField] GunController gun;
        [SerializeField] MeleeWeapon melee;
        [SerializeField] WeaponSwitcher weapons;
        [SerializeField] PlayerHealth health;
        [Tooltip("The eye: camera position and aim direction.")]
        [SerializeField] Transform aim;

        public PlayerInputRouter Input => input;
        public FirstPersonMotor Motor => motor;
        public PostureController Posture => posture;
        public GunController Gun => gun;
        public MeleeWeapon Melee => melee;
        public WeaponSwitcher Weapons => weapons;
        public PlayerHealth Health => health;
        public Transform Aim => aim;

        /// <summary>Fresh player at a spawn point: alive, standing, gun out with full ammo.</summary>
        public void ResetAt(Vector3 position, float yaw)
        {
            health.Revive(health.MaxHealth);
            motor.Teleport(position, yaw);
            posture.ResetToStanding();
            weapons.ForceSlot(WeaponSlot.Gun);
            gun.ResetState();
            input.ReleaseHeldInput();
        }
    }
}
