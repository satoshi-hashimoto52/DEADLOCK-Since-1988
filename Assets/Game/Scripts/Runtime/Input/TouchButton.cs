using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Deadlock.Input
{
    /// <summary>
    /// Hold/press button for touch. Handles its own drag events so a finger starting here never rotates the view,
    /// unless <see cref="dragLooksWith"/> is set (used by the fire button so the player can aim while firing).
    /// </summary>
    public sealed class TouchButton : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        [SerializeField] Graphic graphic;
        [SerializeField] TouchLookArea dragLooksWith;
        [SerializeField] Color normalColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color pressedColor = new Color(1f, 0.85f, 0.4f, 0.75f);

        int pointerId = NoPointer;
        int pressCount;
        bool latched;

        public bool IsHeld => pointerId != NoPointer;

        void OnDisable()
        {
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

        /// <summary>Keeps the pressed tint while a toggle (e.g. ADS) is on.</summary>
        public void SetLatched(bool on)
        {
            latched = on;
            RefreshColor();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !TouchControls.IsTouch(eventData))
                return;
            pointerId = eventData.pointerId;
            pressCount++;
            RefreshColor();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId && dragLooksWith != null)
                dragLooksWith.AddDelta(eventData.delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId)
                return;
            pointerId = NoPointer;
            RefreshColor();
        }

        void RefreshColor()
        {
            if (graphic != null)
                graphic.color = IsHeld || latched ? pressedColor : normalColor;
        }
    }
}
