using System;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// The skins chosen on this device (PlayerPrefs, like the control settings): one weapon skin per weapon id and one
    /// player (hand / arm) skin for every weapon, so switching weapons keeps the hands as chosen. Nothing saved =
    /// standard. Looks only; never sent online (other players' appearance is a later step).
    /// </summary>
    public static class SkinSelection
    {
        public const string PlayerKey = "deadlock.skins.player";
        public const string WeaponKeyPrefix = "deadlock.skins.weapon.";

        /// <summary>Raised after a choice changes; the first-person views re-colour themselves.</summary>
        public static event Action Changed;

        public static string WeaponKey(string weaponId) => WeaponKeyPrefix + weaponId;

        public static string PlayerSkinId
        {
            get => GetString(PlayerKey);
            set => SetString(PlayerKey, value);
        }

        public static string GetWeaponSkinId(string weaponId) => string.IsNullOrEmpty(weaponId) ? SkinCatalog.StandardId : GetString(WeaponKey(weaponId));

        public static void SetWeaponSkinId(string weaponId, string skinId)
        {
            if (!string.IsNullOrEmpty(weaponId))
                SetString(WeaponKey(weaponId), skinId);
        }

        /// <summary>Back to the drawn colours: the player skin and the given weapons' skins (saved as "standard").</summary>
        public static void ResetToStandard(params string[] weaponIds)
        {
            PlayerSkinId = SkinCatalog.StandardId;
            foreach (var id in weaponIds)
                SetWeaponSkinId(id, SkinCatalog.StandardId);
        }

        /// <summary>Removes the saved choices (tests); the standard look applies again.</summary>
        public static void ClearSaved(params string[] weaponIds)
        {
            PlayerPrefs.DeleteKey(PlayerKey);
            foreach (var id in weaponIds)
                PlayerPrefs.DeleteKey(WeaponKey(id));
            Changed?.Invoke();
        }

        static string GetString(string key)
        {
            try
            {
                string value = PlayerPrefs.GetString(key, SkinCatalog.StandardId);
                return string.IsNullOrEmpty(value) ? SkinCatalog.StandardId : value;
            }
            catch (Exception)
            {
                return SkinCatalog.StandardId; // storage unavailable: standard
            }
        }

        static void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                value = SkinCatalog.StandardId;
            if (PlayerPrefs.HasKey(key) && GetString(key) == value)
                return;
            try
            {
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
                Debug.Log($"[DEADLOCK][skin] saved {key} = {value}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK] Could not save skin choice {key}: {e.Message}");
            }
            Changed?.Invoke();
        }
    }
}
