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
    }
}
