using UnityEditor;

namespace Deadlock.Editor
{
    /// <summary>
    /// Switches the development weapon balance lab (Deadlock.Dev WeaponBalanceLab) on or off for Play Mode in the Editor.
    /// Development builds always have it; release builds never do.
    /// </summary>
    public static class WeaponBalanceLabMenu
    {
        const string Path = "DEADLOCK/Dev/Weapon Balance Lab";
        const string Key = "DEADLOCK.WeaponBalanceLab"; // WeaponBalanceLab.EditorToggleKey

        [MenuItem(Path)]
        static void Toggle() => EditorPrefs.SetBool(Key, !EditorPrefs.GetBool(Key, false));

        [MenuItem(Path, true)]
        static bool Validate()
        {
            Menu.SetChecked(Path, EditorPrefs.GetBool(Key, false));
            return true;
        }
    }
}
