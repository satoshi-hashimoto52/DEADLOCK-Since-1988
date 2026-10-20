using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.UI;

namespace Deadlock.Input
{
    /// <summary>
    /// Produces one <see cref="PlayerInputFrame"/> per frame from exactly one active source
    /// (touch UI or desktop keyboard/mouse), so the two can never be applied twice.
    /// Mobile platforms always use touch. In the Editor, F2 toggles touch mode, where the mouse drives a simulated touchscreen.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        // Binding group of the touch bindings in the UI module's actions (DefaultInputActions).
        const string TouchBindingGroup = "Touch";

        [SerializeField] TouchControls touchControls;
        [Tooltip("UI module that drives the touch controls. In touch mode its bindings are limited to touch.")]
        [SerializeField] InputSystemUIInputModule uiInputModule;
        [Tooltip("Start in touch mode in the Editor / desktop player (mouse simulates touches).")]
        [SerializeField] bool startInTouchMode;
        [SerializeField, Min(0.001f)] float mouseDegreesPerPixel = 0.1f;

        DesktopInputSource desktop;
        bool simulatingTouch;
        readonly HashSet<string> blockers = new HashSet<string>();

        public InputMode Mode { get; private set; }
        public PlayerInputFrame Current { get; private set; }

        /// <summary>When true (default), desktop input is ignored until the cursor is captured by a click.</summary>
        public bool RequireCursorLock { get; set; } = true;

        /// <summary>True when keyboard/mouse actually drive the player (desktop mode and, if required, cursor captured).</summary>
        public bool DesktopInputActive =>
            Mode == InputMode.Desktop && (!RequireCursorLock || Cursor.lockState == CursorLockMode.Locked);

        /// <summary>True while a menu, the layout editor, death or a result screen stops player input.</summary>
        public bool IsBlocked => blockers.Count > 0;

        /// <summary>Desktop menu key (Tab) pressed this frame. Read even while blocked so the menu can be closed.</summary>
        public bool MenuKeyPressed { get; private set; }

        public event Action<InputMode> ModeChanged;

        /// <summary>
        /// When set, replaces the devices as the input source (automated network checks, future bots). Blocking
        /// (menu, death, ...) still applies on top of it.
        /// </summary>
        public Func<PlayerInputFrame> ExternalSource { get; set; }

        /// <summary>Set by the gun each frame: false while ADS cannot be used (knife out, switching).</summary>
        public bool AdsAvailable { get; set; } = true;

        void Awake()
        {
            desktop = new DesktopInputSource();
        }

        void OnEnable()
        {
            desktop.Enable();
            ControlSettings.Changed += OnControlSettingsChanged;
        }

        // A changed control setting clears what is held (fire, ADS from every source): a new press is needed.
        void OnControlSettingsChanged() => ReleaseHeldInput();

        /// <summary>
        /// Turns ADS off on every source (menu, layout editor, death, background, input mode or setting change,
        /// weapon put away). It does not come back by itself: the button must be pressed again.
        /// </summary>
        public void CancelAds()
        {
            desktop.CancelAds();
            if (touchControls != null)
                touchControls.CancelAds();
            var frame = Current;
            frame.AdsActive = false;
            Current = frame;
        }

        void Start()
        {
            bool mobile = Application.isMobilePlatform;
            bool touch = mobile || startInTouchMode;
            SetMode(touch ? InputMode.Touch : InputMode.Desktop, simulateTouchWithMouse: touch && !mobile);
        }

        void OnDisable()
        {
            ControlSettings.Changed -= OnControlSettingsChanged;
            desktop.Disable();
            SetTouchSimulation(false);
            SetCursorLocked(false);
            SetUiPointerFilter(InputMode.Desktop);
        }

        // Backgrounding (home button, notification, call) can swallow the release of fingers and keys held at that
        // moment. Drop all held input so nothing keeps firing, moving or pressing after returning.
        void OnApplicationPause(bool paused)
        {
            if (paused)
                ReleaseHeldInput();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ReleaseHeldInput();
        }

        /// <summary>
        /// Stops player input for a reason (e.g. "menu", "dead"). Local input only: it does not pause the game,
        /// so the same call works in a future online match. Every reason must be released with <see cref="Unblock"/>.
        /// </summary>
        public void Block(string reason)
        {
            if (!blockers.Add(reason) || blockers.Count != 1)
                return;
            ReleaseHeldInput();
            if (Mode == InputMode.Desktop)
                SetCursorLocked(false); // menus need the pointer
        }

        public void Unblock(string reason)
        {
            if (blockers.Remove(reason) && blockers.Count == 0)
            {
                // Nothing pressed before or during the menu carries over: touches were released on Block (a finger
                // still down needs a new touch), held keys / mouse buttons count again only after being released.
                ReleaseHeldInput();
                desktop.SuppressHeldUntilReleased();
            }
        }

        public void ReleaseHeldInput()
        {
            if (touchControls != null)
                touchControls.ReleaseAll();
            desktop.CancelAds();
            Current = default;
        }

        public void SetMode(InputMode mode, bool simulateTouchWithMouse = false)
        {
            Mode = mode;
            SetTouchSimulation(mode == InputMode.Touch && simulateTouchWithMouse && !Application.isMobilePlatform);
            SetUiPointerFilter(mode);
            if (touchControls != null)
                touchControls.SetVisible(mode == InputMode.Touch);
            SetCursorLocked(mode == InputMode.Desktop && RequireCursorLock);
            CancelAds();
            Current = default;
            ModeChanged?.Invoke(mode);
        }

        void Update()
        {
            MenuKeyPressed = desktop.MenuPressed;
            desktop.Tick();
            if (IsBlocked)
            {
                Current = default;
                return;
            }

            if (ExternalSource != null)
            {
                Current = ExternalSource();
                return;
            }

            if (!Application.isMobilePlatform && desktop.ToggleModePressed)
            {
                SetMode(Mode == InputMode.Desktop ? InputMode.Touch : InputMode.Desktop, simulateTouchWithMouse: true);
                return;
            }

            if (Mode == InputMode.Touch)
            {
                Current = touchControls != null ? touchControls.Read() : default;
                return;
            }

            if (RequireCursorLock)
            {
                if (desktop.ReleaseCursorPressed)
                    SetCursorLocked(false);
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    // The click that recaptures the cursor is not forwarded as a shot.
                    if (desktop.AnyClickPressed)
                        SetCursorLocked(true);
                    Current = default;
                    return;
                }
            }

            Current = desktop.Read(mouseDegreesPerPixel, AdsAvailable);
        }

        void SetTouchSimulation(bool enable)
        {
            if (enable == simulatingTouch)
                return;
            simulatingTouch = enable;
            if (enable)
                TouchSimulation.Enable();
            else
                TouchSimulation.Disable();
        }

        /// <summary>
        /// The default UI actions feed mouse and touch through the same Point/Click actions. When a physical mouse
        /// button and a finger overlap (or change in the same input update) the UI module mixes them up: the touch-down
        /// is lost, or the finger's release is never delivered and a button stays held. In touch mode the UI therefore
        /// listens to touch bindings only; desktop mode restores all bindings.
        /// </summary>
        void SetUiPointerFilter(InputMode mode)
        {
            if (uiInputModule == null || uiInputModule.actionsAsset == null)
                return;
            uiInputModule.actionsAsset.bindingMask = mode == InputMode.Touch
                ? InputBinding.MaskByGroup(TouchBindingGroup)
                : (InputBinding?)null;
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
