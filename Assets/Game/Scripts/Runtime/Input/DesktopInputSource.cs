using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadlock.Input
{
    /// <summary>
    /// Keyboard + mouse input for checking the game in the Editor / on desktop. Not a shipping control scheme.
    /// All key assignments are listed in <see cref="Bindings"/> (initial, provisional layout).
    /// </summary>
    public sealed class DesktopInputSource
    {
        /// <summary>Initial desktop key layout (provisional; change here to rebind).</summary>
        public static class Bindings
        {
            public const string Up = "<Keyboard>/w", Down = "<Keyboard>/s", Left = "<Keyboard>/a", Right = "<Keyboard>/d";
            public const string Look = "<Mouse>/delta";
            public const string Sprint = "<Keyboard>/leftShift";
            public const string Jump = "<Keyboard>/space";
            public const string Fire = "<Mouse>/leftButton";
            public const string Ads = "<Mouse>/rightButton";
            public const string Reload = "<Keyboard>/r";
            public const string Crouch = "<Keyboard>/c";
            public const string Prone = "<Keyboard>/z";
            public const string SelectGun = "<Keyboard>/1";
            public const string SelectMelee = "<Keyboard>/2";
            public const string ToggleMode = "<Keyboard>/f2";
            public const string ReleaseCursor = "<Keyboard>/escape";
            public const string Menu = "<Keyboard>/tab";
        }

        readonly InputActionMap map;
        readonly InputAction move;
        readonly InputAction look;
        readonly InputAction sprint;
        readonly InputAction jump;
        readonly InputAction fire;
        readonly InputAction ads;
        readonly InputAction reload;
        readonly InputAction crouch;
        readonly InputAction prone;
        readonly InputAction selectGun;
        readonly InputAction selectMelee;
        readonly InputAction toggleMode;
        readonly InputAction releaseCursor;
        readonly InputAction menu;

        // Held inputs ignored until released (keys / buttons already down when input resumes after a menu).
        bool suppressMove, suppressSprint, suppressFire, suppressAds;

        public DesktopInputSource()
        {
            map = new InputActionMap("DesktopDev");
            move = map.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", Bindings.Up)
                .With("Down", Bindings.Down)
                .With("Left", Bindings.Left)
                .With("Right", Bindings.Right);
            look = map.AddAction("Look", InputActionType.Value, Bindings.Look);
            sprint = map.AddAction("Sprint", InputActionType.Button, Bindings.Sprint);
            jump = map.AddAction("Jump", InputActionType.Button, Bindings.Jump);
            fire = map.AddAction("Fire", InputActionType.Button, Bindings.Fire);
            ads = map.AddAction("Ads", InputActionType.Button, Bindings.Ads);
            reload = map.AddAction("Reload", InputActionType.Button, Bindings.Reload);
            crouch = map.AddAction("Crouch", InputActionType.Button, Bindings.Crouch);
            prone = map.AddAction("Prone", InputActionType.Button, Bindings.Prone);
            selectGun = map.AddAction("SelectGun", InputActionType.Button, Bindings.SelectGun);
            selectMelee = map.AddAction("SelectMelee", InputActionType.Button, Bindings.SelectMelee);
            toggleMode = map.AddAction("ToggleMode", InputActionType.Button, Bindings.ToggleMode);
            releaseCursor = map.AddAction("ReleaseCursor", InputActionType.Button, Bindings.ReleaseCursor);
            menu = map.AddAction("Menu", InputActionType.Button, Bindings.Menu);
        }

        public bool ToggleModePressed => toggleMode.WasPressedThisFrame();
        public bool ReleaseCursorPressed => releaseCursor.WasPressedThisFrame();
        public bool MenuPressed => menu.WasPressedThisFrame();
        public bool AnyClickPressed => fire.WasPressedThisFrame() || ads.WasPressedThisFrame();

        /// <summary>Ignores every key / button held right now until it is released (no carry-over into play).</summary>
        public void SuppressHeldUntilReleased()
        {
            suppressMove = move.ReadValue<Vector2>() != Vector2.zero;
            suppressSprint = sprint.IsPressed();
            suppressFire = fire.IsPressed();
            suppressAds = ads.IsPressed();
        }

        /// <summary>Clears suppressions whose key was released. Call once per frame.</summary>
        public void Tick()
        {
            suppressMove &= move.ReadValue<Vector2>() != Vector2.zero;
            suppressSprint &= sprint.IsPressed();
            suppressFire &= fire.IsPressed();
            suppressAds &= ads.IsPressed();
        }

        public void Enable() => map.Enable();
        public void Disable() => map.Disable();

        public PlayerInputFrame Read(float mouseDegreesPerPixel)
        {
            Vector2 delta = look.ReadValue<Vector2>() * mouseDegreesPerPixel;
            return new PlayerInputFrame
            {
                Move = suppressMove ? Vector2.zero : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                LookDegrees = delta,
                Sprint = !suppressSprint && sprint.IsPressed(),
                JumpPressed = jump.WasPressedThisFrame(),
                FireHeld = !suppressFire && fire.IsPressed(),
                FirePressed = !suppressFire && fire.WasPressedThisFrame(),
                AdsActive = !suppressAds && ads.IsPressed(),
                ReloadPressed = reload.WasPressedThisFrame(),
                CrouchPressed = crouch.WasPressedThisFrame(),
                PronePressed = prone.WasPressedThisFrame(),
                SelectGunPressed = selectGun.WasPressedThisFrame(),
                SelectMeleePressed = selectMelee.WasPressedThisFrame(),
            };
        }
    }
}
