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

        public static void ClearSaved()
        {
            PlayerPrefs.DeleteKey(MinimapVisibleKey);
            PlayerPrefs.DeleteKey(MinimapLayoutKey);
        }

        /// <summary>Where the player put the map (Settings → Control layout); kept apart from the touch layout.</summary>
        public const string MinimapLayoutKey = "deadlock.hud.minimap.layout.v1";

        /// <summary>The saved place and size; false when the player never placed the map (first placement is automatic).</summary>
        public static bool TryLoadPlacement(out MiniMapPlacement placement)
        {
            placement = default;
            try
            {
                string json = PlayerPrefs.GetString(MinimapLayoutKey, "");
                if (string.IsNullOrEmpty(json))
                    return false;
                placement = JsonUtility.FromJson<MiniMapPlacement>(json);
                return placement.IsValid;
            }
            catch (Exception)
            {
                return false; // broken data: placed automatically again
            }
        }

        public static void SavePlacement(MiniMapPlacement placement)
        {
            try
            {
                PlayerPrefs.SetString(MinimapLayoutKey, JsonUtility.ToJson(placement));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK] Could not save the minimap layout: {e.Message}");
            }
        }

        public static void ClearPlacement() => PlayerPrefs.DeleteKey(MinimapLayoutKey);
    }

    /// <summary>Place and size of the floor map: centre normalised to the Safe Area (0..1, y up) and a size multiplier.</summary>
    [Serializable]
    public struct MiniMapPlacement
    {
        public Vector2 center;
        public float scale;

        public bool IsValid => scale > 0f && !float.IsNaN(center.x) && !float.IsNaN(center.y) && !float.IsNaN(scale);
    }
}
