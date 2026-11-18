using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Deadlock.Gacha;
using UnityEngine;

namespace Deadlock.Match
{
    /// <summary>One match reward written down before it is given (Stage 35 completion).</summary>
    [Serializable]
    public sealed class PendingReward
    {
        public const string Pending = "pending";
        public const string Completed = "completed";
        public const string Rejected = "rejected";

        public string matchId;
        public int slotId;
        public int rank;
        public int points;
        public bool eligible;
        /// <summary>"pending", "completed" or "rejected".</summary>
        public string state = Pending;
        public string createdUtc;
        public string doneUtc;
        public string note;
        /// <summary>Of the reward's own values: catches damage, not a deliberate edit (local data is never trusted as official).</summary>
        public string checksum;

        public string Key => matchId + ":" + slotId;

        public string ComputeChecksum()
        {
            string text = string.Join("|", matchId, slotId.ToString(CultureInfo.InvariantCulture), rank.ToString(CultureInfo.InvariantCulture),
                points.ToString(CultureInfo.InvariantCulture), eligible ? "1" : "0", createdUtc);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    [Serializable]
    sealed class RewardJournalFile
    {
        public string format = MatchRewardJournal.Format;
        public int version = MatchRewardJournal.Version;
        public List<PendingReward> entries = new List<PendingReward>();
    }

    /// <summary>
    /// The match rewards not yet given (Stage 35 completion, D-069). When a match's result is decided, the local
    /// player's reward (match id, place id, rank, points, eligibility) is written here first, in its own file (the gacha
    /// data's format is unchanged); then the points are given through the gacha transaction, and the entry is marked
    /// completed. An entry still pending (the save failed, the app stopped in between) is found at the next start and
    /// tried again: the gacha data's request id per match and place makes a second grant impossible, so a reward that
    /// was given but not yet marked is only marked. Entries that fail their checks are rejected, never applied.
    /// Limits: the entry is written when the result is decided; if the device stops before that write (or the storage
    /// fails for both writes), that reward is lost. Official online play decides results and points on the server.
    /// </summary>
    public sealed class MatchRewardJournal
    {
        public const string Format = "deadlock.match-rewards";
        public const int Version = 1;
        public const int KeepDone = 50;

        readonly IGachaStore store;
        RewardJournalFile file = new RewardJournalFile();

        public string LoadError { get; private set; }
        public IReadOnlyList<PendingReward> Entries => file.entries;
        public List<PendingReward> Pending => file.entries.FindAll(e => e.state == PendingReward.Pending);

        public MatchRewardJournal(IGachaStore store)
        {
            this.store = store;
            Load();
        }

        public void Load()
        {
            LoadError = null;
            string json = store.Read() ?? store.ReadPrevious();
            if (json == null)
            {
                file = new RewardJournalFile();
                return;
            }
            RewardJournalFile parsed = null;
            try
            {
                parsed = JsonUtility.FromJson<RewardJournalFile>(json);
            }
            catch (Exception)
            {
                // damaged: handled below
            }
            if (parsed == null || parsed.format != Format || parsed.version != Version || parsed.entries == null)
            {
                var previous = store.ReadPrevious();
                try
                {
                    parsed = previous != null ? JsonUtility.FromJson<RewardJournalFile>(previous) : null;
                }
                catch (Exception)
                {
                    parsed = null;
                }
                if (parsed == null || parsed.format != Format || parsed.version != Version || parsed.entries == null)
                {
                    LoadError = "報酬の記録を読めません（壊れているか、対応していない版です）";
                    file = new RewardJournalFile();
                    return;
                }
            }
            file = parsed;
        }

        /// <summary>The reason an entry may not be applied, or null when it passes every check.</summary>
        public static string Check(PendingReward e)
        {
            if (e == null || string.IsNullOrEmpty(e.matchId) || !e.matchId.StartsWith("m-", StringComparison.Ordinal))
                return "試合IDが不正です";
            if (e.slotId < 1 || e.slotId > MatchRules.Slots)
                return "参加枠IDが不正です";
            if (e.rank < 1 || e.rank > MatchRules.Slots)
                return "順位が不正です";
            if (!e.eligible || e.points <= 0)
                return "報酬資格がありません";
            if (e.points != MatchRules.PointsForRank(e.rank))
                return "順位とポイントが一致しません";
            if (e.checksum != e.ComputeChecksum())
                return "チェックサムが一致しません";
            return null;
        }

        /// <summary>Writes the reward down before it is given. False when it could not be saved (the grant still goes ahead).</summary>
        public bool RecordPending(string matchId, MatchResultRow row, out string error)
        {
            error = null;
            if (row == null || row.Points <= 0)
                return true;
            var e = new PendingReward
            {
                matchId = matchId,
                slotId = row.SlotId,
                rank = row.Rank,
                points = row.Points,
                eligible = true,
                createdUtc = GachaSave.Now(),
            };
            e.checksum = e.ComputeChecksum();
            var existing = file.entries.Find(x => x.Key == e.Key);
            if (existing != null)
                return true; // written already (the result shown again)
            return TrySave(f => f.entries.Add(e), out error);
        }

        /// <summary>Marks an entry completed or rejected.</summary>
        public bool Mark(string matchId, int slotId, string state, string note)
        {
            return TrySave(f =>
            {
                var e = f.entries.Find(x => x.matchId == matchId && x.slotId == slotId);
                if (e == null)
                    return;
                e.state = state;
                e.note = note;
                e.doneUtc = GachaSave.Now();
            }, out _);
        }

        /// <summary>
        /// Gives every pending reward that passes its checks (at start-up, and after a failed save). A reward already in
        /// the gacha data is only marked completed; a failing check marks it rejected; a failed save leaves it pending.
        /// Returns the points given now.
        /// </summary>
        public int Recover(GachaService gacha, List<string> messages = null)
        {
            int given = 0;
            if (LoadError != null)
            {
                messages?.Add(LoadError);
                return 0;
            }
            foreach (var e in Pending)
            {
                string problem = Check(e);
                if (problem != null)
                {
                    Mark(e.matchId, e.slotId, PendingReward.Rejected, problem);
                    messages?.Add($"試合 {e.matchId} の報酬は付与しません（{problem}）");
                    continue;
                }
                var r = gacha.GrantMatchReward(e.matchId, e.slotId, e.points);
                if (r.Outcome == GachaOutcome.Done)
                {
                    given += e.points;
                    Mark(e.matchId, e.slotId, PendingReward.Completed, "recovered");
                    messages?.Add($"前回の試合の報酬 {e.points}pt を付与しました");
                }
                else if (r.Outcome == GachaOutcome.AlreadyApplied)
                    Mark(e.matchId, e.slotId, PendingReward.Completed, "already given");
                else
                    messages?.Add(r.Message);
            }
            return given;
        }

        bool TrySave(Action<RewardJournalFile> change, out string error)
        {
            error = null;
            var next = JsonUtility.FromJson<RewardJournalFile>(JsonUtility.ToJson(file));
            change(next);
            // Keep every pending entry; of the finished ones only the newest.
            var done = next.entries.FindAll(x => x.state != PendingReward.Pending);
            while (done.Count > KeepDone)
            {
                next.entries.Remove(done[0]);
                done.RemoveAt(0);
            }
            try
            {
                store.Write(JsonUtility.ToJson(next, true));
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            file = next;
            return true;
        }
    }

    /// <summary>The app's reward journal (its own file; a development build keeps a separate one).</summary>
    public static class MatchRewardAccount
    {
        public const string FolderName = "match";
        public const string FileName = "match_rewards.json";
        public const string DevFileName = "match_rewards_dev.json";

        static MatchRewardJournal journal;
        public static IGachaStore StoreOverride;

        public static MatchRewardJournal Journal => journal ??= new MatchRewardJournal(StoreOverride ??
            new FileGachaStore(Path.Combine(Application.persistentDataPath, FolderName), Debug.isDebugBuild ? DevFileName : FileName));

        /// <summary>At start-up: gives the rewards still pending from earlier matches. Returns the messages to show.</summary>
        public static List<string> RecoverPending()
        {
            var messages = new List<string>();
            if (journal == null && StoreOverride == null && !File.Exists(Path.Combine(Application.persistentDataPath, FolderName,
                    Debug.isDebugBuild ? DevFileName : FileName)))
                return messages; // no match was ever played: nothing to read (no file is made)
            Journal.Recover(GachaAccount.Service, messages);
            return messages;
        }

        public static void Reset() => journal = null;
    }
}
