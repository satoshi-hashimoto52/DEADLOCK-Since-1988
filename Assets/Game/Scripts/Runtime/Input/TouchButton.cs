using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Deadlock.Input
{
    /// <summary>
    /// Hold/press button for touch. Handles its own drag events so a finger starting here never rotates the view,
    /// unless <see cref="dragLooksWith"/> is set (FIRE always, ADS when the setting allows): then the finger is
    /// registered with the look area, which decides whether it is the one turning the view.
    /// </summary>
    public sealed class TouchButton : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        [Tooltip("The plate (and the touch area).")]
        [SerializeField] Graphic graphic;
        [Tooltip("Icon drawn on the plate. Display only: Raycast Target off, so the plate alone takes touches.")]
        [SerializeField] Image icon;
        [Tooltip("Ring shown while the button's state is on (e.g. ADS active).")]
        [SerializeField] Graphic activeRing;
        [SerializeField] TouchLookArea dragLooksWith;
        [SerializeField] Color normalColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color pressedColor = new Color(1f, 0.85f, 0.4f, 0.75f);
        [SerializeField] Color activeColor = new Color(1f, 0.6f, 0.15f, 0.85f);
        [SerializeField] Color disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.18f);
        [SerializeField] Color iconColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] Color iconActiveColor = new Color(0.12f, 0.08f, 0.04f, 1f);
        [SerializeField] Color iconDisabledColor = new Color(1f, 1f, 1f, 0.3f);

        int pointerId = NoPointer;
        bool looking;
        System.Func<bool> lookEnabled;
        int pressCount;
        bool latched;
        bool interactable = true;

        public bool IsHeld => pointerId != NoPointer;
        /// <summary>The "on" display state (e.g. ADS active), as set by <see cref="SetLatched"/>.</summary>
        public bool IsLatched => latched;
        public Image Icon => icon;
        public bool Interactable => interactable;
        /// <summary>Time.time when the current press began.</summary>
        public float PressStartTime { get; private set; }

        void Awake() => RefreshColor();

        void OnDisable() => Release();

        /// <summary>Drops the held finger and pending presses (also used when the app loses focus).</summary>
        public void Release()
        {
            EndLook();
            pointerId = NoPointer;
            pressCount = 0;
            RefreshColor();
        }

        /// <summary>Returns true once per press.</summary>
        public bool ConsumePressed()
        {
            bool pressed = pressCount > 0;
            pressCount = 0;
            return pressed;
        }

        /// <summary>Shows the "on" state (active plate colour and ring), e.g. while ADS is actually active.</summary>
        public void SetLatched(bool on)
        {
            if (latched == on)
                return;
            latched = on;
            RefreshColor();
        }

        /// <summary>Changes the icon (e.g. gun / knife, current posture). Display only.</summary>
        public void SetIcon(Sprite sprite)
        {
            if (icon != null && icon.sprite != sprite)
                icon.sprite = sprite;
        }

        /// <summary>Disabled buttons look greyed out and ignore new presses (a held press is released).</summary>
        public void SetInteractable(bool value)
        {
            if (interactable == value)
                return;
            interactable = value;
            if (!value)
            {
                EndLook();
                pointerId = NoPointer;
                pressCount = 0;
            }
            RefreshColor();
        }

        /// <summary>Lets a finger starting on this button aim, when <paramref name="enabled"/> says so at the press.</summary>
        public void SetDragLook(TouchLookArea area, System.Func<bool> enabled)
        {
            dragLooksWith = area;
            lookEnabled = enabled;
        }

        void EndLook()
        {
            if (looking && dragLooksWith != null)
                dragLooksWith.EndLook(pointerId);
            looking = false;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !interactable || !TouchControls.IsTouch(eventData))
                return;
            pointerId = eventData.pointerId;
            looking = dragLooksWith != null && (lookEnabled == null || lookEnabled());
            if (looking)
                dragLooksWith.BeginLook(pointerId);
            pressCount++;
            PressStartTime = Time.time;
            RefreshColor();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId && looking)
                dragLooksWith.AddDelta(pointerId, eventData.delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId)
                return;
            EndLook();
            pointerId = NoPointer;
            RefreshColor();
        }

        // Normal / pressed / on (latched) / disabled: plate colour, the ring and the icon's colour and opacity.
        void RefreshColor()
        {
            if (graphic != null)
                graphic.color = !interactable ? disabledColor : latched ? activeColor : IsHeld ? pressedColor : normalColor;
            if (activeRing != null)
                activeRing.enabled = interactable && latched;
            if (icon != null)
                icon.color = !interactable ? iconDisabledColor : latched ? iconActiveColor : iconColor;
        }
    }
}
