using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Deadlock.UI
{
    /// <summary>Drag handle used by <see cref="TouchLayoutEditor"/> (works with touch and mouse).</summary>
    public sealed class LayoutHandle : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IDragHandler
    {
        public Action Pressed;
        public Action<Vector2> Dragged;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData.delta);
    }
}
