using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Deadlock.UI;
using NUnit.Framework;

namespace Deadlock.Tests
{
    /// <summary>
    /// The development capture store (2026-10-28 incident: screenshots filled the device; 10-29: a check run must not
    /// remove an earlier one): a folder per run, nothing deleted or overwritten, limits that stop writing instead of
    /// making room. Temporary folders only.
    /// </summary>
    public class DevCaptureStoreTests
    {
        string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "DeadlockCaptureTest_" + Guid.NewGuid().ToString("N"), DevCaptureStore.RootName);

        [TearDown]
        public void TearDown()
        {
            string parent = Path.GetDirectoryName(root);
            if (Directory.Exists(parent))
                Directory.Delete(parent, true); // this test's own temporary folder only
        }

        static readonly DateTime Now = new DateTime(2026, 10, 29, 12, 0, 0);

        static string Sha(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        [Test]
        public void TwoRuns_GetDifferentFolders_TheEarlierRunIsKept()
        {
            var store = new DevCaptureStore(root);
            var first = store.BeginRun("WeaponQuickCheck", Now);
            string shot = first.Write("01_compact.png", new byte[] { 1, 2, 3 });
            first.AppendLine("log.txt", "first run");
            string before = Sha(shot);
            var second = store.BeginRun("WeaponQuickCheck", Now); // the same second
            Assert.AreNotEqual(first.Folder, second.Folder, "a folder of its own");
            StringAssert.StartsWith("20261029_120000_", second.Id, "date and time, then a unique id");
            Assert.AreEqual($"Library/Caches/DeadlockDevCaptures/WeaponQuickCheck/{second.Id}", second.ContainerPath);
            second.Write("01_compact.png", new byte[] { 9, 9 });
            second.AppendLine("log.txt", "second run");
            Assert.AreEqual(before, Sha(shot), "the earlier run's picture untouched");
            Assert.AreEqual("first run\n", File.ReadAllText(Path.Combine(first.Folder, "log.txt")));
            Assert.AreEqual("second run\n", File.ReadAllText(Path.Combine(second.Folder, "log.txt")));
        }

        [Test]
        public void TheSameName_IsNeverOverwritten_LogsAreAppended()
        {
            var run = new DevCaptureStore(root).BeginRun("equip", Now);
            string a = run.Write("shot.png", new byte[] { 1 });
            string b = run.Write("shot.png", new byte[] { 2 });
            Assert.AreNotEqual(a, b);
            StringAssert.EndsWith("shot_2.png", b);
            CollectionAssert.AreEqual(new byte[] { 1 }, File.ReadAllBytes(a), "the first file as it was");
            run.AppendLine("log.txt", "one");
            run.AppendLine("log.txt", "two");
            Assert.AreEqual("one\ntwo\n", File.ReadAllText(Path.Combine(run.Folder, "log.txt")));
        }

        [Test]
        public void Limits_StopWriting_NeverRemoveEarlierRecords()
        {
            var store = new DevCaptureStore(root, maxFiles: 4, maxBytes: 1 << 20);
            var first = store.BeginRun("WeaponQuickCheck", Now);
            first.Write("a.png", new byte[100]);
            first.Write("b.png", new byte[100]); // + marker = 3 files
            var second = store.BeginRun("WeaponQuickCheck", Now);
            Assert.IsNotNull(second.Write("c.png", new byte[100]), "the 4th file fits");
            Assert.IsNull(second.Write("d.png", new byte[100]), "the 5th is refused");
            Assert.IsTrue(second.LimitReached);
            Assert.IsFalse(second.AppendLine("log.txt", "x"), "a new log file is refused too");
            Assert.IsTrue(File.Exists(Path.Combine(first.Folder, "a.png")) && File.Exists(Path.Combine(first.Folder, "b.png")), "nothing removed to make room");

            var bySize = new DevCaptureStore(root + "_size", maxFiles: 100, maxBytes: 350);
            var r1 = bySize.BeginRun("equip", Now);
            r1.Write("a.png", new byte[200]);
            var r2 = bySize.BeginRun("equip", Now);
            Assert.IsNull(r2.Write("b.png", new byte[200]), "over the size limit: refused");
            Assert.AreEqual(200, new FileInfo(Path.Combine(r1.Folder, "a.png")).Length);
        }

        [Test]
        public void FilesOfTheEarlierLayout_AreKept_AndCounted()
        {
            // Stage 6 (10-29) wrote straight into WeaponQuickCheck/ (7 pictures and a log): they stay as they are.
            var store = new DevCaptureStore(root);
            store.BeginRun("WeaponQuickCheck", Now); // makes the root and its marker
            string old = Path.Combine(root, "WeaponQuickCheck");
            for (int i = 1; i <= 7; i++)
                File.WriteAllBytes(Path.Combine(old, $"0{i}_old.png"), new byte[10]);
            File.WriteAllText(Path.Combine(old, "log.txt"), "stage 6");
            var run = store.BeginRun("WeaponQuickCheck", Now);
            run.Write("01_compact.png", new byte[10]);
            run.AppendLine("log.txt", "new");
            Assert.AreEqual(8, Directory.GetFiles(old).Length, "the earlier pictures and log untouched");
            Assert.AreEqual("stage 6", File.ReadAllText(Path.Combine(old, "log.txt")));
            Assert.AreEqual(1 + 8 + 2, store.Usage().files, "counted by the limits (marker, old files, new run)");
        }

        [Test]
        public void AFailedRun_LeavesEverythingInPlace()
        {
            var store = new DevCaptureStore(root);
            var done = store.BeginRun("WeaponQuickCheck", Now);
            done.Write("01.png", new byte[5]);
            var failing = store.BeginRun("WeaponQuickCheck", Now);
            failing.AppendLine("log.txt", "started");
            Assert.Throws<InvalidOperationException>(() =>
            {
                failing.Write("02.png", new byte[5]);
                throw new InvalidOperationException("check stopped");
            });
            Assert.IsTrue(File.Exists(Path.Combine(done.Folder, "01.png")));
            Assert.IsTrue(File.Exists(Path.Combine(failing.Folder, "02.png")), "what the failed run wrote stays too");
            Assert.AreEqual("started\n", File.ReadAllText(Path.Combine(failing.Folder, "log.txt")));
        }

        [Test]
        public void TheStore_HasNoWayToDeleteOrClear()
        {
            foreach (var type in new[] { typeof(DevCaptureStore), typeof(DevCaptureRun) })
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    Assert.IsFalse(new[] { "Delete", "Clear", "Prune", "Remove", "MakeRoom" }.Any(w => m.Name.Contains(w)), type.Name + "." + m.Name);
        }

        [Test]
        public void AFolderWithoutTheMarker_IsNotUsed_AndLeftAlone()
        {
            Directory.CreateDirectory(Path.Combine(root, "equip"));
            string foreign = Path.Combine(root, "equip", "x.png");
            File.WriteAllBytes(foreign, new byte[10]);
            Assert.IsNull(new DevCaptureStore(root).BeginRun("equip", Now));
            Assert.IsTrue(File.Exists(foreign));
            Assert.IsFalse(File.Exists(Path.Combine(root, DevCaptureStore.MarkerName)));
        }

        [Test]
        public void BadNames_AreRefused()
        {
            var store = new DevCaptureStore(root);
            Assert.Throws<ArgumentException>(() => store.BeginRun("../Documents", Now));
            var run = store.BeginRun("equip", Now);
            Assert.Throws<ArgumentException>(() => run.Write("../../a.png", new byte[1]));
            Assert.Throws<ArgumentException>(() => run.Write("a.plist", new byte[1]));
            Assert.Throws<ArgumentException>(() => run.Write(".hidden.png", new byte[1]));
            Assert.Throws<ArgumentException>(() => run.AppendLine("../log.txt", "x"));
        }

        [Test]
        public void DefaultLimits_AndDeviceFolder_AreApartFromSettingsAndDocuments()
        {
            Assert.AreEqual(300, DevCaptureStore.DefaultMaxFiles);
            Assert.AreEqual(256L * 1024 * 1024, DevCaptureStore.DefaultMaxBytes);
            string device = DevCaptureStore.Device.Root;
            StringAssert.EndsWith(DevCaptureStore.RootName, device);
            StringAssert.StartsWith(Path.GetFullPath(UnityEngine.Application.temporaryCachePath), device, "in Caches, not Documents or Preferences");
            Assert.IsTrue(DevCaptureStore.Allowed, "Editor");
        }

        [Test]
        public void EquipmentScreen_CapturesOnlyWhenAskedFor()
        {
            Assert.IsFalse(EquipmentScreenDiagnostics.CaptureRequested(_ => null), "opening the screen alone saves nothing");
            Assert.IsFalse(EquipmentScreenDiagnostics.CaptureRequested(k => k == EquipmentScreenDiagnostics.CaptureVariable ? "0" : null));
            Assert.IsTrue(EquipmentScreenDiagnostics.CaptureRequested(k => k == EquipmentScreenDiagnostics.CaptureVariable ? "1" : null));
            Assert.IsFalse(EquipmentScreenDiagnostics.Enabled, "never in the Editor");
        }
    }
}
