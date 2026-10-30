using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Deadlock.Home;
using Deadlock.UI.Layout;
using UnityEngine;

namespace Deadlock.Dev
{
    /// <summary>Settings storage the recovery reads and writes: PlayerPrefs on the device, a dictionary in tests.</summary>
    public interface IRecoveryPrefs
    {
        bool HasKey(string key);
        string GetString(string key);
        int GetInt(string key);
        void SetString(string key, string value);
        void SetInt(string key, int value);
        void Save();
    }

    /// <summary>
    /// The app's own settings storage (PlayerPrefs; on iOS the app's NSUserDefaults, flushed by Save). The recovery
    /// never makes or replaces the preferences file itself.
    /// </summary>
    public sealed class PlayerPrefsRecoveryStore : IRecoveryPrefs
    {
        public bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public string GetString(string key) => PlayerPrefs.GetString(key, "");
        public int GetInt(string key) => PlayerPrefs.GetInt(key, 0);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>Recovery data of one incident (made on the Mac from the checked backup; see docs/recovery).</summary>
    [Serializable]
    public sealed class RecoveryBundle
    {
        public string bundleId;
        public int formatVersion;
        public string source;
        public List<RecoveryPref> prefs = new List<RecoveryPref>();
        public List<RecoveryFile> files = new List<RecoveryFile>();
    }

    [Serializable]
    public sealed class RecoveryPref
    {
        public string item;
        public string key;
        public string kind; // "string" or "int"
        public string stringValue;
        public int intValue;
        public string sha256; // of stringValue (UTF-8); empty for ints
    }

    [Serializable]
    public sealed class RecoveryFile
    {
        public string item;
        public string weaponId;
        public string contentBase64;
        public string sha256; // of the decoded bytes
    }

    /// <summary>One line of a run: what, where, what was (or would be) done.</summary>
    [Serializable]
    public sealed class RecoveryEntry
    {
        public string item;
        public string target;
        public string action;
        public string detail;
    }

    [Serializable]
    public sealed class RecoveryReport
    {
        public string bundleId;
        public string mode;
        public string startedAt;
        public bool ok;
        public string error;
        public List<string> selectedItems = new List<string>();
        public List<RecoveryEntry> entries = new List<RecoveryEntry>();

        public int Count(string action) => entries.Count(e => e.action == action);
    }

    public enum RecoveryMode
    {
        DryRun,
        Apply,
    }

    /// <summary>
    /// What may be recovered after the 2026-10-28 incident (docs/recovery/IOS_DATA_INCIDENT_20261028.md): only
    /// the values confirmed from the device's own records, each with its expected value or SHA-256. A bundle with
    /// anything else, or different content, is refused as a whole. Estimated values (touch layout etc.) are not here.
    /// </summary>
    public static class IosIncident20261028
    {
        public const string BundleId = "ios-incident-20261028";
        public const int FormatVersion = 1;

        public const string HomeLayoutItem = "home-layout";
        public const string PanelTransparencyItem = "panel-transparency";
        public const string PanelCompareItem = "panel-compare";
        public const string RifleItem = "weapon-proto_rifle";
        public const string SmgItem = "weapon-proto_smg";

        public static readonly string[] Items = { HomeLayoutItem, PanelTransparencyItem, PanelCompareItem, RifleItem, SmgItem };

        /// <summary>SHA-256 of the home layout JSON recorded on the device (10-27 13:28, final50_cap_log.txt).</summary>
        public const string HomeLayoutSha256 = "2371715fae3f266f6256151f0db52523bdb7dc723eda453b306a73852c8a1123";
        public const string RifleSha256 = "c8323045c1162c7461ceca3ee56fda60a88b7e2f4cadc9e1663c824f4de21456";
        public const string SmgSha256 = "a355bbfe5e6cd51a51f24bcbfbdff09e8478b28db04e8d60397ea0d4fb79d6fd";

        public static readonly string[] HomeLayoutIds = { "logo", "survival", "training", "event", "gear", "points", "settings", "crate" };

        /// <summary>Allowed settings: key → (item, kind, expected int or the string's SHA-256).</summary>
        public static IEnumerable<(string item, string key, string kind, int intValue, string sha256)> Prefs()
        {
            yield return (HomeLayoutItem, UiLayoutStore.Key(HomeMenu.LayoutId), "string", 0, HomeLayoutSha256);
            foreach (var t in HomePanelLook.Targets)
                yield return (PanelTransparencyItem, HomePanelLook.Key(t), "int", 50, "");
            yield return (PanelCompareItem, HomePanelTuner.CompareKey, "int", 0, "");
        }

        /// <summary>Allowed files: weapon id → (item, SHA-256).</summary>
        public static IEnumerable<(string item, string weaponId, string sha256)> Files()
        {
            yield return (RifleItem, "proto_rifle", RifleSha256);
            yield return (SmgItem, "proto_smg", SmgSha256);
        }
    }

    /// <summary>
    /// DEVELOPMENT ONLY (Deadlock.Dev: never in a release build). Puts back the approved settings and the balance lab's
    /// JSON from a checked bundle. Everything is validated first (whitelist, values, SHA-256, JSON form); any problem
    /// stops the run before anything is written. A setting or file that already exists is never overwritten (the same
    /// content counts as already restored), so a second run changes nothing. Dry-run reports the same plan without
    /// writing. Files are written to a new temporary name and moved into place, which fails rather than replaces.
    /// </summary>
    public static class DeviceDataRecovery
    {
        public const string Write = "write";
        public const string Written = "written";
        public const string AlreadyRestored = "already-restored";
        public const string KeptExisting = "kept-existing";
        public const string NotSelected = "not-selected";
        public const string Failed = "failed";
        public const string TempSuffix = ".recovery-tmp";

        public static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// Runs the bundle. <paramref name="items"/> are item ids (<see cref="IosIncident20261028.Items"/>) or "all".
        /// <paramref name="weaponFolder"/> is the balance lab's folder (Documents/WeaponBalanceLab on the device).
        /// </summary>
        public static RecoveryReport Run(RecoveryBundle bundle, RecoveryMode mode, IEnumerable<string> items, IRecoveryPrefs prefs,
            string weaponFolder)
        {
            var report = new RecoveryReport
            {
                bundleId = bundle?.bundleId,
                mode = mode == RecoveryMode.Apply ? "apply" : "dry-run",
                startedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            };
            if (!TrySelect(items, report.selectedItems, out string error) || !Validate(bundle, out error))
            {
                report.error = error;
                return report;
            }
            var selected = new HashSet<string>(report.selectedItems);
            var store = new WeaponBalanceStore(weaponFolder);

            var prefWrites = new List<(RecoveryPref pref, RecoveryEntry entry)>();
            foreach (var p in bundle.prefs)
            {
                var entry = new RecoveryEntry { item = p.item, target = "PlayerPrefs:" + p.key };
                report.entries.Add(entry);
                string wanted = p.kind == "int" ? p.intValue.ToString(CultureInfo.InvariantCulture) : Preview(p.stringValue);
                if (!selected.Contains(p.item))
                {
                    entry.action = NotSelected;
                    entry.detail = "対象外（値 " + wanted + "）";
                }
                else if (prefs.HasKey(p.key))
                {
                    bool same = p.kind == "int" ? prefs.GetInt(p.key) == p.intValue : prefs.GetString(p.key) == p.stringValue;
                    entry.action = same ? AlreadyRestored : KeptExisting;
                    entry.detail = same ? "同じ値が保存済み" : "既存の値を保持（上書きしない）：" + CurrentText(prefs, p);
                }
                else
                {
                    entry.action = Write;
                    entry.detail = "未保存 → " + wanted;
                    prefWrites.Add((p, entry));
                }
            }

            var fileWrites = new List<(RecoveryFile file, byte[] bytes, string path, RecoveryEntry entry)>();
            foreach (var f in bundle.files)
            {
                string path = store.PathOf(f.weaponId);
                var entry = new RecoveryEntry { item = f.item, target = "File:" + Path.GetFileName(store.Folder) + "/" + f.weaponId + ".json" };
                report.entries.Add(entry);
                byte[] bytes = Convert.FromBase64String(f.contentBase64);
                if (!selected.Contains(f.item))
                {
                    entry.action = NotSelected;
                    entry.detail = $"対象外（{bytes.Length} bytes, sha256 {f.sha256}）";
                }
                else if (File.Exists(path) || Directory.Exists(path))
                {
                    bool same = File.Exists(path) && Sha256(File.ReadAllBytes(path)) == f.sha256;
                    entry.action = same ? AlreadyRestored : KeptExisting;
                    entry.detail = same ? "同じ内容が保存済み" : "既存のファイルを保持（上書きしない）";
                }
                else
                {
                    entry.action = Write;
                    entry.detail = $"未保存 → {bytes.Length} bytes, sha256 {f.sha256}";
                    fileWrites.Add((f, bytes, path, entry));
                }
            }

            if (mode == RecoveryMode.DryRun)
            {
                report.ok = true;
                return report;
            }

            foreach (var (p, entry) in prefWrites)
            {
                if (p.kind == "int")
                    prefs.SetInt(p.key, p.intValue);
                else
                    prefs.SetString(p.key, p.stringValue);
            }
            if (prefWrites.Count > 0)
                prefs.Save();
            foreach (var (p, entry) in prefWrites)
            {
                bool back = prefs.HasKey(p.key) && (p.kind == "int" ? prefs.GetInt(p.key) == p.intValue : prefs.GetString(p.key) == p.stringValue);
                entry.action = back ? Written : Failed;
                entry.detail = back ? "保存し、読み戻して一致を確認" : "保存後の読み戻しが一致しません";
            }

            foreach (var (f, bytes, path, entry) in fileWrites)
            {
                string tmp = path + TempSuffix;
                try
                {
                    Directory.CreateDirectory(store.Folder);
                    File.WriteAllBytes(tmp, bytes);
                    File.Move(tmp, path); // fails if the file appeared meanwhile: never replaces
                }
                catch (Exception e)
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp); // only the recovery's own temporary file
                    entry.action = Failed;
                    entry.detail = "書き込めません：" + e.Message;
                    continue;
                }
                bool same = Sha256(File.ReadAllBytes(path)) == f.sha256;
                bool loads = store.TryLoad(f.weaponId, out _, out string loadError);
                entry.action = same && loads ? Written : Failed;
                entry.detail = same && loads ? "書き込み、SHA-256 と読み込みを確認" : "書き込み後の確認に失敗：" + (same ? loadError : "SHA-256 不一致");
            }

            report.ok = report.Count(Failed) == 0;
            if (!report.ok)
                report.error = "一部の書き込みを確認できませんでした";
            return report;
        }

        static bool TrySelect(IEnumerable<string> items, List<string> selected, out string error)
        {
            error = null;
            var list = (items ?? Enumerable.Empty<string>()).Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
            if (list.Count == 0)
            {
                error = "復旧する項目が指定されていません";
                return false;
            }
            if (list.Contains("all"))
                list = IosIncident20261028.Items.ToList();
            foreach (string i in list)
            {
                if (!IosIncident20261028.Items.Contains(i))
                {
                    error = "不明な項目：" + i;
                    return false;
                }
                if (!selected.Contains(i))
                    selected.Add(i);
            }
            return true;
        }

        /// <summary>The bundle matches the whitelist exactly, and every value and file is what was checked on the Mac.</summary>
        public static bool Validate(RecoveryBundle bundle, out string error)
        {
            error = null;
            if (bundle == null || bundle.prefs == null || bundle.files == null)
                return Fail("復旧データがありません、または読み込めません", out error);
            if (bundle.bundleId != IosIncident20261028.BundleId || bundle.formatVersion != IosIncident20261028.FormatVersion)
                return Fail($"復旧データの種類が違います（{bundle.bundleId} v{bundle.formatVersion}）", out error);

            var allowedPrefs = IosIncident20261028.Prefs().ToList();
            if (bundle.prefs.Count != allowedPrefs.Count || bundle.prefs.Select(p => p?.key).Distinct().Count() != bundle.prefs.Count)
                return Fail("設定の数が許可リストと違います、または重複しています", out error);
            foreach (var p in bundle.prefs)
            {
                var allowed = allowedPrefs.FirstOrDefault(a => a.key == p?.key);
                if (p == null || allowed.key == null)
                    return Fail("許可リストにない設定：" + p?.key, out error);
                if (p.item != allowed.item || p.kind != allowed.kind)
                    return Fail("設定の種類が違います：" + p.key, out error);
                if (p.kind == "int" && p.intValue != allowed.intValue)
                    return Fail($"設定の値が確定値と違います：{p.key}={p.intValue}", out error);
                if (p.kind == "string")
                {
                    if (p.stringValue == null || Sha256(p.stringValue) != allowed.sha256 || p.sha256 != allowed.sha256)
                        return Fail("SHA-256 が一致しません：" + p.key, out error);
                    if (p.key == UiLayoutStore.Key(HomeMenu.LayoutId) && !HomeLayoutIsUsable(p.stringValue, out string layoutError))
                        return Fail("ホームのレイアウトが使えません：" + layoutError, out error);
                }
            }

            var allowedFiles = IosIncident20261028.Files().ToList();
            if (bundle.files.Count != allowedFiles.Count || bundle.files.Select(f => f?.weaponId).Distinct().Count() != bundle.files.Count)
                return Fail("ファイルの数が許可リストと違います、または重複しています", out error);
            foreach (var f in bundle.files)
            {
                var allowed = allowedFiles.FirstOrDefault(a => a.weaponId == f?.weaponId);
                if (f == null || allowed.weaponId == null || f.item != allowed.item)
                    return Fail("許可リストにないファイル：" + f?.weaponId, out error);
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(f.contentBase64 ?? "");
                }
                catch (FormatException)
                {
                    return Fail("ファイルの中身を読めません：" + f.weaponId, out error);
                }
                if (Sha256(bytes) != allowed.sha256 || f.sha256 != allowed.sha256)
                    return Fail("SHA-256 が一致しません：" + f.weaponId, out error);
                if (!WeaponProfileIsUsable(bytes, f.weaponId, out string profileError))
                    return Fail("武器調整データが使えません：" + profileError, out error);
            }
            return true;
        }

        /// <summary>The JSON is the home's layout of the current version, with the 8 elements on screen.</summary>
        public static bool HomeLayoutIsUsable(string json, out string error)
        {
            error = null;
            UiLayoutData data;
            try
            {
                data = JsonUtility.FromJson<UiLayoutData>(json);
            }
            catch (Exception e)
            {
                error = "JSON として読めません：" + e.Message;
                return false;
            }
            if (data == null || data.items == null || data.layoutVersion != HomeMenu.LayoutVersion)
            {
                error = "形式または版が違います";
                return false;
            }
            var ids = data.items.Select(i => i.id).OrderBy(i => i, StringComparer.Ordinal);
            if (!ids.SequenceEqual(IosIncident20261028.HomeLayoutIds.OrderBy(i => i, StringComparer.Ordinal)))
            {
                error = "要素が違います";
                return false;
            }
            foreach (var i in data.items)
            {
                if (!(i.center.x >= 0f && i.center.x <= 1f && i.center.y >= 0f && i.center.y <= 1f && i.scale > 0f && i.scale < 10f))
                {
                    error = "位置か倍率が範囲外：" + i.id;
                    return false;
                }
            }
            return true;
        }

        /// <summary>The JSON reads as the lab's profile of this weapon, of the current version, with valid values.</summary>
        public static bool WeaponProfileIsUsable(byte[] bytes, string weaponId, out string error)
        {
            error = null;
            WeaponBalanceProfile profile;
            try
            {
                profile = JsonUtility.FromJson<WeaponBalanceProfile>(new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (Exception e)
            {
                error = weaponId + "：JSON として読めません：" + e.Message;
                return false;
            }
            if (profile == null || profile.weaponId != weaponId || profile.profileVersion != WeaponBalanceProfile.CurrentVersion)
            {
                error = weaponId + "：武器か版が違います";
                return false;
            }
            var problems = profile.Problems();
            if (problems.Count > 0)
            {
                error = weaponId + "：" + string.Join("、", problems);
                return false;
            }
            return true;
        }

        static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        static string CurrentText(IRecoveryPrefs prefs, RecoveryPref p) =>
            p.kind == "int" ? prefs.GetInt(p.key).ToString(CultureInfo.InvariantCulture) : Preview(prefs.GetString(p.key));

        static string Preview(string s) => s == null ? "" : s.Length <= 60 ? s : s.Substring(0, 60) + $"…（{s.Length} 文字, sha256 {Sha256(s)}）";
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Starts the recovery when the app is launched with it asked for, and only then:
    /// DEADLOCK_RECOVERY = dry-run | apply, DEADLOCK_RECOVERY_BUNDLE = the bundle's id (must match), and for apply
    /// DEADLOCK_RECOVERY_ITEMS = item ids, comma separated, or "all" (dry-run: all when not given). A normal launch does
    /// nothing. The bundle is compiled into development builds only (registered by its generated data file); the
    /// report goes to the log and to Library/Caches/DeadlockRecovery/ (a new file per run, nothing removed).
    /// </summary>
    public static class DeviceDataRecoveryBoot
    {
        public const string ModeVariable = "DEADLOCK_RECOVERY";
        public const string BundleVariable = "DEADLOCK_RECOVERY_BUNDLE";
        public const string ItemsVariable = "DEADLOCK_RECOVERY_ITEMS";
        public const string ReportFolderName = "DeadlockRecovery";

        static string registeredBundle;

        /// <summary>Called by the generated, development-only data file before any scene loads.</summary>
        public static void RegisterBundle(string json) => registeredBundle = json;

        public static string RegisteredBundle => registeredBundle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor)
                return;
            RunFromEnvironment(Environment.GetEnvironmentVariable, registeredBundle, new PlayerPrefsRecoveryStore(),
                Path.Combine(Application.persistentDataPath, "WeaponBalanceLab"), Path.Combine(Application.temporaryCachePath, ReportFolderName));
        }

        /// <summary>Null when no recovery was asked for (nothing read, nothing written).</summary>
        public static RecoveryReport RunFromEnvironment(Func<string, string> environment, string bundleJson, IRecoveryPrefs prefs,
            string weaponFolder, string reportFolder)
        {
            string modeText = environment(ModeVariable);
            if (string.IsNullOrEmpty(modeText))
                return null;

            RecoveryReport report;
            string itemsText = environment(ItemsVariable);
            if (modeText != "dry-run" && modeText != "apply")
                report = Refused(modeText, "DEADLOCK_RECOVERY は dry-run か apply です");
            else if (environment(BundleVariable) != IosIncident20261028.BundleId)
                report = Refused(modeText, "DEADLOCK_RECOVERY_BUNDLE が復旧データと一致しません");
            else if (modeText == "apply" && string.IsNullOrWhiteSpace(itemsText))
                report = Refused(modeText, "apply には DEADLOCK_RECOVERY_ITEMS（項目、または all）が必要です");
            else if (string.IsNullOrEmpty(bundleJson))
                report = Refused(modeText, "このビルドに復旧データが入っていません");
            else
            {
                RecoveryBundle bundle = null;
                try
                {
                    bundle = JsonUtility.FromJson<RecoveryBundle>(bundleJson);
                }
                catch (Exception)
                {
                    // reported as unreadable by Run
                }
                var items = string.IsNullOrWhiteSpace(itemsText) ? new[] { "all" } : itemsText.Split(',');
                report = DeviceDataRecovery.Run(bundle, modeText == "apply" ? RecoveryMode.Apply : RecoveryMode.DryRun, items, prefs, weaponFolder);
            }

            Debug.Log($"[DEADLOCK][recovery] {report.mode} bundle={report.bundleId} ok={report.ok} error={report.error} items={string.Join(",", report.selectedItems)}");
            foreach (var e in report.entries)
                Debug.Log($"[DEADLOCK][recovery] {e.action} {e.item} {e.target} {e.detail}");
            try
            {
                Directory.CreateDirectory(reportFolder);
                string name = $"recovery_{report.mode}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.json";
                File.WriteAllText(Path.Combine(reportFolder, name), JsonUtility.ToJson(report, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DEADLOCK][recovery] report not saved: " + e.Message);
            }
            return report;
        }

        static RecoveryReport Refused(string mode, string error) => new RecoveryReport
        {
            mode = mode == "apply" || mode == "dry-run" ? mode : "invalid", // also names the report file
            startedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture),
            error = error,
        };
    }
}
