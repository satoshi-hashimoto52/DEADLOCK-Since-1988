using System.IO;
using Deadlock.Game;
using Deadlock.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). Stage 37F: a
    /// button in the home's settings screen that opens the online verification scene (dedicated server on the same LAN),
    /// shown only when that scene is in the build (the development export with -deadlockOnline). Not the game's online
    /// entrance: matchmaking from the home screen is not decided yet.
    /// </summary>
    public static class OnlineDevEntry
    {
        public const string ButtonName = "OnlineDevEntry";
        /// <summary>The online verification scene (Deadlock.Online's OnlineProtocol.SceneName).</summary>
        public const string SceneName = "Online_Dedicated";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static bool autoStarted;

        /// <summary>
        /// An automated device check (Tools/ios-device-check.py) puts its command line in the app's data folder
        /// (online_dev/args.txt, the online scene reads it): the home then opens the online scene once by itself.
        /// </summary>
        static bool AutomatedCheck()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "online_dev", "args.txt");
                return Application.isMobilePlatform && File.Exists(path) && File.ReadAllText(path).Contains("-online-client");
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameScenes.Home || !Debug.isDebugBuild || !Application.CanStreamedLevelBeLoaded(SceneName))
                return;
            if (!autoStarted && AutomatedCheck())
            {
                autoStarted = true;
                SceneManager.LoadScene(SceneName);
                return;
            }
            var menu = Object.FindAnyObjectByType<Deadlock.Home.HomeMenu>();
            var panel = menu != null ? menu.PanelObject("SettingsPanel") : null;
            var window = panel != null ? panel.transform.Find("SafeArea/Window") as RectTransform : null;
            if (window == null || window.Find(ButtonName) != null)
                return;
            var b = UiBuild.Button(window, ButtonName, "オンライン検証（開発用・LAN）", new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(460f, 56f),
                () => SceneManager.LoadScene(SceneName), 20);
            ((Image)b.targetGraphic).color = new Color(0.1f, 0.2f, 0.4f, 0.95f);
        }
    }
}
