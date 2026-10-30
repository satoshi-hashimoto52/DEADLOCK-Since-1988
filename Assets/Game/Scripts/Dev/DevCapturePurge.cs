using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Deadlock.UI;

namespace Deadlock.Dev
{
    /// <summary>One check screenshot that may be removed: its path under the capture root, size and SHA-256.</summary>
    [Serializable]
    public struct CapturePurgeEntry
    {
        /// <summary>Under the capture root, '/' separated: WeaponQuickCheck/[run/]name.png.</summary>
        public string path;
        public long size;
        /// <summary>Lower-case hex.</summary>
        public string sha256;

        public CapturePurgeEntry(string path, long size, string sha256)
        {
            this.path = path;
            this.size = size;
            this.sha256 = sha256;
        }
    }

    public enum CapturePurgeMode
    {
        DryRun,
        Delete,
    }

    [Serializable]
    public sealed class CapturePurgeLine
    {
        public string path;
        /// <summary>match / mismatch / missing / rejected / deleted / skipped / failed.</summary>
        public string result;
        public long size;
        public string detail;
    }

    [Serializable]
    public sealed class CapturePurgeReport
    {
        public string mode;
        public string startedAt;
        public bool ok;
        public string error;
        public int listed;
        public int matched;
        public int deleted;
        public long deletedBytes;
        public int skipped;
        public List<CapturePurgeLine> lines = new List<CapturePurgeLine>();
    }

    /// <summary>
    /// DEVELOPMENT ONLY (the Dev assembly is not in release builds). Removes check screenshots from the device's capture
    /// store (<see cref="DevCaptureStore"/>), and only those on a list made from their backups on the Mac: each by its
    /// path, size and SHA-256. Nothing else is touched: only .png files under WeaponQuickCheck/, one by one, never a
    /// folder, never a log, never a file reached through a link or outside that folder.
    /// <para>
    /// Every listed file is checked first. If any one is missing, different, or not a plain file where it should be,
    /// nothing is deleted at all (so a second run, after a delete, refuses: it never turns to other files). A dry run
    /// stops after the checks. A delete checks each file again right before removing it, and skips it if it changed.
    /// </para>
    /// </summary>
    public static class DevCapturePurge
    {
        public const string Folder = "WeaponQuickCheck";

        static readonly Regex PathPattern = new Regex(@"^WeaponQuickCheck/(?:[0-9]{8}_[0-9]{6}_[0-9a-f]{6}/)?[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}\.png$");
        static readonly Regex ShaPattern = new Regex("^[0-9a-f]{64}$");

        public static CapturePurgeReport Run(string captureRoot, IReadOnlyList<CapturePurgeEntry> list, CapturePurgeMode mode)
        {
            var report = new CapturePurgeReport
            {
                mode = mode == CapturePurgeMode.Delete ? "delete" : "dry-run",
                startedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
                listed = list?.Count ?? 0,
            };
            string error = CheckList(list);
            if (error != null)
                return Refuse(report, error);

            string root = Path.GetFullPath(captureRoot);
            string folder = Path.Combine(root, Folder);
            if (!File.Exists(Path.Combine(root, DevCaptureStore.MarkerName)))
                return Refuse(report, "the capture root has no marker (not ours)");
            if (!Directory.Exists(folder) || IsLink(root) || IsLink(folder))
                return Refuse(report, "the WeaponQuickCheck folder is missing or a link");

            var targets = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                string reason = Locate(folder, e, out targets[i]);
                if (reason == null)
                    reason = Compare(targets[i], e);
                if (reason == null)
                    report.matched++;
                Add(report, e.path, reason == null ? "match" : reason.StartsWith("missing") ? "missing" : reason.StartsWith("rejected") ? "rejected" : "mismatch", e.size, reason);
            }
            if (report.matched != list.Count)
                return Refuse(report, $"{list.Count - report.matched} of {list.Count} files do not match: nothing deleted");
            if (mode == CapturePurgeMode.DryRun)
            {
                report.ok = true;
                return report;
            }

            report.lines.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                string again = Locate(folder, e, out string target) ?? Compare(target, e);
                if (again != null || target != targets[i])
                {
                    report.skipped++;
                    Add(report, e.path, "skipped", e.size, "changed since the check: " + again);
                    continue;
                }
                try
                {
                    File.Delete(target);
                    report.deleted++;
                    report.deletedBytes += e.size;
                    Add(report, e.path, "deleted", e.size, e.sha256);
                }
                catch (Exception ex)
                {
                    report.skipped++;
                    Add(report, e.path, "failed", e.size, ex.GetType().Name + ": " + ex.Message);
                }
            }
            report.ok = report.skipped == 0;
            if (!report.ok)
                report.error = $"{report.skipped} files not deleted";
            return report;
        }

        /// <summary>A list fit to use: entries, each a plain screenshot path under WeaponQuickCheck/, once, with a size and a hash.</summary>
        static string CheckList(IReadOnlyList<CapturePurgeEntry> list)
        {
            if (list == null || list.Count == 0)
                return "empty list";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in list)
            {
                if (e.path == null || !PathPattern.IsMatch(e.path) || e.path.Contains(".."))
                    return "not a screenshot path under WeaponQuickCheck/: " + e.path;
                if (!seen.Add(e.path))
                    return "listed twice: " + e.path;
                if (e.size <= 0 || e.sha256 == null || !ShaPattern.IsMatch(e.sha256))
                    return "no size or SHA-256 for " + e.path;
            }
            return null;
        }

        /// <summary>The entry's file, strictly inside the folder, through no link; null when fine, else why not.</summary>
        static string Locate(string folder, CapturePurgeEntry e, out string target)
        {
            target = Path.GetFullPath(Path.Combine(folder, e.path.Substring(Folder.Length + 1).Replace('/', Path.DirectorySeparatorChar)));
            string inside = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(inside, StringComparison.Ordinal))
                return "rejected: outside the folder";
            for (string dir = Path.GetDirectoryName(target); dir.Length >= inside.Length; dir = Path.GetDirectoryName(dir))
                if (IsLink(dir))
                    return "rejected: a link on the way";
            if (Directory.Exists(target))
                return "rejected: a folder";
            if (!File.Exists(target))
                return "missing";
            if (IsLink(target))
                return "rejected: a link";
            return null;
        }

        static string Compare(string target, CapturePurgeEntry e)
        {
            long size = new FileInfo(target).Length;
            if (size != e.size)
                return $"size {size}, listed {e.size}";
            string sha = Sha256(target);
            return sha == e.sha256 ? null : "SHA-256 " + sha;
        }

        static bool IsLink(string path)
        {
            var info = new FileInfo(path);
            return info.Exists || Directory.Exists(path) ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 : false;
        }

        public static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static CapturePurgeReport Refuse(CapturePurgeReport report, string error)
        {
            report.ok = false;
            report.error = error;
            return report;
        }

        static void Add(CapturePurgeReport report, string path, string result, long size, string detail) =>
            report.lines.Add(new CapturePurgeLine { path = path, result = result, size = size, detail = detail });
    }
}
