using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Deadlock.Gacha;
using UnityEngine;

namespace Deadlock.Match
{
    /// <summary>The steps of a match load (D-064), measured one by one.</summary>
    public enum LoadStage
    {
        SceneLoad,
        AssetInit,
        NetworkSync,
        ParticipantReady,
    }

    [Serializable]
    public sealed class StageRecord
    {
        public string stage;
        /// <summary>"not_run", "running", "done", "failed" or "not_supported" (never a made-up time).</summary>
        public string status = LoadTrace.NotRun;
        /// <summary>Seconds from the start of the load (−1: none).</summary>
        public float startSeconds = -1f;
        public float endSeconds = -1f;
        public float seconds = -1f;
        public string note;
    }

    /// <summary>
    /// One load, step by step: each step's start, end and duration, measured from the start of the load. The whole
    /// load's time is the time from its start to its end, never the sum of the steps (steps may overlap). A step not
    /// run or not supported says so and has no time.
    /// </summary>
    public sealed class LoadTrace
    {
        public const string NotRun = "not_run";
        public const string Running = "running";
        public const string Done = "done";
        public const string Failed = "failed";
        public const string NotSupported = "not_supported";

        readonly float start;
        readonly Dictionary<LoadStage, StageRecord> stages = new Dictionary<LoadStage, StageRecord>();

        /// <summary>"offline" (Stage 36, the default) or "online" (Stage 37E).</summary>
        public string Mode = "offline";
        /// <summary>
        /// Waiting that is not loading and is left out of the whole time (online: the matchmaking wait between the
        /// accepted join and the confirmed match, measured, never estimated). 0 offline.
        /// </summary>
        public float ExcludedWaitSeconds { get; private set; }
        /// <summary>Connection attempts of the load (online; 0 offline).</summary>
        public int Attempts;

        public LoadTrace(float now)
        {
            start = now;
            foreach (LoadStage s in Enum.GetValues(typeof(LoadStage)))
                stages[s] = new StageRecord { stage = Name(s) };
        }

        public static string Name(LoadStage s) => s switch
        {
            LoadStage.SceneLoad => "scene_load",
            LoadStage.AssetInit => "asset_init",
            LoadStage.NetworkSync => "network_sync",
            _ => "participant_ready",
        };

        public StageRecord Stage(LoadStage s) => stages[s];

        public void Begin(LoadStage s, float now)
        {
            var r = stages[s];
            r.status = Running;
            r.startSeconds = now - start;
        }

        public void End(LoadStage s, float now, bool ok = true, string note = null)
        {
            var r = stages[s];
            if (r.status != Running)
                return;
            r.status = ok ? Done : Failed;
            r.endSeconds = now - start;
            r.seconds = r.endSeconds - r.startSeconds;
            r.note = note;
        }

        public void MarkNotSupported(LoadStage s, string note)
        {
            var r = stages[s];
            r.status = NotSupported;
            r.note = note;
        }

        /// <summary>The step still running (where a failure or timeout happened), or null.</summary>
        public LoadStage? Current
        {
            get
            {
                foreach (LoadStage s in Enum.GetValues(typeof(LoadStage)))
                    if (stages[s].status == Running)
                        return s;
                return null;
            }
        }

        /// <summary>Adds a measured wait that is not part of the load (see <see cref="ExcludedWaitSeconds"/>).</summary>
        public void ExcludeWait(float seconds)
        {
            if (seconds > 0f)
                ExcludedWaitSeconds += seconds;
        }

        /// <summary>The whole load so far: start to <paramref name="now"/> (wall time, not a sum), less the excluded wait.</summary>
        public float Total(float now) => now - start - ExcludedWaitSeconds;

        public List<StageRecord> Records()
        {
            var list = new List<StageRecord>();
            foreach (LoadStage s in Enum.GetValues(typeof(LoadStage)))
            {
                var r = stages[s];
                list.Add(new StageRecord { stage = r.stage, status = r.status, startSeconds = r.startSeconds, endSeconds = r.endSeconds, seconds = r.seconds, note = r.note });
            }
            return list;
        }
    }

    [Serializable]
    public sealed class LoadLogEntry
    {
        public string timeUtc;
        /// <summary>"LOAD_SUCCESS", "LOAD_FAILED", "LOAD_TIMEOUT" or "SLOW_LOAD".</summary>
        public string eventType;
        /// <summary>The step where it ended ("complete" when the load finished).</summary>
        public string stage;
        public float totalSeconds;
        public List<StageRecord> stages = new List<StageRecord>();
        public int consecutiveFailures;
        /// <summary>"NONE", "TIMEOUT", "SETUP_FAILED", ...</summary>
        public string errorCategory = "NONE";
        /// <summary>"development" or "release".</summary>
        public string build;
        /// <summary>"offline" or "online" (Stage 37E; empty in entries written before).</summary>
        public string mode;
        /// <summary>Online: the matchmaking wait left out of <see cref="totalSeconds"/> (measured).</summary>
        public float excludedWaitSeconds;
        /// <summary>Online: connection attempts (the first and the retries).</summary>
        public int attempts;
    }

    [Serializable]
    sealed class LoadLogFile
    {
        public string format = LoadDiagnostics.Format;
        public int version = 1;
        public int consecutiveFailures;
        public List<LoadLogEntry> entries = new List<LoadLogEntry>();
    }

    /// <summary>
    /// The load diagnostics log (Stage 36B, D-064): its own file in the app's data folder (apart from the gacha data,
    /// the weapon lab and PlayerPrefs). A development build records every success, failure and timeout; a release build
    /// only failures, timeouts and slow loads. A load over 10 s adds a SLOW_LOAD entry (no warning on screen, and the
    /// load goes on; the 30 s timeout is the session's). At most 100 entries and 30 days, the oldest dropped first.
    /// The number of failures in a row is kept: from 3 the failure screen asks to check the device and network
    /// (rematching is never limited); a normal start of a match resets it. Nothing personal is recorded: times, step
    /// names, durations, counts and error kinds only.
    /// </summary>
    public sealed class LoadDiagnostics
    {
        public const string Format = "deadlock.load-diagnostics";
        public const float SlowSeconds = 10f;
        public const int MaxEntries = 100;
        public const int KeepDays = 30;
        public const int WarnAfterFailures = 3;

        readonly IGachaStore store;
        readonly Func<DateTime> utcNow;
        readonly bool development;
        LoadLogFile file = new LoadLogFile();

        public IReadOnlyList<LoadLogEntry> Entries => file.entries;
        public int ConsecutiveFailures => file.consecutiveFailures;
        public bool ShouldWarn => file.consecutiveFailures >= WarnAfterFailures;
        public bool Development => development;
        public string LastError { get; private set; }

        public LoadDiagnostics(IGachaStore store, bool development, Func<DateTime> utcNow = null)
        {
            this.store = store;
            this.development = development;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            Load();
        }

        void Load()
        {
            string json = store.Read() ?? store.ReadPrevious();
            LoadLogFile parsed = null;
            try
            {
                parsed = json != null ? JsonUtility.FromJson<LoadLogFile>(json) : null;
            }
            catch (Exception)
            {
                parsed = null;
            }
            file = parsed != null && parsed.format == Format && parsed.entries != null ? parsed : new LoadLogFile();
            Prune(file);
        }

        /// <summary>The load finished. Development: a success entry; any build: SLOW_LOAD when over 10 s.</summary>
        public void Success(LoadTrace trace, float now)
        {
            float total = trace.Total(now);
            Save(f =>
            {
                if (development)
                    f.entries.Add(Entry(f, "LOAD_SUCCESS", "complete", trace, total, "NONE"));
                if (total > SlowSeconds)
                    f.entries.Add(Entry(f, "SLOW_LOAD", "complete", trace, total, "NONE"));
            });
        }

        /// <summary>The load failed or timed out: always recorded, and one more failure in a row.</summary>
        public void Failure(LoadTrace trace, float now, string errorCategory, bool timeout)
        {
            float total = trace.Total(now);
            var at = trace.Current;
            if (at.HasValue)
                trace.End(at.Value, now, false, timeout ? "timeout" : errorCategory);
            string stage = at.HasValue ? LoadTrace.Name(at.Value) : "complete";
            Save(f =>
            {
                f.consecutiveFailures++;
                f.entries.Add(Entry(f, timeout ? "LOAD_TIMEOUT" : "LOAD_FAILED", stage, trace, total, timeout ? "TIMEOUT" : errorCategory));
                if (total > SlowSeconds)
                    f.entries.Add(Entry(f, "SLOW_LOAD", stage, trace, total, timeout ? "TIMEOUT" : errorCategory));
            });
        }

        /// <summary>The player took part in a match normally: the failures in a row start again from 0.</summary>
        public void ParticipatedNormally()
        {
            if (file.consecutiveFailures != 0)
                Save(f => f.consecutiveFailures = 0);
        }

        LoadLogEntry Entry(LoadLogFile f, string type, string stage, LoadTrace trace, float total, string category) => new LoadLogEntry
        {
            timeUtc = utcNow().ToString("o", CultureInfo.InvariantCulture),
            eventType = type,
            stage = stage,
            totalSeconds = total,
            stages = trace.Records(),
            consecutiveFailures = f.consecutiveFailures,
            errorCategory = category,
            build = development ? "development" : "release",
            mode = trace.Mode,
            excludedWaitSeconds = trace.ExcludedWaitSeconds,
            attempts = trace.Attempts,
        };

        void Prune(LoadLogFile f)
        {
            DateTime limit = utcNow().AddDays(-KeepDays);
            f.entries.RemoveAll(e => !DateTime.TryParse(e.timeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                                     || t.ToUniversalTime() < limit);
            if (f.entries.Count > MaxEntries)
                f.entries.RemoveRange(0, f.entries.Count - MaxEntries);
        }

        void Save(Action<LoadLogFile> change)
        {
            var next = JsonUtility.FromJson<LoadLogFile>(JsonUtility.ToJson(file));
            change(next);
            Prune(next);
            try
            {
                store.Write(JsonUtility.ToJson(next, true));
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = e.Message; // the diagnostics never stop a load
            }
            file = next; // kept in memory even if the write failed
        }

        /// <summary>The whole log as structured JSON (development builds only; null otherwise).</summary>
        public string ExportJson() => development ? JsonUtility.ToJson(file, true) : null;

        public void PruneNow() => Save(_ => { });
    }

    /// <summary>The app's load diagnostics (its own file; a development build keeps a separate one).</summary>
    public static class LoadDiagnosticsAccount
    {
        public const string FolderName = "diagnostics";
        public const string FileName = "load_diagnostics.json";
        public const string DevFileName = "load_diagnostics_dev.json";

        static LoadDiagnostics instance;
        public static IGachaStore StoreOverride;
        /// <summary>Checks only: as a release build.</summary>
        public static bool? DevelopmentOverride;

        public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

        public static LoadDiagnostics Instance
        {
            get
            {
                bool dev = DevelopmentOverride ?? Debug.isDebugBuild;
                return instance ??= new LoadDiagnostics(StoreOverride ?? new FileGachaStore(Folder, dev ? DevFileName : FileName), dev);
            }
        }

        public static void Reset() => instance = null;
    }
}
