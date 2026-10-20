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
    /// Standard mobile controls (2026-10-20): ADS button = sights on / off (toggle), dragging the empty right side
    /// or FIRE aims (also while aiming down sights), FIRE = normal fire. Hold ADS, aiming with the ADS button and
    /// ADS-linked fire stay available as options. Defaults apply only where nothing is saved, so a player's saved
    /// settings (and the touch layout, stored separately) are never overwritten.
    ///
    /// Per-device control preferences ("Menu → Settings → Controls"). Touch and desktop (PC) are stored separately and
    /// kept on this device (PlayerPrefs); never synced online — the other player only sees the actual ADS state.
    /// </summary>
    public static class ControlSettings
    {
        public const string TouchAdsKey = "deadlock.controls.adsMode.touch";
        public const string DesktopAdsKey = "deadlock.controls.adsMode.desktop";
        public const string TouchAdsDragAimKey = "deadlock.controls.touch.adsDragAim";
        public const string TouchFireModeKey = "deadlock.controls.touch.fireMode";
        /// <summary>Default for the PC right button (unchanged).</summary>
        public const AdsMode DefaultAdsMode = AdsMode.Hold;
        /// <summary>Default for the touch ADS button: sights on / off.</summary>
        public const AdsMode DefaultTouchAdsMode = AdsMode.Toggle;
        public const bool DefaultTouchAdsDragAim = false;
        public const TouchFireMode DefaultTouchFire = TouchFireMode.Normal;

        public static AdsMode DefaultAds(InputMode mode) => mode == InputMode.Touch ? DefaultTouchAdsMode : DefaultAdsMode;

        /// <summary>Raised after any control setting changes (held input and ADS are then cleared).</summary>
        public static event Action Changed;

        static string Key(InputMode mode) => mode == InputMode.Touch ? TouchAdsKey : DesktopAdsKey;

        public static AdsMode GetAdsMode(InputMode mode)
        {
            try
            {
                int value = PlayerPrefs.GetInt(Key(mode), (int)DefaultAds(mode));
                return Enum.IsDefined(typeof(AdsMode), value) ? (AdsMode)value : DefaultAds(mode);
            }
            catch (Exception)
            {
                return DefaultAds(mode); // storage unavailable: default
            }
        }

        public static void SetAdsMode(InputMode mode, AdsMode value)
        {
            // An explicit choice is always saved (even if it equals today's default), so it survives default changes.
            if (PlayerPrefs.HasKey(Key(mode)) && GetAdsMode(mode) == value)
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

        /// <summary>Touch: a finger that starts on the ADS button also aims (drags the view). Default off (option).</summary>
        public static bool TouchAdsDragAim
        {
            get => GetInt(TouchAdsDragAimKey, DefaultTouchAdsDragAim ? 1 : 0) != 0;
            set => SetInt(TouchAdsDragAimKey, value ? 1 : 0);
        }

        /// <summary>Touch: what FIRE does with the gun out. Default normal fire.</summary>
        public static TouchFireMode TouchFire
        {
            get
            {
                int v = GetInt(TouchFireModeKey, (int)DefaultTouchFire);
                return Enum.IsDefined(typeof(TouchFireMode), v) ? (TouchFireMode)v : DefaultTouchFire;
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

        /// <summary>Removes the saved control settings, so the standard values apply again (the touch layout is separate and kept).</summary>
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
