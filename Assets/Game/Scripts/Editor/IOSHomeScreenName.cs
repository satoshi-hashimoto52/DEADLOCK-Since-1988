#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Deadlock.Editor
{
    /// <summary>
    /// Name shown under the app icon on the iPhone / iPad home screen: "DEADLOCK" only (2026-10-25). Only
    /// CFBundleDisplayName in the generated Info.plist is set; the product name ("DEADLOCK: Since 1988",
    /// <see cref="IOSPlayerSetup.DisplayName"/>), the bundle id and the other identifiers stay as they are.
    /// </summary>
    public static class IOSHomeScreenName
    {
        public const string HomeScreenName = "DEADLOCK";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
                return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("CFBundleDisplayName", HomeScreenName);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
