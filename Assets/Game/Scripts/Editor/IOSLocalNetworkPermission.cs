#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Deadlock.Editor
{
    /// <summary>
    /// iOS local network privacy (iOS 14+): connecting to / sending UDP to another device on the LAN needs the user's
    /// permission, and the app must explain why in NSLocalNetworkUsageDescription (Apple TN3179). Added to the
    /// generated Info.plist. No Bonjour service types and no multicast entitlement: players type the host address.
    /// </summary>
    public static class IOSLocalNetworkPermission
    {
        public const string UsageDescription =
            "同じWi-Fi内の端末とオンライン検証（2人対戦）を行うために、ローカルネットワークに接続します。";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
                return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription", UsageDescription);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
