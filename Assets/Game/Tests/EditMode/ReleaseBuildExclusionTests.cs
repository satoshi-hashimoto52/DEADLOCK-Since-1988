using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;

namespace Deadlock.Tests
{
    /// <summary>
    /// The recovery code and its data never reach a release build: the iOS player scripts are compiled twice into a
    /// temporary folder (the project's Temp) (release, then development as the control) and the assemblies are searched for them.
    /// Nothing is built or installed; the project and its settings are not changed.
    /// </summary>
    public class ReleaseBuildExclusionTests
    {
        static readonly string[] Markers =
        {
            "ios-incident-20261028",          // bundle id (code and data)
            "DeviceDataRecovery",             // recovery code
            "DEADLOCK_RECOVERY_ITEMS",        // its launch switch
            "0.1967005729675293",             // a value of the recorded home layout (data)
            "IosIncident20261028Bundle",      // the generated data class
        };

        static string[] Compile(ScriptCompilationOptions options, string output)
        {
            var settings = new ScriptCompilationSettings
            {
                target = BuildTarget.iOS,
#pragma warning disable CS0618
                group = BuildTargetGroup.iOS,
#pragma warning restore CS0618
                options = options,
            };
            Directory.CreateDirectory(output);
            var result = PlayerBuildInterface.CompilePlayerScripts(settings, output);
            Assert.IsNotEmpty(result.assemblies, "player scripts compiled");
            return Directory.GetFiles(output, "*.dll");
        }

        static string[] Found(string[] dlls)
        {
            var found = new System.Collections.Generic.List<string>();
            foreach (string dll in dlls)
            {
                byte[] bytes = File.ReadAllBytes(dll);
                foreach (string m in Markers)
                    if (Contains(bytes, Encoding.UTF8.GetBytes(m)) || Contains(bytes, Encoding.Unicode.GetBytes(m)))
                        found.Add(Path.GetFileName(dll) + ":" + m);
            }
            return found.ToArray();
        }

        static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                    j++;
                if (j == needle.Length)
                    return true;
            }
            return false;
        }

        [Test, Timeout(900000)]
        public void ReleasePlayer_HasNoRecoveryCodeOrData_DevelopmentPlayerHasThem()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                Assert.Ignore("iOS build support not installed");
            string root = Path.Combine("Temp", "DeadlockPlayerScripts_" + Guid.NewGuid().ToString("N")); // the project's Temp (not in Git)
            try
            {
                string[] release = Compile(ScriptCompilationOptions.None, Path.Combine(root, "release"));
                Assert.IsFalse(release.Any(d => Path.GetFileName(d) == "Deadlock.Dev.dll"), "the development assembly is not in a release build");
                Assert.IsTrue(release.Any(d => Path.GetFileName(d) == "Deadlock.Runtime.dll"));
                CollectionAssert.IsEmpty(Found(release), "release build");

                string[] dev = Compile(ScriptCompilationOptions.DevelopmentBuild, Path.Combine(root, "development"));
                Assert.IsTrue(dev.Any(d => Path.GetFileName(d) == "Deadlock.Dev.dll"), "control: development build has it");
                var inDev = Found(dev);
                CollectionAssert.Contains(inDev, "Deadlock.Dev.dll:DeviceDataRecovery", "control: the search finds the code");
                if (DeviceDataRecoveryTests.BundleJson() != null)
                    CollectionAssert.Contains(inDev, "Assembly-CSharp.dll:0.1967005729675293", "control: the search finds the data");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true); // this test's own temporary folder only
            }
        }
    }
}
