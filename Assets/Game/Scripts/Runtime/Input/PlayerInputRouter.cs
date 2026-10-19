using System;
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

        public InputMode Mode { get; private set; }
        public PlayerInputFrame Current { get; private set; }

        /// <summary>When true (default), desktop input is ignored until the cursor is captured by a click.</summary>
        public bool RequireCursorLock { get; set; } = true;

        public event Action<InputMode> ModeChanged;

        void Awake()
        {
            desktop = new DesktopInputSource();
        }

        void OnEnable() => desktop.Enable();

        void Start()
        {
            bool mobile = Application.isMobilePlatform;
            bool touch = mobile || startInTouchMode;
            SetMode(touch ? InputMode.Touch : InputMode.Desktop, simulateTouchWithMouse: touch && !mobile);
        }

        void OnDisable()
        {
            desktop.Disable();
            SetTouchSimulation(false);
            SetCursorLocked(false);
            SetUiPointerFilter(InputMode.Desktop);
        }

        public void SetMode(InputMode mode, bool simulateTouchWithMouse = false)
        {
            Mode = mode;
            SetTouchSimulation(mode == InputMode.Touch && simulateTouchWithMouse && !Application.isMobilePlatform);
            SetUiPointerFilter(mode);
            if (touchControls != null)
                touchControls.SetVisible(mode == InputMode.Touch);
            SetCursorLocked(mode == InputMode.Desktop && RequireCursorLock);
            Current = default;
            ModeChanged?.Invoke(mode);
        }

        void Update()
        {
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

            Current = desktop.Read(mouseDegreesPerPixel);
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
