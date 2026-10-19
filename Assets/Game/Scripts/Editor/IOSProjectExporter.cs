using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Exports the Xcode project to Builds/iOS (git-ignored). Signing uses the Player Settings (see IOSPlayerSetup);
    /// compiling, signing and installing happen in Xcode / Tools/ios-device-run.sh.
    /// Batch: -batchmode -quit -buildTarget iOS -executeMethod Deadlock.Editor.IOSProjectExporter.ExportFromCommandLine
    /// Add -deadlockDevelopment for a development build (frame-rate readout, console log).
    /// </summary>
    public static class IOSProjectExporter
    {
        public const string OutputPath = "Builds/iOS";

        [MenuItem("DEADLOCK/Build/Export iOS Xcode Project")]
        public static void ExportFromMenu() => Export(development: false);

        public static void ExportFromCommandLine()
        {
            // A failed build must fail the batch process; success lets -quit shut down normally.
            bool development = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-deadlockDevelopment") >= 0;
            if (!Export(development))
                EditorApplication.Exit(1);
        }

        static bool Export(bool development)
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DEADLOCK] iOS export ({(development ? "development" : "release")}): {report.summary.result}, errors={report.summary.totalErrors}, output={OutputPath}");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
