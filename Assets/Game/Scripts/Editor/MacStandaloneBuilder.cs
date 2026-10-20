using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deadlock.Editor
{
    /// <summary>
    /// macOS development build in Builds/Mac (git-ignored), used to run the online prototype as separate processes on
    /// this Mac (host and client) and as a LAN peer for iPhone / iPad. Not a release target (PC release is undecided).
    /// Batch: -batchmode -quit -buildTarget OSXUniversal -executeMethod Deadlock.Editor.MacStandaloneBuilder.BuildFromCommandLine
    /// </summary>
    public static class MacStandaloneBuilder
    {
        public const string OutputPath = "Builds/Mac/DEADLOCK.app";

        [MenuItem("DEADLOCK/Build/Build macOS Development Player")]
        public static void BuildFromMenu() => Build();

        public static void BuildFromCommandLine()
        {
            if (!Build())
                EditorApplication.Exit(1);
        }

        static bool Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[DEADLOCK] macOS development build: {report.summary.result}, errors={report.summary.totalErrors}, output={OutputPath}");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
