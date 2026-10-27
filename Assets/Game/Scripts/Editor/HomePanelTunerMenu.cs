using UnityEditor;

namespace Deadlock.Editor
{
    /// <summary>
    /// Switches the development home panel tuner (Deadlock.Dev HomePanelTuner) on or off for Play Mode in the Editor.
    /// Development builds always have it; release builds never do.
    /// </summary>
    public static class HomePanelTunerMenu
    {
        const string Path = "DEADLOCK/Dev/Home Panel Tuner";
        const string Key = "DEADLOCK.HomePanelTuner"; // HomePanelTuner.EditorToggleKey

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
