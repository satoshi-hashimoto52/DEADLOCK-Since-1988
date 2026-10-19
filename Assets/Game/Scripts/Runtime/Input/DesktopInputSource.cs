using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadlock.Input
{
    /// <summary>
    /// Keyboard + mouse input for checking the game in the Editor / on desktop. Not a shipping control scheme.
    /// </summary>
    public sealed class DesktopInputSource
    {
        readonly InputActionMap map;
        readonly InputAction move;
        readonly InputAction look;
        readonly InputAction sprint;
        readonly InputAction jump;
        readonly InputAction fire;
        readonly InputAction ads;
        readonly InputAction reload;
        readonly InputAction toggleMode;
        readonly InputAction releaseCursor;

        public DesktopInputSource()
        {
            map = new InputActionMap("DesktopDev");
            move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            look = map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            sprint = map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            jump = map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            fire = map.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            ads = map.AddAction("Ads", InputActionType.Button, "<Mouse>/rightButton");
            reload = map.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            toggleMode = map.AddAction("ToggleMode", InputActionType.Button, "<Keyboard>/f2");
            releaseCursor = map.AddAction("ReleaseCursor", InputActionType.Button, "<Keyboard>/escape");
        }

        public bool ToggleModePressed => toggleMode.WasPressedThisFrame();
        public bool ReleaseCursorPressed => releaseCursor.WasPressedThisFrame();
        public bool AnyClickPressed => fire.WasPressedThisFrame() || ads.WasPressedThisFrame();

        public void Enable() => map.Enable();
        public void Disable() => map.Disable();

        public PlayerInputFrame Read(float mouseDegreesPerPixel)
        {
            Vector2 delta = look.ReadValue<Vector2>() * mouseDegreesPerPixel;
            return new PlayerInputFrame
            {
                Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                LookDegrees = delta,
                Sprint = sprint.IsPressed(),
                JumpPressed = jump.WasPressedThisFrame(),
                FireHeld = fire.IsPressed(),
                FirePressed = fire.WasPressedThisFrame(),
                AdsActive = ads.IsPressed(),
                ReloadPressed = reload.WasPressedThisFrame(),
            };
        }
    }
}
