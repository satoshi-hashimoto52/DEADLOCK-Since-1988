using System;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

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
        [SerializeField] TouchControls touchControls;
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
        }

        public void SetMode(InputMode mode, bool simulateTouchWithMouse = false)
        {
            Mode = mode;
            SetTouchSimulation(mode == InputMode.Touch && simulateTouchWithMouse && !Application.isMobilePlatform);
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

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
