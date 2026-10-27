using System;
using UnityEngine;

namespace Deadlock.Home
{
    /// <summary>Which home panels share one transparency value.</summary>
    public enum HomePanelTarget
    {
        MainButton,
        Points,
        Settings,
        Crate,
        Window,
        Row,
    }

    /// <summary>
    /// Transparency of the centre (fill) of the 9-slice home panels, per target, in percent (0 = opaque, 100 = clear;
    /// 0–50 in 1 % steps). The brass frame and rivets are a separate layer and stay opaque. Used while the panel images
    /// are shown (<see cref="HomeMenu.SetPanelSkin"/>, the normal home); Unity-drawn panels keep their colours.
    /// Release builds use the user-approved 50% defaults for all six targets. The Editor and development builds use the
    /// development overrides saved on the device by the panel tuner (PlayerPrefs <see cref="KeyPrefix"/>), else the
    /// defaults; a release build never reads or writes those keys.
    /// </summary>
    public static class HomePanelLook
    {
        public const int MinPercent = 0;
        public const int MaxPercent = 50;
        public const string KeyPrefix = "deadlock.dev.home.panelTransparency.";

        public static readonly HomePanelTarget[] Targets = (HomePanelTarget[])Enum.GetValues(typeof(HomePanelTarget));

        /// <summary>Raised after a value changes (the panels re-colour at once).</summary>
        public static event Action Changed;

        public static int Default(HomePanelTarget target) => target switch
        {
            HomePanelTarget.MainButton => 50,
            HomePanelTarget.Points => 50,
            HomePanelTarget.Settings => 50,
            HomePanelTarget.Crate => 50,
            HomePanelTarget.Window => 50,
            _ => 50, // Row
        };

        public static string DisplayName(HomePanelTarget target) => target switch
        {
            HomePanelTarget.MainButton => "メインボタン",
            HomePanelTarget.Points => "ポイント表示",
            HomePanelTarget.Settings => "Settings",
            HomePanelTarget.Crate => "Crate",
            HomePanelTarget.Window => "サブメニューの窓",
            _ => "サブメニューの項目",
        };

        public static string Key(HomePanelTarget target) => KeyPrefix + target;

        public static int Clamp(int percent) => Mathf.Clamp(percent, MinPercent, MaxPercent);

        /// <summary>True where the development overrides are read (Editor, development builds).</summary>
        public static bool OverridesEnabled =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        public static int Get(HomePanelTarget target) =>
            OverridesEnabled ? Clamp(PlayerPrefs.GetInt(Key(target), Default(target))) : Default(target);

        /// <summary>Saves a development override (Editor / development builds only; ignored in a release build).</summary>
        public static void Set(HomePanelTarget target, int percent)
        {
            if (!OverridesEnabled)
                return;
            PlayerPrefs.SetInt(Key(target), Clamp(percent));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>This target back to its default (the override is removed).</summary>
        public static void Reset(HomePanelTarget target)
        {
            if (!OverridesEnabled)
                return;
            PlayerPrefs.DeleteKey(Key(target));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Back to the defaults (the saved values are removed).</summary>
        public static void ResetAll()
        {
            if (!OverridesEnabled)
                return;
            foreach (var t in Targets)
                PlayerPrefs.DeleteKey(Key(t));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Alpha of the fill for a transparency in percent.</summary>
        public static float FillAlpha(int percent) => 1f - Clamp(percent) / 100f;
    }
}
