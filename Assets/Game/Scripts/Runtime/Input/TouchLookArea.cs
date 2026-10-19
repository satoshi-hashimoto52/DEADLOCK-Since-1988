using UnityEngine;
using UnityEngine.EventSystems;

namespace Deadlock.Input
{
    /// <summary>
    /// Transparent full-screen area behind all touch widgets. One finger at a time drags the view.
    /// </summary>
    public sealed class TouchLookArea : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        int pointerId = NoPointer;
        Vector2 pendingDelta;

        public bool IsActive => pointerId != NoPointer;

        void OnDisable()
        {
            pointerId = NoPointer;
            pendingDelta = Vector2.zero;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsActive || !TouchControls.IsTouch(eventData))
                return;
            pointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId)
                pendingDelta += eventData.delta;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId)
                pointerId = NoPointer;
        }

        /// <summary>Adds a drag delta coming from another widget (e.g. dragging while holding fire).</summary>
        public void AddDelta(Vector2 screenDelta) => pendingDelta += screenDelta;

        /// <summary>Returns the screen-pixel delta accumulated since the last call.</summary>
        public Vector2 ConsumeDelta()
        {
            Vector2 delta = pendingDelta;
            pendingDelta = Vector2.zero;
            return delta;
        }
    }
}
