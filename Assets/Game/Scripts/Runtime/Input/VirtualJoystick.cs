using UnityEngine;
using UnityEngine.EventSystems;

namespace Deadlock.Input
{
    /// <summary>
    /// Floating stick: the base appears where the finger lands inside this zone and the knob follows the finger.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        [SerializeField] RectTransform stickBase;
        [SerializeField] RectTransform knob;
        [Tooltip("Knob travel as a fraction of the stick base width (110 units for the default 280-unit base).")]
        [SerializeField, Range(0.1f, 1f)] float radiusFraction = 0.3929f;
        [SerializeField, Range(0f, 0.5f)] float deadZone = 0.08f;

        RectTransform zone;
        int pointerId = NoPointer;

        public Vector2 Value { get; private set; }
        public bool IsActive => pointerId != NoPointer;

        void Awake()
        {
            zone = (RectTransform)transform;
        }

        void OnDisable() => Release();

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsActive || !TouchControls.IsTouch(eventData))
                return;
            pointerId = eventData.pointerId;
            stickBase.localPosition = ToLocal(eventData);
            knob.localPosition = Vector3.zero;
            Value = Vector2.zero;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId)
                return;
            float radius = Mathf.Max(1f, stickBase.rect.width * radiusFraction);
            Vector2 offset = Vector2.ClampMagnitude(ToLocal(eventData) - (Vector2)stickBase.localPosition, radius);
            knob.localPosition = offset;
            Vector2 value = offset / radius;
            Value = value.magnitude < deadZone ? Vector2.zero : value;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId)
                Release();
        }

        /// <summary>Drops the tracked finger (also used when the app loses focus).</summary>
        public void Release()
        {
            pointerId = NoPointer;
            Value = Vector2.zero;
            if (stickBase != null)
                stickBase.anchoredPosition = Vector2.zero; // rest point = the base's anchor (set by the layout)
            if (knob != null)
                knob.localPosition = Vector3.zero;
        }

        Vector2 ToLocal(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, eventData.position, eventData.pressEventCamera, out Vector2 local);
            return local;
        }
    }
}
