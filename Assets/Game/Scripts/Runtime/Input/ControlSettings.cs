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

    public enum TouchFireMode
    {
        /// <summary>FIRE shoots from the current stance while held (hip, or ADS if already aiming). Default.</summary>
        Normal,
        /// <summary>FIRE raises the sights first and starts shooting once fully aimed; the same finger aims.</summary>
        AdsLinked,
    }

    /// <summary>
    /// Per-device control preferences ("Menu → Settings → Controls"). Touch and desktop (PC) are stored separately and
    /// kept on this device (PlayerPrefs); never synced online — the other player only sees the actual ADS state.
    /// </summary>
    public static class ControlSettings
    {
        public const string TouchAdsKey = "deadlock.controls.adsMode.touch";
        public const string DesktopAdsKey = "deadlock.controls.adsMode.desktop";
        public const string TouchAdsDragAimKey = "deadlock.controls.touch.adsDragAim";
        public const string TouchFireModeKey = "deadlock.controls.touch.fireMode";
        public const AdsMode DefaultAdsMode = AdsMode.Hold;

        /// <summary>Raised after any control setting changes (held input and ADS are then cleared).</summary>
        public static event Action Changed;

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
            Changed?.Invoke();
        }

        /// <summary>Touch: a finger that starts on the ADS button also aims (drags the view). Default on.</summary>
        public static bool TouchAdsDragAim
        {
            get => GetInt(TouchAdsDragAimKey, 1) != 0;
            set => SetInt(TouchAdsDragAimKey, value ? 1 : 0);
        }

        /// <summary>Touch: what FIRE does with the gun out. Default normal (the previous behaviour).</summary>
        public static TouchFireMode TouchFire
        {
            get
            {
                int v = GetInt(TouchFireModeKey, (int)TouchFireMode.Normal);
                return Enum.IsDefined(typeof(TouchFireMode), v) ? (TouchFireMode)v : TouchFireMode.Normal;
            }
            set => SetInt(TouchFireModeKey, (int)value);
        }

        static int GetInt(string key, int fallback)
        {
            try { return PlayerPrefs.GetInt(key, fallback); }
            catch (Exception) { return fallback; }
        }

        static void SetInt(string key, int value)
        {
            if (GetInt(key, int.MinValue) == value)
                return;
            try
            {
                PlayerPrefs.SetInt(key, value);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK] Could not save control setting {key}: {e.Message}");
            }
            Changed?.Invoke();
        }

        /// <summary>Removes the saved control settings (not the touch layout).</summary>
        public static void ClearSaved()
        {
            PlayerPrefs.DeleteKey(TouchAdsKey);
            PlayerPrefs.DeleteKey(DesktopAdsKey);
            PlayerPrefs.DeleteKey(TouchAdsDragAimKey);
            PlayerPrefs.DeleteKey(TouchFireModeKey);
        }

        public static string Label(TouchFireMode mode) => mode == TouchFireMode.AdsLinked ? "ADS連動射撃" : "通常射撃";

        public static string Label(AdsMode mode) => mode == AdsMode.Toggle ? "切り替え" : "長押し";
    }
}
