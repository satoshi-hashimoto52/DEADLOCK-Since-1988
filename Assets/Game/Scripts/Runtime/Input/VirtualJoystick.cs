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
        [Tooltip("Knob travel in canvas units.")]
        [SerializeField, Min(1f)] float radius = 110f;
        [SerializeField, Range(0f, 0.5f)] float deadZone = 0.08f;

        RectTransform zone;
        Vector3 restPosition;
        int pointerId = NoPointer;

        public Vector2 Value { get; private set; }
        public bool IsActive => pointerId != NoPointer;

        void Awake()
        {
            zone = (RectTransform)transform;
            restPosition = stickBase.localPosition;
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

        void Release()
        {
            pointerId = NoPointer;
            Value = Vector2.zero;
            if (stickBase != null)
                stickBase.localPosition = restPosition;
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
