using UnityEngine;

namespace Deadlock.Input
{
    public enum InputMode
    {
        Desktop,
        Touch,
    }

    /// <summary>
    /// Device-independent player intent for one frame. Gameplay reads only this, never raw devices,
    /// so the same intent can later be sent over the network.
    /// </summary>
    public struct PlayerInputFrame
    {
        /// <summary>x = strafe, y = forward. Magnitude &lt;= 1.</summary>
        public Vector2 Move;
        /// <summary>Look rotation for this frame in degrees (x = yaw, y = pitch up).</summary>
        public Vector2 LookDegrees;
        public bool Sprint;
        public bool JumpPressed;
        public bool FireHeld;
        public bool FirePressed;
        public bool AdsActive;
        public bool ReloadPressed;
        /// <summary>Stand ⇄ crouch (from prone: crouch).</summary>
        public bool CrouchPressed;
        /// <summary>Go prone (from prone: stand).</summary>
        public bool PronePressed;
        public bool SelectGunPressed;
        public bool SelectMeleePressed;
        /// <summary>Switch to the other weapon (mobile single button).</summary>
        public bool ToggleWeaponPressed;
    }
}
