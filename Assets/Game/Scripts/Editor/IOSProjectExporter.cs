using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// Exports the Xcode project to Builds/iOS (git-ignored). Does not sign, install or change signing settings.
    /// Batch: -batchmode -quit -buildTarget iOS -executeMethod Deadlock.Editor.IOSProjectExporter.ExportFromCommandLine
    /// </summary>
    public static class IOSProjectExporter
    {
        public const string OutputPath = "Builds/iOS";

        [MenuItem("DEADLOCK/Build/Export iOS Xcode Project")]
        public static void ExportFromMenu() => Export();

        public static void ExportFromCommandLine()
        {
            // A failed build must fail the batch process; success lets -quit shut down normally.
            if (!Export())
                EditorApplication.Exit(1);
        }

        static bool Export()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DEADLOCK] iOS export: {report.summary.result}, errors={report.summary.totalErrors}, output={OutputPath}");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
