using System;
using UnityEngine;

namespace Deadlock.Input
{
    public enum AdsMode
    {
        /// <summary>ADS while the button is held (default).</summary>
        Hold,
        /// <summary>One press turns ADS on, the next press turns it off.</summary>
        Toggle,
    }

    /// <summary>
    /// Per-device control preferences ("Menu → Settings → Controls"). Touch and desktop (PC) are stored separately and
    /// kept on this device (PlayerPrefs); never synced online — the other player only sees the actual ADS state.
    /// </summary>
    public static class ControlSettings
    {
        public const string TouchAdsKey = "deadlock.controls.adsMode.touch";
        public const string DesktopAdsKey = "deadlock.controls.adsMode.desktop";
        public const AdsMode DefaultAdsMode = AdsMode.Hold;

        /// <summary>Raised after a mode changes (input mode it applies to, new value).</summary>
        public static event Action<InputMode, AdsMode> AdsModeChanged;

        static string Key(InputMode mode) => mode == InputMode.Touch ? TouchAdsKey : DesktopAdsKey;

        public static AdsMode GetAdsMode(InputMode mode)
        {
            try
            {
                int value = PlayerPrefs.GetInt(Key(mode), (int)DefaultAdsMode);
                return Enum.IsDefined(typeof(AdsMode), value) ? (AdsMode)value : DefaultAdsMode;
            }
            catch (Exception)
            {
                return DefaultAdsMode; // storage unavailable: default
            }
        }

        public static void SetAdsMode(InputMode mode, AdsMode value)
        {
            if (GetAdsMode(mode) == value)
                return;
            try
            {
                PlayerPrefs.SetInt(Key(mode), (int)value);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK] Could not save the ADS mode: {e.Message}");
            }
            AdsModeChanged?.Invoke(mode, value);
        }

        public static void ClearSaved()
        {
            PlayerPrefs.DeleteKey(TouchAdsKey);
            PlayerPrefs.DeleteKey(DesktopAdsKey);
        }

        public static string Label(AdsMode mode) => mode == AdsMode.Toggle ? "切り替え" : "長押し";
    }
}
