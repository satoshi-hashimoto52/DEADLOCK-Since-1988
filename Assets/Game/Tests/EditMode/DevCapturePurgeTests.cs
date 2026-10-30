using System.Collections.Generic;
using System.IO;
using Deadlock.Dev;
using Deadlock.UI;
using NUnit.Framework;

namespace Deadlock.Tests
{
    /// <summary>
    /// The one-off removal of check screenshots already backed up on the Mac: only listed .png files under
    /// WeaponQuickCheck/ whose size and SHA-256 match, one by one; logs, folders, other files and anything through a link
    /// or outside the folder are never touched; any mismatch means nothing is deleted; a dry run deletes nothing.
    /// </summary>
    public class DevCapturePurgeTests
    {
        string root, folder;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "deadlock-purge-" + System.Guid.NewGuid().ToString("N"), DevCaptureStore.RootName);
            folder = Path.Combine(root, DevCapturePurge.Folder);
            Directory.CreateDirectory(Path.Combine(folder, "20261029_180718_7fcf0c"));
            File.WriteAllText(Path.Combine(root, DevCaptureStore.MarkerName), "marker");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(Path.GetDirectoryName(root), true); // the test's own temp folder

        CapturePurgeEntry Make(string path, string content)
        {
            string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return new CapturePurgeEntry(path, new FileInfo(full).Length, DevCapturePurge.Sha256(full));
        }

        string Full(string path) => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

        List<CapturePurgeEntry> Shots() => new List<CapturePurgeEntry>
        {
            Make("WeaponQuickCheck/01_compact.png", "loose shot"),
            Make("WeaponQuickCheck/20261029_180718_7fcf0c/13_dash_rifle.png", "run shot 1"),
            Make("WeaponQuickCheck/20261029_180718_7fcf0c/13_dash_smg.png", "run shot 2"),
        };

        void Others()
        {
            File.WriteAllText(Path.Combine(folder, "log.txt"), "old log");
            File.WriteAllText(Path.Combine(folder, "20261029_180718_7fcf0c", "log.txt"), "run log");
            File.WriteAllText(Path.Combine(folder, "20261029_180718_7fcf0c", "unlisted.png"), "not on the list");
            Directory.CreateDirectory(Path.Combine(root, "EquipmentScreen"));
            File.WriteAllText(Path.Combine(root, "EquipmentScreen", "shot.png"), "other source");
        }

        void OthersKept()
        {
            Assert.IsTrue(File.Exists(Path.Combine(folder, "log.txt")), "log kept");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "20261029_180718_7fcf0c", "log.txt")), "run log kept");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "20261029_180718_7fcf0c", "unlisted.png")), "unlisted screenshot kept");
            Assert.IsTrue(File.Exists(Path.Combine(root, "EquipmentScreen", "shot.png")), "other source kept");
            Assert.IsTrue(File.Exists(Path.Combine(root, DevCaptureStore.MarkerName)), "marker kept");
            Assert.IsTrue(Directory.Exists(Path.Combine(folder, "20261029_180718_7fcf0c")), "the run's folder kept (never a folder)");
        }

        [Test]
        public void DryRun_AllMatch_DeletesNothing()
        {
            var list = Shots();
            Others();
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.DryRun);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(3, report.matched);
            Assert.AreEqual(0, report.deleted);
            foreach (var e in list)
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
            OthersKept();
        }

        [Test]
        public void Delete_AllMatch_RemovesOnlyTheListedScreenshots_OneByOne()
        {
            var list = Shots();
            Others();
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsTrue(report.ok, report.error);
            Assert.AreEqual(3, report.deleted);
            Assert.AreEqual(list[0].size + list[1].size + list[2].size, report.deletedBytes);
            Assert.AreEqual(3, report.lines.Count, "one line per file");
            foreach (var e in list)
                Assert.IsFalse(File.Exists(Full(e.path)), e.path);
            OthersKept();
        }

        [Test]
        public void OneFileDifferent_NothingDeleted()
        {
            var list = Shots();
            File.WriteAllText(Full(list[1].path), "run shot X"); // same length, other content
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsFalse(report.ok);
            Assert.AreEqual(0, report.deleted);
            Assert.AreEqual("mismatch", report.lines[1].result);
            foreach (var e in list)
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
        }

        [Test]
        public void OneFileMissing_NothingDeleted_ASecondRunAfterADeleteRefuses()
        {
            var list = Shots();
            Assert.IsTrue(DevCapturePurge.Run(root, list, CapturePurgeMode.Delete).ok);
            Others();
            var again = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsFalse(again.ok, "already done: refused, never other files");
            Assert.AreEqual(0, again.deleted);
            OthersKept();

            var list2 = new List<CapturePurgeEntry> { Make("WeaponQuickCheck/02_dropdown.png", "a"), Make("WeaponQuickCheck/03_expanded_top.png", "b") };
            File.Delete(Full(list2[1].path));
            var partial = DevCapturePurge.Run(root, list2, CapturePurgeMode.Delete);
            Assert.IsFalse(partial.ok);
            Assert.AreEqual("missing", partial.lines[1].result);
            Assert.IsTrue(File.Exists(Full(list2[0].path)), "the present one is not deleted either");
        }

        [TestCase("WeaponQuickCheck/../outside.png")]
        [TestCase("WeaponQuickCheck/20261029_180718_7fcf0c/../../x.png")]
        [TestCase("/tmp/x.png")]
        [TestCase("EquipmentScreen/shot.png")]
        [TestCase("WeaponQuickCheck/log.txt")]
        [TestCase("WeaponQuickCheck/20261029_180718_7fcf0c/log.txt")]
        [TestCase("WeaponQuickCheck/a/b/c.png")]
        [TestCase("WeaponQuickCheck\\x.png")]
        [TestCase("WeaponQuickCheck/")]
        [TestCase("")]
        public void PathsOutsideOrNotScreenshots_TheListIsRefused(string path)
        {
            var list = Shots();
            Others();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(root), "outside.png"), "outside");
            list.Add(new CapturePurgeEntry(path, 3, new string('a', 64)));
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsFalse(report.ok);
            Assert.AreEqual(0, report.deleted);
            foreach (var e in Shots())
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
            Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(root), "outside.png")));
            OthersKept();
        }

        [Test]
        public void ListedTwice_NoHash_NoSize_Refused()
        {
            var list = Shots();
            list.Add(list[0]);
            Assert.IsFalse(DevCapturePurge.Run(root, list, CapturePurgeMode.Delete).ok);
            Assert.IsFalse(DevCapturePurge.Run(root, new[] { new CapturePurgeEntry(list[1].path, list[1].size, null) }, CapturePurgeMode.Delete).ok);
            Assert.IsFalse(DevCapturePurge.Run(root, new[] { new CapturePurgeEntry(list[1].path, 0, list[1].sha256) }, CapturePurgeMode.Delete).ok);
            Assert.IsFalse(DevCapturePurge.Run(root, new CapturePurgeEntry[0], CapturePurgeMode.Delete).ok);
            foreach (var e in Shots())
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
        }

        [Test]
        public void AFolderNamedLikeAScreenshot_IsNotDeleted()
        {
            var list = Shots();
            string dir = Path.Combine(folder, "fake.png");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "inside.txt"), "x");
            list.Add(new CapturePurgeEntry("WeaponQuickCheck/fake.png", 1, new string('b', 64)));
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsFalse(report.ok);
            Assert.AreEqual("rejected", report.lines[3].result);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "inside.txt")));
        }

        [Test]
        public void NoMarker_NotOurs_Refused()
        {
            var list = Shots();
            File.Delete(Path.Combine(root, DevCaptureStore.MarkerName));
            var report = DevCapturePurge.Run(root, list, CapturePurgeMode.Delete);
            Assert.IsFalse(report.ok);
            foreach (var e in list)
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
        }

        static void Link(string target, string link)
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/ln", $"-s \"{target}\" \"{link}\"") { UseShellExecute = false });
            p.WaitForExit();
            Assert.AreEqual(0, p.ExitCode, "ln -s");
        }

        [Test]
        public void Links_FileOrFolder_Rejected_TheirTargetsKept()
        {
            if (!File.Exists("/bin/ln"))
                Assert.Ignore("no ln here");
            var list = Shots();
            string outsideDir = Path.Combine(Path.GetDirectoryName(root), "elsewhere");
            Directory.CreateDirectory(outsideDir);
            string outsideFile = Path.Combine(outsideDir, "keep.png");
            File.WriteAllText(outsideFile, "keep me");
            Link(outsideFile, Path.Combine(folder, "linked.png"));
            var withFileLink = new List<CapturePurgeEntry>(list) { new CapturePurgeEntry("WeaponQuickCheck/linked.png", new FileInfo(outsideFile).Length, DevCapturePurge.Sha256(outsideFile)) };
            var r1 = DevCapturePurge.Run(root, withFileLink, CapturePurgeMode.Delete);
            Assert.IsFalse(r1.ok, "a file link");
            Assert.AreEqual("rejected", r1.lines[3].result);

            Link(outsideDir, Path.Combine(folder, "20261029_000000_abcdef"));
            var withDirLink = new List<CapturePurgeEntry>(list) { new CapturePurgeEntry("WeaponQuickCheck/20261029_000000_abcdef/keep.png", new FileInfo(outsideFile).Length, DevCapturePurge.Sha256(outsideFile)) };
            var r2 = DevCapturePurge.Run(root, withDirLink, CapturePurgeMode.Delete);
            Assert.IsFalse(r2.ok, "a folder link");
            Assert.AreEqual("rejected", r2.lines[3].result);

            Assert.IsTrue(File.Exists(outsideFile), "the link's target is kept");
            foreach (var e in list)
                Assert.IsTrue(File.Exists(Full(e.path)), e.path);
        }
    }
}
