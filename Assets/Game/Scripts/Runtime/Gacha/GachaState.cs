using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.Gacha
{
    [Serializable]
    public sealed class OwnedItem
    {
        public string itemId;
        public string acquiredUtc;
        /// <summary>"gacha", "exchange" or "migrated".</summary>
        public string source;
        /// <summary>How many more times it came out of the gacha (each turned into medals).</summary>
        public int duplicates;
    }

    [Serializable]
    public sealed class DrawResult
    {
        public int slot;
        public string itemId;
        public string displayName;
        public string rarity;
        public string category;
        public bool guaranteed;
        public bool firstTime;
        public int medals;

        public WeaponRarity Rarity => Enum.TryParse(rarity, out WeaponRarity r) ? r : WeaponRarity.Unassigned;
        public ItemCategory Category => Enum.TryParse(category, out ItemCategory c) ? c : ItemCategory.Weapon;
    }

    /// <summary>One purchase (a single draw or an 11-draw with its 11 results), D-058.</summary>
    [Serializable]
    public sealed class HistoryRecord
    {
        public string requestId;
        public string timeUtc;
        /// <summary>"single" or "ten".</summary>
        public string kind;
        public int cost;
        public string eventId;
        /// <summary>The opening was shown (or skipped). False after an interruption: shown again on the next open.</summary>
        public bool revealed;
        public List<DrawResult> results = new List<DrawResult>();

        public int MedalsGained
        {
            get
            {
                int m = 0;
                foreach (var r in results)
                    m += r.medals;
                return m;
            }
        }
    }

    /// <summary>A request already applied (draw or exchange): the same request is never applied twice.</summary>
    [Serializable]
    public sealed class RequestRecord
    {
        public string requestId;
        public string kind;
        public string itemId;
        public string timeUtc;
    }

    /// <summary>Everything of the gacha a player has: points, medals, owned items, the last 100 draws, applied requests.</summary>
    [Serializable]
    public sealed class GachaStateData
    {
        public int points;
        public int medals;
        public List<OwnedItem> owned = new List<OwnedItem>();
        public List<HistoryRecord> history = new List<HistoryRecord>();
        public List<RequestRecord> requests = new List<RequestRecord>();

        public bool Owns(string itemId) => FindOwned(itemId) != null;

        public OwnedItem FindOwned(string itemId)
        {
            foreach (var o in owned)
                if (o.itemId == itemId)
                    return o;
            return null;
        }

        public HistoryRecord FindRecord(string requestId)
        {
            foreach (var h in history)
                if (h.requestId == requestId)
                    return h;
            return null;
        }

        public RequestRecord FindRequest(string requestId)
        {
            foreach (var r in requests)
                if (r.requestId == requestId)
                    return r;
            return null;
        }

        public GachaStateData Clone() => JsonUtility.FromJson<GachaStateData>(JsonUtility.ToJson(this));
    }

    /// <summary>The saved file: a versioned envelope with a checksum of its data.</summary>
    [Serializable]
    public sealed class GachaSaveFile
    {
        public string format = GachaSave.Format;
        public int version = GachaSave.CurrentVersion;
        /// <summary>Always local and unverified: never taken as official assets without the server's check (D-059).</summary>
        public string origin = "local-unverified";
        public string savedAtUtc;
        public string checksum;
        public GachaStateData data = new GachaStateData();
    }

    /// <summary>
    /// Reading and writing the gacha data (D-059): one JSON document holding everything, so a draw (points, results,
    /// owned items, medals, history) is saved by one write and never in part. Versioned with stepwise migration of known
    /// older versions; unknown, future or damaged data is refused.
    /// </summary>
    public static class GachaSave
    {
        public const string Format = "deadlock.gacha";
        public const int CurrentVersion = 2;
        public const int OldestVersion = 1;
        public const int HistoryLimit = 100;
        public const int RequestLimit = 200;

        public static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        public static string Checksum(GachaStateData data)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string Serialize(GachaStateData data, string origin = "local-unverified")
        {
            var file = new GachaSaveFile { data = data, savedAtUtc = Now(), origin = origin, checksum = Checksum(data) };
            return JsonUtility.ToJson(file, true);
        }

        [Serializable]
        sealed class Header
        {
            public string format;
            public int version;
        }

        // ---------------------------------------------------------------- version 1 (the first form of Stage 34)

        [Serializable]
        sealed class V1File
        {
            public string format;
            public int version;
            public int points;
            public int medals;
            public List<string> owned = new List<string>();
            public List<V1Record> history = new List<V1Record>();
        }

        [Serializable]
        sealed class V1Record
        {
            public string timeUtc;
            public string kind;
            public List<V1Result> results = new List<V1Result>();
        }

        [Serializable]
        sealed class V1Result
        {
            public string itemId;
            public bool firstTime;
            public int medals;
        }

        /// <summary>
        /// Reads a saved / imported document: checks the format, migrates a known older version step by step, checks the
        /// checksum and every value. Returns null (with the reason) for anything unknown, newer, damaged or inconsistent.
        /// </summary>
        public static GachaStateData Parse(string json, ItemCatalog catalog, out string error, out int version)
        {
            version = 0;
            error = null;
            Header header;
            try
            {
                header = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<Header>(json);
            }
            catch (Exception)
            {
                header = null;
            }
            if (header == null || header.format != Format)
            {
                error = "形式が違います（DEADLOCK のガチャデータではありません）";
                return null;
            }
            version = header.version;
            if (version > CurrentVersion)
            {
                error = $"新しすぎる版です（版{version}。対応は版{CurrentVersion}まで）";
                return null;
            }
            if (version < OldestVersion)
            {
                error = $"対応していない古い版です（版{version}）";
                return null;
            }
            GachaStateData data;
            try
            {
                if (version == 1)
                    data = MigrateV1(JsonUtility.FromJson<V1File>(json), catalog, out error);
                else
                {
                    var file = JsonUtility.FromJson<GachaSaveFile>(json);
                    data = file?.data;
                    if (data == null)
                        error = "データがありません";
                    else if (file.checksum != Checksum(data))
                    {
                        error = "チェックサムが一致しません（壊れているか書き換えられています）";
                        data = null;
                    }
                }
            }
            catch (Exception e)
            {
                error = "読み込めません：" + e.GetType().Name;
                return null;
            }
            if (data == null)
                return null;
            var problems = Validate(data, catalog);
            if (problems.Count > 0)
            {
                error = "内容が正しくありません：" + string.Join("／", problems);
                return null;
            }
            return data;
        }

        /// <summary>Version 1 → 2: owned ids get their record, results get rarity and kind from the catalog, requests get ids.</summary>
        static GachaStateData MigrateV1(V1File v1, ItemCatalog catalog, out string error)
        {
            error = null;
            if (v1 == null)
            {
                error = "データがありません";
                return null;
            }
            var data = new GachaStateData { points = v1.points, medals = v1.medals };
            foreach (var id in v1.owned)
                data.owned.Add(new OwnedItem { itemId = id, acquiredUtc = "", source = "migrated" });
            int n = 0;
            foreach (var h in v1.history)
            {
                var rec = new HistoryRecord
                {
                    requestId = $"migrated-v1-{n++}",
                    timeUtc = h.timeUtc,
                    kind = h.kind,
                    cost = h.kind == "ten" ? GachaRates.TenCost : GachaRates.SingleCost,
                    eventId = "",
                    revealed = true,
                };
                for (int i = 0; i < h.results.Count; i++)
                {
                    var item = catalog?.Find(h.results[i].itemId);
                    if (item == null)
                    {
                        error = $"不明なアイテム {h.results[i].itemId}";
                        return null;
                    }
                    rec.results.Add(new DrawResult
                    {
                        slot = i,
                        itemId = item.itemId,
                        displayName = item.displayName,
                        rarity = item.rarity.ToString(),
                        category = item.category.ToString(),
                        guaranteed = h.kind == "ten" && i == GachaRates.TenSlots - 1,
                        firstTime = h.results[i].firstTime,
                        medals = h.results[i].medals,
                    });
                }
                data.history.Add(rec);
                data.requests.Add(new RequestRecord { requestId = rec.requestId, kind = "draw", timeUtc = rec.timeUtc });
            }
            return data;
        }

        /// <summary>Problems of a state (empty: none).</summary>
        public static List<string> Validate(GachaStateData d, ItemCatalog catalog)
        {
            var p = new List<string>();
            if (d.owned == null || d.history == null || d.requests == null)
            {
                p.Add("項目が欠けています");
                return p;
            }
            if (d.points < 0)
                p.Add("ポイントが負です");
            if (d.medals < 0)
                p.Add("メダルが負です");
            var seen = new HashSet<string>();
            foreach (var o in d.owned)
            {
                if (o == null || !seen.Add(o.itemId))
                    p.Add($"所持品の重複 {o?.itemId}");
                else if (catalog == null || catalog.Find(o.itemId) == null)
                    p.Add($"不明なアイテム {o.itemId}");
                else if (o.duplicates < 0)
                    p.Add($"重複回数が負 {o.itemId}");
            }
            if (d.history.Count > HistoryLimit)
                p.Add($"履歴が{HistoryLimit}件を超えています");
            var ids = new HashSet<string>();
            foreach (var h in d.history)
            {
                if (h == null || string.IsNullOrEmpty(h.requestId) || !ids.Add(h.requestId))
                {
                    p.Add("履歴の取引IDが無いか重複しています");
                    continue;
                }
                int expected = h.kind == "single" ? 1 : h.kind == "ten" ? GachaRates.TenSlots : -1;
                if (expected < 0 || h.results == null || h.results.Count != expected)
                {
                    p.Add($"履歴 {h.requestId} の件数が正しくありません");
                    continue;
                }
                foreach (var r in h.results)
                {
                    var item = catalog?.Find(r.itemId);
                    if (item == null)
                        p.Add($"履歴に不明なアイテム {r.itemId}");
                    else if (r.medals != (r.firstTime ? 0 : GachaRates.DuplicateMedals(item.rarity)))
                        p.Add($"履歴 {h.requestId} のメダル数が正しくありません");
                }
            }
            return p;
        }
    }

    /// <summary>Where the gacha data lives. A write replaces the whole document at once.</summary>
    public interface IGachaStore
    {
        /// <summary>The saved document, or null when there is none.</summary>
        string Read();
        /// <summary>The document before the last write (kept by the atomic replacement), or null.</summary>
        string ReadPrevious();
        /// <summary>Replaces the whole document. Throws on failure; the old document then stays as it was.</summary>
        void Write(string json);
        /// <summary>Keeps a copy (before an import). Returns where.</summary>
        string Backup(string json, string label);
    }

    /// <summary>
    /// A file in the app's own data folder. A write goes to a temporary file first and then replaces the document in
    /// one step (the previous one kept as .bak), so an interruption leaves either the old or the new document, whole.
    /// </summary>
    public sealed class FileGachaStore : IGachaStore
    {
        public string Folder { get; }
        public string FileName { get; }
        public string MainPath => Path.Combine(Folder, FileName);
        public string PreviousPath => MainPath + ".bak";
        string TempPath => MainPath + ".tmp";

        public FileGachaStore(string folder, string fileName)
        {
            Folder = folder;
            FileName = fileName;
        }

        public string Read() => File.Exists(MainPath) ? File.ReadAllText(MainPath, Encoding.UTF8) : null;

        public string ReadPrevious() => File.Exists(PreviousPath) ? File.ReadAllText(PreviousPath, Encoding.UTF8) : null;

        public void Write(string json)
        {
            Directory.CreateDirectory(Folder);
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(MainPath))
                File.Replace(TempPath, MainPath, PreviousPath);
            else
                File.Move(TempPath, MainPath);
        }

        public string Backup(string json, string label)
        {
            string dir = Path.Combine(Folder, "backups");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(FileName)}_{label}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.json");
            File.WriteAllText(path, json ?? "", Encoding.UTF8);
            return path;
        }
    }

    /// <summary>In memory (tests). Can be told to fail its next writes.</summary>
    public sealed class MemoryGachaStore : IGachaStore
    {
        public string Main;
        public string Previous;
        public int FailNextWrites;
        public int Writes;
        public readonly List<string> Backups = new List<string>();

        public string Read() => Main;
        public string ReadPrevious() => Previous;

        public void Write(string json)
        {
            if (FailNextWrites > 0)
            {
                FailNextWrites--;
                throw new IOException("simulated write failure");
            }
            Previous = Main;
            Main = json;
            Writes++;
        }

        public string Backup(string json, string label)
        {
            Backups.Add(json);
            return $"memory:{label}:{Backups.Count}";
        }
    }
}
