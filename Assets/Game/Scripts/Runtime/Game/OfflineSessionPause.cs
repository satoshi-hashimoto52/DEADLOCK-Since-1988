using Deadlock.UI;
using UnityEngine;

namespace Deadlock.Game
{
    /// <summary>
    /// Pauses the whole game while the menu is open — only in offline single-player scenes (training, firing range),
    /// which is why it is a separate component. Online matches will not have it: there the menu only blocks the
    /// local player's input (<see cref="Input.PlayerInputRouter.Block"/>) and the match keeps running.
    /// </summary>
    public sealed class OfflineSessionPause : MonoBehaviour
    {
        [SerializeField] GameMenu menu;

        void OnEnable()
        {
            menu.Opened += Pause;
            menu.Closed += Resume;
        }

        void OnDisable()
        {
            menu.Opened -= Pause;
            menu.Closed -= Resume;
            Resume();
        }

        void Pause() => Time.timeScale = 0f;

        void Resume() => Time.timeScale = 1f;
    }
}
