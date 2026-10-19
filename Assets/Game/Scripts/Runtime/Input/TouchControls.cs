using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Deadlock.Input
{
    /// <summary>
    /// Root of the on-screen touch controls. Converts the state of the individual widgets into a <see cref="PlayerInputFrame"/>.
    /// Each widget tracks its own pointer id, so several fingers work at once, and a finger that starts on a button
    /// never reaches the look area (buttons sit above it in the UI hierarchy).
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        [SerializeField] VirtualJoystick moveStick;
        [SerializeField] TouchLookArea lookArea;
        [SerializeField] TouchButton fireButton;
        [SerializeField] TouchButton adsButton;
        [SerializeField] TouchButton reloadButton;
        [SerializeField] TouchButton jumpButton;
        [Tooltip("Degrees of rotation for a drag across the full screen height.")]
        [SerializeField, Min(1f)] float lookDegreesPerScreenHeight = 140f;
        [Tooltip("Stick pushed forward beyond this value sprints (provisional prototype rule).")]
        [SerializeField, Range(0.5f, 1f)] float sprintThreshold = 0.9f;

        bool adsToggled;

        public bool AdsToggled => adsToggled;

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (!visible)
                SetAds(false);
        }

        public PlayerInputFrame Read()
        {
            if (adsButton.ConsumePressed())
                SetAds(!adsToggled);

            float degreesPerPixel = lookDegreesPerScreenHeight / Mathf.Max(1, Screen.height);
            Vector2 move = moveStick.Value;
            return new PlayerInputFrame
            {
                Move = move,
                LookDegrees = lookArea.ConsumeDelta() * degreesPerPixel,
                Sprint = move.y >= sprintThreshold,
                JumpPressed = jumpButton.ConsumePressed(),
                FireHeld = fireButton.IsHeld,
                FirePressed = fireButton.ConsumePressed(),
                AdsActive = adsToggled,
                ReloadPressed = reloadButton.ConsumePressed(),
            };
        }

        void SetAds(bool on)
        {
            adsToggled = on;
            if (adsButton != null)
                adsButton.SetLatched(on);
        }

        /// <summary>Only real (or simulated) touches drive the touch widgets; mouse pointer events are ignored.</summary>
        internal static bool IsTouch(PointerEventData eventData) =>
            eventData is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch;
    }
}
