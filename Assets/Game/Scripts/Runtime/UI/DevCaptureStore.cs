using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Deadlock.UI
{
    /// <summary>
    /// The one place development screenshots and check logs are written on a device (2026-10-28 storage incident):
    /// Library/Caches/DeadlockDevCaptures/ (<see cref="Application.temporaryCachePath"/>), apart from Documents, the
    /// settings (Library/Preferences) and the balance lab's JSON. A marker file identifies the folder.
    /// <para>
    /// History is kept: every run (a check, an equipment capture session) writes into a folder of its own,
    /// &lt;source&gt;/&lt;yyyyMMdd_HHmmss&gt;_&lt;id&gt;/, made new for the run (another name when it exists). Nothing here
    /// ever deletes or overwrites a file: a name already taken gets a numbered one, and logs are appended to.
    /// </para>
    /// <para>
    /// The limits stop writing instead of making room: once everything under the root (old runs and files of the
    /// earlier layout included) would pass <see cref="MaxFiles"/> files or <see cref="MaxBytes"/> bytes, writes are
    /// refused (null) and <see cref="DevCaptureRun.LimitReached"/> is set, so the device never fills up and no earlier
    /// result is lost. Clearing old runs is left to a person (after copying them to the Mac). A root that exists with
    /// other content but no marker is not used at all. Release builds never write (<see cref="Allowed"/>).
    /// </para>
    /// </summary>
    public sealed class DevCaptureStore
    {
        public const string RootName = "DeadlockDevCaptures";
        public const string MarkerName = ".deadlock-dev-captures";
        public const int DefaultMaxFiles = 300;
        public const long DefaultMaxBytes = 256L * 1024 * 1024;

        static readonly Regex SourcePattern = new Regex("^[A-Za-z0-9_-]{1,40}$");
        static readonly Regex FilePattern = new Regex(@"^[A-Za-z0-9_-][A-Za-z0-9._-]{0,119}\.(png|txt|json)$");

        public string Root { get; }
        public int MaxFiles { get; }
        public long MaxBytes { get; }

        public DevCaptureStore(string root, int maxFiles = DefaultMaxFiles, long maxBytes = DefaultMaxBytes)
        {
            Root = Path.GetFullPath(root);
            MaxFiles = Math.Max(1, maxFiles);
            MaxBytes = Math.Max(1, maxBytes);
        }

        /// <summary>The device's store (Library/Caches/DeadlockDevCaptures), default limits.</summary>
        public static DevCaptureStore Device => new DevCaptureStore(Path.Combine(Application.temporaryCachePath, RootName));

        /// <summary>True where development captures may be written (Editor, development builds).</summary>
        public static bool Allowed =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        public static bool IsPlainName(string fileName) => fileName != null && FilePattern.IsMatch(fileName);

        /// <summary>
        /// A new run of <paramref name="source"/>: a folder of its own, never one that exists. Null where captures are
        /// not allowed, or when the root is not ours (no marker).
        /// </summary>
        public DevCaptureRun BeginRun(string source, DateTime? now = null)
        {
            if (!Allowed)
                return null;
            if (source == null || !SourcePattern.IsMatch(source))
                throw new ArgumentException("not a capture source name: " + source);
            if (!EnsureRoot())
                return null;
            string parent = Path.Combine(Root, source);
            Directory.CreateDirectory(parent);
            string stamp = (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string id = stamp + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                string folder = Path.Combine(parent, id);
                if (Directory.Exists(folder) || File.Exists(folder))
                    continue; // taken: another name
                Directory.CreateDirectory(folder);
                return new DevCaptureRun(this, source, id, folder);
            }
            return null;
        }

        /// <summary>Files and bytes under the root now (every file, old runs included; links not followed).</summary>
        public (int files, long bytes) Usage()
        {
            if (!Directory.Exists(Root))
                return (0, 0L);
            int files = 0;
            long bytes = 0;
            var pending = new System.Collections.Generic.Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(Root));
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var f in dir.GetFiles())
                {
                    files++;
                    bytes += f.Length;
                }
                foreach (var d in dir.GetDirectories())
                    if ((d.Attributes & FileAttributes.ReparsePoint) == 0)
                        pending.Push(d);
            }
            return (files, bytes);
        }

        /// <summary>Whether one more file / <paramref name="bytes"/> more fit under the limits.</summary>
        internal bool Fits(long bytes, bool newFile)
        {
            var (files, used) = Usage();
            return (!newFile || files + 1 <= MaxFiles) && used + bytes <= MaxBytes;
        }

        bool EnsureRoot()
        {
            string marker = Path.Combine(Root, MarkerName);
            if (Directory.Exists(Root) && !File.Exists(marker) && Directory.EnumerateFileSystemEntries(Root).Any())
                return false; // not ours: left alone
            Directory.CreateDirectory(Root);
            if (!File.Exists(marker))
                File.WriteAllText(marker, "DEADLOCK development captures. One folder per run; nothing here is deleted or overwritten by the app.\n");
            return true;
        }
    }

    /// <summary>One run's folder: new files only (a taken name gets a numbered one), logs appended to.</summary>
    public sealed class DevCaptureRun
    {
        readonly DevCaptureStore store;

        public string Source { get; }
        public string Id { get; }
        public string Folder { get; }
        /// <summary>Path inside the app's container, for copying the run to the Mac (Library/Caches/…/&lt;source&gt;/&lt;id&gt;).</summary>
        public string ContainerPath => $"Library/Caches/{DevCaptureStore.RootName}/{Source}/{Id}";
        /// <summary>A write was refused by the limits (nothing was deleted to make room).</summary>
        public bool LimitReached { get; private set; }

        internal DevCaptureRun(DevCaptureStore store, string source, string id, string folder)
        {
            this.store = store;
            Source = source;
            Id = id;
            Folder = folder;
        }

        /// <summary>Writes a new file (never over an existing one). Returns its path, or null when the limits refuse it.</summary>
        public string Write(string fileName, byte[] bytes)
        {
            if (!DevCaptureStore.IsPlainName(fileName))
                throw new ArgumentException("not a capture file name: " + fileName);
            if (!store.Fits(bytes.LongLength, true))
            {
                LimitReached = true;
                return null;
            }
            string path = Free(fileName);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                stream.Write(bytes, 0, bytes.Length);
            return path;
        }

        /// <summary>Adds a line to a log of this run (made on first use). False when the limits refuse it.</summary>
        public bool AppendLine(string fileName, string line)
        {
            if (!DevCaptureStore.IsPlainName(fileName))
                throw new ArgumentException("not a capture file name: " + fileName);
            string path = Path.Combine(Folder, fileName);
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            if (!store.Fits(bytes.LongLength, !File.Exists(path)))
            {
                LimitReached = true;
                return false;
            }
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
                stream.Write(bytes, 0, bytes.Length);
            return true;
        }

        /// <summary>The name itself when free, else name_2, name_3, … (the existing file stays as it is).</summary>
        string Free(string fileName)
        {
            string path = Path.Combine(Folder, fileName);
            if (!File.Exists(path))
                return path;
            string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
            for (int n = 2; ; n++)
            {
                path = Path.Combine(Folder, $"{stem}_{n}{ext}");
                if (!File.Exists(path))
                    return path;
            }
        }
    }
}
