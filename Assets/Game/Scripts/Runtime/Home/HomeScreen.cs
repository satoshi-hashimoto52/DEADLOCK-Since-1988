using Deadlock.Game;
using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>
    /// Home scene entry (first scene of the build). Leaving the home happens only through the home menu
    /// (<see cref="HomeMenu"/>); the interim "tap anywhere to continue to the firing range" was removed (D-047).
    /// </summary>
    public sealed class HomeScreen : MonoBehaviour
    {
        /// <summary>Loads a game scene from the home menu.</summary>
        public static void Load(string sceneName) => GameScenes.Load(sceneName);

        /// <summary>Back to the home screen (in-game menu → Home).</summary>
        public static void Return() => GameScenes.Load(GameScenes.Home);
    }
}
