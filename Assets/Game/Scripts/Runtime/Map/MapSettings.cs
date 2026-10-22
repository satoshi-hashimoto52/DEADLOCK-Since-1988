using System;
using UnityEngine;

namespace Deadlock.Map
{
    /// <summary>
    /// "Show the floor map" (Menu → Settings → Controls), saved on this device (PlayerPrefs, its own key: the control,
    /// layout, transparency, ADS and skin settings are not touched). Default on. Off only hides the map; detection,
    /// enemies and combat do not change.
    /// </summary>
    public static class MapSettings
    {
        public const string MinimapVisibleKey = "deadlock.hud.minimap.visible";
        public const bool DefaultMinimapVisible = true;

        public static event Action Changed;

        public static bool MinimapVisible
        {
            get
            {
                try { return PlayerPrefs.GetInt(MinimapVisibleKey, DefaultMinimapVisible ? 1 : 0) != 0; }
                catch (Exception) { return DefaultMinimapVisible; }
            }
            set
            {
                // An explicit choice is always saved (even if it equals the default).
                if (PlayerPrefs.HasKey(MinimapVisibleKey) && MinimapVisible == value)
                    return;
                try
                {
                    PlayerPrefs.SetInt(MinimapVisibleKey, value ? 1 : 0);
                    PlayerPrefs.Save();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DEADLOCK] Could not save the minimap setting: {e.Message}");
                }
                Changed?.Invoke();
            }
        }

        public static void ClearSaved() => PlayerPrefs.DeleteKey(MinimapVisibleKey);
    }
}
