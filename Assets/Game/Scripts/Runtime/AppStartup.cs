using UnityEngine;

namespace Deadlock
{
    /// <summary>App-wide runtime settings applied before the first scene loads.</summary>
    public static class AppStartup
    {
        /// <summary>Provisional prototype target (mobile Unity defaults to 30 fps). Not a decided performance spec.</summary>
        public const int TargetFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            Application.targetFrameRate = TargetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep; // touch-only play would otherwise dim / lock mid-session
        }
    }
}
