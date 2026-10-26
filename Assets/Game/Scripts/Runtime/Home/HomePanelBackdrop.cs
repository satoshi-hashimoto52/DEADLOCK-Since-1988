using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Deadlock.Home
{
    /// <summary>
    /// The dimmed full-screen layer behind a home sub-screen: it takes every tap / click outside the window (so nothing
    /// behind it, such as the home buttons, receives it) and a completed tap closes the sub-screen. The window is a
    /// sibling drawn above it, not a child, so taps inside the window never reach it. A click only reaches the object the
    /// same pointer pressed, and nothing reaches it once it is hidden, so extra fingers cannot close twice or press what
    /// was behind.
    /// </summary>
    public sealed class HomePanelBackdrop : MonoBehaviour, IPointerClickHandler
    {
        public Action Tapped;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !isActiveAndEnabled)
                return;
            Tapped?.Invoke();
        }
    }
}
