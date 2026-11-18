using System;
using UnityEngine;

namespace Deadlock.Audio
{
    /// <summary>The volume groups of the game's sound (Stage 39). Master scales all three.</summary>
    public enum SoundGroup
    {
        Bgm,
        /// <summary>Sounds of the game world and the player: guns, footsteps, damage, match cues.</summary>
        Se,
        /// <summary>Menu and screen sounds (buttons, panels, gacha).</summary>
        Ui,
    }

    /// <summary>
    /// The player's volume settings ("設定 → サウンド", Stage 39): Master, BGM, SE and UI in percent (0–100) and a mute,
    /// kept on this device (PlayerPrefs) and never synced online. 0 % is silent; 100 % plays a sound at its own level
    /// (the gain never goes above 1, so nothing is louder than the sound file). Defaults apply only where nothing is
    /// saved. The defaults are provisional for the prototype (BGM lower than the information sounds of play).
    /// </summary>
    public static class SoundSettings
    {
        public const string MasterKey = "deadlock.audio.master";
        public const string BgmKey = "deadlock.audio.bgm";
        public const string SeKey = "deadlock.audio.se";
        public const string UiKey = "deadlock.audio.ui";
        public const string MuteKey = "deadlock.audio.mute";
        public static readonly string[] Keys = { MasterKey, BgmKey, SeKey, UiKey, MuteKey };

        public const int DefaultMaster = 100;
        public const int DefaultBgm = 60;
        public const int DefaultSe = 100;
        public const int DefaultUi = 80;

        /// <summary>Raised after any volume setting changes (playing sounds follow at once).</summary>
        public static event Action Changed;

        public static int Master
        {
            get => Get(MasterKey, DefaultMaster);
            set => Set(MasterKey, value);
        }

        public static int Volume(SoundGroup group) => Get(KeyOf(group), DefaultOf(group));

        public static void SetVolume(SoundGroup group, int percent) => Set(KeyOf(group), percent);

        public static bool Muted
        {
            get => GetInt(MuteKey, 0) != 0;
            set => SetInt(MuteKey, value ? 1 : 0);
        }

        /// <summary>The gain (0–1) of a group as set: muted or 0 % is 0, Master and the group both at 100 % is 1.</summary>
        public static float Gain(SoundGroup group) => Muted ? 0f : Master / 100f * (Volume(group) / 100f);

        public static string KeyOf(SoundGroup group) => group switch
        {
            SoundGroup.Bgm => BgmKey,
            SoundGroup.Ui => UiKey,
            _ => SeKey,
        };

        public static int DefaultOf(SoundGroup group) => group switch
        {
            SoundGroup.Bgm => DefaultBgm,
            SoundGroup.Ui => DefaultUi,
            _ => DefaultSe,
        };

        /// <summary>Removes the saved volume settings, so the defaults apply again.</summary>
        public static void ClearSaved()
        {
            foreach (var key in Keys)
                PlayerPrefs.DeleteKey(key);
            Changed?.Invoke();
        }

        static int Get(string key, int fallback) => Mathf.Clamp(GetInt(key, fallback), 0, 100);

        static void Set(string key, int percent) => SetInt(key, Mathf.Clamp(percent, 0, 100));

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
                Debug.LogWarning($"[DEADLOCK] Could not save sound setting {key}: {e.Message}");
            }
            Changed?.Invoke();
        }
    }
}
