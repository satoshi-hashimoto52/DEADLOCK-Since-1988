using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deadlock.Game
{
    /// <summary>Prototype scenes and switching between them.</summary>
    public static class GameScenes
    {
        public const string FiringRange = "Proto_FiringRange";
        public const string Training = "Proto_Training";

        public static bool IsTraining => SceneManager.GetActiveScene().name == Training;

        /// <summary>Loads a scene fresh (also used to retry: everything is re-created from the scene).</summary>
        public static void Load(string sceneName)
        {
            Time.timeScale = 1f; // never carry a paused clock into the next scene
            SceneManager.LoadScene(sceneName);
        }

        public static void ReloadCurrent() => Load(SceneManager.GetActiveScene().name);
    }
}
