using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Deadlock.Input
{
    /// <summary>
    /// Transparent full-screen area behind all touch widgets, and the single place where touch look input is
    /// collected. Fingers that may turn the view (on this area, and on FIRE / ADS when they aim) register here in the
    /// order they went down; only the earliest registered finger turns the view. When it lifts, the next one takes
    /// over from where it is (only its later movement counts), so nothing is added twice and the view does not jump.
    /// Fingers that started on other buttons (posture, jump, reload, ...) never register.
    /// </summary>
    public sealed class TouchLookArea : MonoBehaviour,
        IInitializePotentialDragHandler, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const int NoPointer = int.MinValue;

        readonly List<int> lookers = new List<int>();
        readonly HashSet<int> own = new HashSet<int>();
        Vector2 pendingDelta;

        /// <summary>A finger on the empty area is turning (or waiting to turn) the view.</summary>
        public bool IsActive => own.Count > 0;
        /// <summary>The finger whose movement turns the view now, or int.MinValue.</summary>
        public int LookPointer => lookers.Count > 0 ? lookers[0] : NoPointer;
        public int LookerCount => lookers.Count;

        void OnDisable() => Release();

        /// <summary>Drops every tracked finger and any pending drag (also used when the app loses focus).</summary>
        public void Release()
        {
            lookers.Clear();
            own.Clear();
            pendingDelta = Vector2.zero;
        }

        /// <summary>A finger that may turn the view went down (registration order decides who turns it).</summary>
        public void BeginLook(int pointerId)
        {
            if (!lookers.Contains(pointerId))
                lookers.Add(pointerId);
        }

        public void EndLook(int pointerId) => lookers.Remove(pointerId);

        /// <summary>Movement of a registered finger; only the current look finger's movement is used.</summary>
        public void AddDelta(int pointerId, Vector2 screenDelta)
        {
            if (lookers.Count > 0 && lookers[0] == pointerId)
                pendingDelta += screenDelta;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!TouchControls.IsTouch(eventData))
                return;
            own.Add(eventData.pointerId);
            BeginLook(eventData.pointerId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (own.Contains(eventData.pointerId))
                AddDelta(eventData.pointerId, eventData.delta);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (own.Remove(eventData.pointerId))
                EndLook(eventData.pointerId);
        }

        /// <summary>Returns the screen-pixel delta accumulated since the last call.</summary>
        public Vector2 ConsumeDelta()
        {
            Vector2 delta = pendingDelta;
            pendingDelta = Vector2.zero;
            return delta;
        }
    }
}
