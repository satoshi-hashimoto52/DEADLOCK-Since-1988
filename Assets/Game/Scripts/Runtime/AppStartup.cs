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
            // The URP Rendering Debugger (Display Stats) opens at runtime from a multi-finger gesture, which happens
            // during normal touch play. Its runtime UI is switched off; the project's own dev frame-rate readout
            // (FrameRateMonitor) is separate and stays.
            UnityEngine.Rendering.DebugManager.instance.enableRuntimeUI = false;
        }
    }
}
