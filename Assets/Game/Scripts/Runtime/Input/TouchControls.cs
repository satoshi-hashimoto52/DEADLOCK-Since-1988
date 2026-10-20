using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Deadlock.Input
{
    /// <summary>
    /// Root of the on-screen touch controls. Converts the state of the individual widgets into a <see cref="PlayerInputFrame"/>.
    /// Each widget tracks its own pointer id, so several fingers work at once, and a finger that starts on a button
    /// never reaches the look area (buttons sit above it in the UI hierarchy).
    /// Initial (provisional) layout: posture button tap = stand ⇄ crouch, hold = prone ⇄ stand; weapon button toggles
    /// gun ⇄ melee; FIRE fires or swings depending on the weapon; ADS / R are disabled while the melee weapon is out.
    /// ADS is hold or toggle as set in Menu → Settings → Controls (touch setting, <see cref="ControlSettings"/>).
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        [SerializeField] VirtualJoystick moveStick;
        [SerializeField] TouchLookArea lookArea;
        [SerializeField] TouchButton fireButton;
        [SerializeField] TouchButton adsButton;
        [SerializeField] TouchButton reloadButton;
        [SerializeField] TouchButton jumpButton;
        [SerializeField] TouchButton postureButton;
        [SerializeField] TouchButton weaponButton;

        [Header("State display")]
        [SerializeField] PostureController posture;
        [SerializeField] WeaponSwitcher weapons;
        [SerializeField] Text postureLabel;
        [SerializeField] Text weaponLabel;
        [Tooltip("Tinted while sprinting (the stick knob).")]
        [SerializeField] Graphic sprintIndicator;
        [SerializeField] Color sprintColor = new Color(1f, 0.75f, 0.3f, 0.75f);

        [Header("Tuning (provisional)")]
        [Tooltip("Degrees of rotation for a drag across the full screen height.")]
        [SerializeField, Min(1f)] float lookDegreesPerScreenHeight = 140f;
        [Tooltip("Stick pushed forward to at least this starts sprinting.")]
        [SerializeField, Range(0.5f, 1f)] float sprintStartThreshold = 0.9f;
        [Tooltip("Sprinting continues until the stick drops below this (hysteresis against flicker at the boundary).")]
        [SerializeField, Range(0.3f, 1f)] float sprintReleaseThreshold = 0.75f;
        [Tooltip("Holding the posture button this long goes prone (or stands up from prone) instead of the tap action.")]
        [SerializeField, Min(0.1f)] float longPressTime = 0.45f;

        readonly AdsInput ads = new AdsInput();
        bool sprintLatched;
        bool postureWasHeld;
        bool postureLongFired;
        Color knobColor;
        FirstPersonMotor motor;

        /// <summary>ADS intent from the touch controls (after the hold / toggle setting).</summary>
        public bool AdsActive => ads.Active;
        public bool SprintLatched => sprintLatched;
        public float LongPressTime => longPressTime;

        void Awake()
        {
            if (sprintIndicator != null)
                knobColor = sprintIndicator.color;
            if (posture != null)
                motor = posture.GetComponent<FirstPersonMotor>();
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (!visible)
            {
                CancelAds();
                sprintLatched = false;
                postureWasHeld = postureLongFired = false;
            }
        }

        /// <summary>
        /// Lets go of every finger the widgets are tracking. Used when the app is backgrounded / loses focus:
        /// the release of a finger held at that moment may never arrive, which would leave FIRE or the stick held.
        /// ADS is turned off too (a new press is needed afterwards).
        /// </summary>
        public void ReleaseAll()
        {
            CancelAds();
            moveStick.Release();
            lookArea.Release();
            foreach (var button in new[] { fireButton, adsButton, reloadButton, jumpButton, postureButton, weaponButton })
                if (button != null)
                    button.Release();
            sprintLatched = false;
            postureWasHeld = postureLongFired = false;
        }

        public PlayerInputFrame Read()
        {
            bool gunOut = weapons == null || (weapons.Current == WeaponSlot.Gun && !weapons.IsSwitching);
            adsButton.SetInteractable(gunOut);
            reloadButton.SetInteractable(gunOut);
            // A finger that leaves the button keeps being tracked by it, so a hold ends wherever the finger lifts.
            bool adsOn = ads.Update(ControlSettings.GetAdsMode(InputMode.Touch), adsButton.IsHeld, adsButton.ConsumePressed(), gunOut);
            adsButton.SetLatched(adsOn);

            float degreesPerPixel = lookDegreesPerScreenHeight / Mathf.Max(1, Screen.height);
            Vector2 move = moveStick.Value;
            if (move.y >= sprintStartThreshold)
                sprintLatched = true;
            else if (move.y < sprintReleaseThreshold)
                sprintLatched = false;

            ReadPostureButton(out bool crouch, out bool prone);
            UpdateDisplay();

            return new PlayerInputFrame
            {
                Move = move,
                LookDegrees = lookArea.ConsumeDelta() * degreesPerPixel,
                Sprint = sprintLatched,
                JumpPressed = jumpButton.ConsumePressed(),
                FireHeld = fireButton.IsHeld,
                FirePressed = fireButton.ConsumePressed(),
                AdsActive = adsOn,
                ReloadPressed = gunOut && reloadButton.ConsumePressed(),
                CrouchPressed = crouch,
                PronePressed = prone,
                ToggleWeaponPressed = weaponButton.ConsumePressed(),
            };
        }

        /// <summary>
        /// Tap = crouch toggle, hold = prone toggle. A press that became a hold never also fires the tap.
        /// While sprinting, the press itself is the crouch request at once (= slide), and that press is then spent:
        /// keeping the finger down does not go prone and releasing it does not crouch again.
        /// </summary>
        void ReadPostureButton(out bool tap, out bool hold)
        {
            tap = hold = false;
            bool pressedSinceLastRead = postureButton.ConsumePressed();
            bool held = postureButton.IsHeld;
            bool newPress = (held && !postureWasHeld) || (!held && !postureWasHeld && pressedSinceLastRead);

            if (newPress && motor != null && motor.IsSprinting)
            {
                tap = true;
                postureLongFired = true; // spent: no hold, no release tap
                postureWasHeld = held;
                return;
            }
            if (held && !postureWasHeld)
                postureLongFired = false; // new press
            if (held && !postureLongFired && Time.time - postureButton.PressStartTime >= longPressTime)
            {
                hold = true; // fires while still held, so the player gets feedback without releasing
                postureLongFired = true;
            }
            if (!held && postureWasHeld && !postureLongFired)
                tap = true;
            else if (!held && !postureWasHeld && pressedSinceLastRead)
                tap = true; // pressed and released between two reads
            postureWasHeld = held;
        }

        void UpdateDisplay()
        {
            if (postureLabel != null && posture != null)
                postureLabel.text = posture.Current switch
                {
                    Posture.Crouching => "しゃがみ",
                    Posture.Prone => "伏せ",
                    Posture.Sliding => "スライド",
                    _ => "立ち",
                };
            if (weaponLabel != null && weapons != null)
                weaponLabel.text = weapons.Current == WeaponSlot.Gun ? "銃" : "ナイフ";
            if (sprintIndicator != null)
                sprintIndicator.color = sprintLatched ? sprintColor : knobColor;
        }

        /// <summary>ADS off; a finger still on the button has to lift and press again.</summary>
        public void CancelAds()
        {
            if (adsButton != null)
            {
                adsButton.Release();
                adsButton.SetLatched(false);
            }
            ads.Cancel(false);
        }

        /// <summary>Only real (or simulated) touches drive the touch widgets; mouse pointer events are ignored.</summary>
        internal static bool IsTouch(PointerEventData eventData) =>
            eventData is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch;
    }
}
