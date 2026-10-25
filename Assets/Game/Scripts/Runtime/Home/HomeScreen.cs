using Deadlock.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadlock.Home
{
    /// <summary>
    /// Home scene entry (first scene of the build). Until the home menu exists, a tap / click anywhere (or Enter /
    /// Space) continues to the firing range, where the in-game menu reaches training, the ruins test and the LAN
    /// test as before. The hint text is a placeholder for that interim step, not part of the home art.
    /// </summary>
    public sealed class HomeScreen : MonoBehaviour
    {
        [Tooltip("Scene loaded by the interim tap-to-continue.")]
        public string continueScene = GameScenes.FiringRange;
        [Tooltip("Ignore input for this long after the scene starts (a press carried over from launch).")]
        public float inputDelay = 0.3f;

        bool leaving;

        void Update()
        {
            if (leaving || Time.timeSinceLevelLoad < inputDelay)
                return;
            bool pressed = (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
                || (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame));
            if (pressed)
                Continue();
        }

        public void Continue()
        {
            leaving = true;
            GameScenes.Load(continueScene);
        }
    }
}
