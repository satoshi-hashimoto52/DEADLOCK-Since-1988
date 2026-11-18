using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Deadlock.Combat;
using Deadlock.Economy;
using Deadlock.Gacha;
using Deadlock.UI;
using UnityEngine;

namespace Deadlock.Dev
{
    /// <summary>
    /// DEVELOPMENT ONLY (assembly Deadlock.Dev: Editor and development builds; never in a release build). The gacha
    /// screen's figures and actions from the account and economy service (Stage 37E) through the development sign-in of
    /// a local account, on this Mac only (a loopback address; the service listens nowhere else and has no official
    /// sign-in yet, D-072). Only what the service confirms is shown: points, medals, items, history, shop. An action
    /// whose answer did not come keeps its request id — in memory and in a small file of its own (never the gacha data)
    /// — and is sent again as it was, or looked up by that id when the screen opens again, so it is applied once.
    /// Nothing here reads or writes the device's Stage 34 gacha data.
    /// </summary>
    public sealed class ServerGachaSource : IGachaSource, IDisposable
    {
        public const string DefaultUrl = "http://127.0.0.1:7791";
        public const string DefaultAccount = "dev-local";
        public const string PendingFolderName = "economy_dev";

        [Serializable]
        sealed class PendingFile
        {
            public string drawId;
            public string drawKind;
            public string exchangeId;
            public string exchangeItem;
        }

        readonly ConcurrentQueue<Action> answers = new ConcurrentQueue<Action>();
        readonly OnlineGachaSession session;
        readonly EconomyClient client;
        readonly string pendingPath;
        readonly ItemCatalog names;
        readonly List<HistoryRecord> history = new List<HistoryRecord>();
        readonly List<ItemDefinition> shop = new List<ItemDefinition>();
        readonly HashSet<string> owned = new HashSet<string>();
        WalletAnswer wallet;
        string error;
        int waiting;

        public string Url { get; }
        public string AccountId => client.AccountId;
        public OnlineGachaSession Session => session;
        public string PendingPath => pendingPath;

        ServerGachaSource(string url, EconomyClient client, string pendingFolder, ItemCatalog names)
        {
            Url = url;
            this.client = client;
            session = new OnlineGachaSession(client);
            this.names = names;
            Directory.CreateDirectory(pendingFolder);
            pendingPath = Path.Combine(pendingFolder, $"pending_{client.AccountId}.json");
            LoadPending();
        }

        public void Dispose() => client.Dispose();

        /// <summary>Only an address of this machine (the service has no official sign-in and listens on the loopback only).</summary>
        public static bool IsLoopback(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "http" && (u.Host == "127.0.0.1" || u.Host == "localhost" || u.Host == "[::1]");

        /// <summary>The default folder of the pending file: the app's data folder, apart from the gacha data.</summary>
        public static string DefaultPendingFolder => Path.Combine(Application.persistentDataPath, PendingFolderName);

        /// <summary>Signs in a development account (the service must run with development sign-in) and answers on the main thread.</summary>
        public static Task<(ServerGachaSource source, string error)> SignIn(string url, string accountId, string pendingFolder = null)
        {
            if (!IsLoopback(url))
                return Task.FromResult<(ServerGachaSource, string)>((null, "開発用の接続は、このMacのアドレス（127.0.0.1）だけです"));
            if (!EconomyProtocol.ValidAccountId(accountId))
                return Task.FromResult<(ServerGachaSource, string)>((null, "アカウントIDが不正です"));
            var names = GachaAccount.BuildCatalog(); // Unity objects: read here, on the main thread
            string folder = pendingFolder ?? DefaultPendingFolder;
            return Task.Run(async () =>
            {
                var s = await EconomyClient.DevSession(url, accountId).ConfigureAwait(false);
                if (s.status != EconomyCodes.Ok)
                    return ((ServerGachaSource)null, s.message ?? s.code);
                var c = new EconomyClient(url, accountId, s.token) { RetryDelayMs = 200 };
                return (new ServerGachaSource(url, c, folder, names), (string)null);
            });
        }

        // ------------------------------------------------------------------------------------------- pending file

        void LoadPending()
        {
            try
            {
                if (!File.Exists(pendingPath))
                    return;
                var p = JsonUtility.FromJson<PendingFile>(File.ReadAllText(pendingPath));
                if (p != null)
                    session.Restore(p.drawId, p.drawKind, p.exchangeId, p.exchangeItem);
            }
            catch (Exception)
            {
                // An unreadable file: nothing pending (the service still has every applied action by its id).
            }
        }

        void SavePending()
        {
            try
            {
                if (session.PendingDrawId == null && session.PendingExchangeId == null)
                {
                    if (File.Exists(pendingPath))
                        File.Delete(pendingPath);
                    return;
                }
                File.WriteAllText(pendingPath, JsonUtility.ToJson(new PendingFile
                {
                    drawId = session.PendingDrawId,
                    drawKind = session.PendingDrawKind,
                    exchangeId = session.PendingExchangeId,
                    exchangeItem = session.PendingExchangeItem,
                }));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ECONOMY][Dev] pending request not saved: " + e.Message);
            }
        }

        // ------------------------------------------------------------------------------------------- IGachaSource

        public string Label => $"サーバー接続（開発用・このMac）：アカウント {AccountId}　表示はサーバーが確定した値だけです";
        public bool Ready => wallet != null;
        public bool Busy => waiting > 0;
        public int Points => wallet?.points ?? 0;
        public int Medals => wallet?.medals ?? 0;
        public bool Owns(string itemId) => owned.Contains(itemId);
        public IReadOnlyList<HistoryRecord> History => history;
        public GachaEvent ActiveEvent => wallet != null && !string.IsNullOrEmpty(wallet.activeEventId) ? new GachaEvent { eventId = wallet.activeEventId, rerun = wallet.activeEventRerun } : null;
        public bool PoolOpen => wallet != null && wallet.gachaOpen;
        public IReadOnlyList<string> PoolProblems => wallet != null ? wallet.gachaProblems : (IReadOnlyList<string>)Array.Empty<string>();
        public string Error => error;
        public bool UsesTestItems => wallet != null && wallet.catalog != "official";
        public bool HasPendingDraw => session.PendingDrawId != null;

        public void Pump()
        {
            while (answers.TryDequeue(out var a))
                a();
        }

        /// <summary>Runs <paramref name="work"/> off the main thread and its answer on it (through <see cref="Pump"/>).</summary>
        void Ask<T>(Func<Task<T>> work, Action<T> then, bool busy = true)
        {
            if (busy)
                waiting++;
            Task.Run(work).ContinueWith(t =>
            {
                answers.Enqueue(() =>
                {
                    if (busy)
                        waiting--;
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        error = "サーバーとの通信でエラーが起きました：" + t.Exception?.GetBaseException().Message;
                        then(default);
                        return;
                    }
                    then(t.Result);
                });
            });
        }

        void Apply(WalletAnswer w)
        {
            if (w == null)
                return;
            if (w.status == EconomyCodes.Ok)
            {
                wallet = w;
                error = null;
                owned.Clear();
                foreach (var o in w.owned)
                    owned.Add(o.itemId);
            }
            else
                error = Unavailable(w.message);
        }

        static string Unavailable(string detail) => "サーバーに接続できません。残高・所持品はサーバーの確定値だけを表示します" + (string.IsNullOrEmpty(detail) ? "" : $"（{detail}）");

        public void Refresh(Action done)
        {
            Ask(async () =>
            {
                var pending = await session.ResolvePendingDraw();
                var w = await session.Refresh();
                var h = await client.History();
                var s = await client.Shop();
                return (pending, w, h, s);
            }, r =>
            {
                if (r.w != null)
                    Apply(r.w);
                if (r.h != null && r.h.status == EconomyCodes.Ok)
                {
                    history.Clear();
                    for (int i = r.h.records.Count - 1; i >= 0; i--)
                        history.Add(r.h.records[i]); // the service answers newest first; the screen keeps oldest first
                    history.Sort((a, b) => string.CompareOrdinal(a.timeUtc, b.timeUtc));
                }
                if (r.s != null && r.s.status == EconomyCodes.Ok)
                {
                    shop.Clear();
                    foreach (var v in r.s.items)
                        shop.Add(new ItemDefinition
                        {
                            itemId = v.itemId,
                            displayName = string.IsNullOrEmpty(v.displayName) ? names.Find(v.itemId)?.displayName ?? v.itemId : v.displayName,
                            rarity = Enum.TryParse(v.rarity, out WeaponRarity rr) ? rr : WeaponRarity.Unassigned,
                            category = Enum.TryParse(v.category, out ItemCategory cc) ? cc : ItemCategory.Weapon,
                        });
                }
                SavePending();
                done?.Invoke();
            });
        }

        public HistoryRecord Unrevealed()
        {
            for (int i = history.Count - 1; i >= 0; i--)
                if (!history[i].revealed)
                    return history[i];
            return null;
        }

        public void MarkRevealed(string requestId)
        {
            var rec = history.Find(h => h.requestId == requestId);
            if (rec == null || rec.revealed)
                return;
            rec.revealed = true;
            Ask(() => client.MarkRevealed(requestId), _ => { }, busy: false); // nothing waits for it (shown again if it is lost)
        }

        public List<ItemDefinition> ShopLineup() => new List<ItemDefinition>(shop);

        static GachaOutcome OutcomeOf(string status, string code)
        {
            if (status == EconomyCodes.Ok)
                return GachaOutcome.Done;
            if (status == EconomyCodes.AlreadyApplied)
                return GachaOutcome.AlreadyApplied;
            return code switch
            {
                EconomyCodes.NotEnoughPoints => GachaOutcome.NotEnoughPoints,
                EconomyCodes.NotEnoughMedals => GachaOutcome.NotEnoughMedals,
                EconomyCodes.InvalidConfiguration => GachaOutcome.InvalidConfiguration,
                EconomyCodes.NotOffered => GachaOutcome.NotOffered,
                EconomyCodes.AlreadyOwned => GachaOutcome.AlreadyOwned,
                EconomyCodes.Unavailable => GachaOutcome.DataUnavailable,
                _ => GachaOutcome.Disabled,
            };
        }

        const string Unconfirmed = "サーバーから応答がありません。結果は確定していません。同じ種類をもう一度押すと、同じ取引IDで再送します（二重には引かれません）";

        public void Draw(GachaKind kind, string requestId, Action<GachaResult> done)
        {
            string k = GachaService.KindId(kind);
            Ask(() => session.Draw(k, requestId), a =>
            {
                SavePending();
                if (a == null || a.status == EconomyCodes.Error)
                {
                    error = null;
                    done(new GachaResult { Outcome = GachaOutcome.DataUnavailable, Message = Unconfirmed + (a?.message != null ? "\n" + a.message : "") });
                    return;
                }
                if (session.Wallet != null)
                    Apply(session.Wallet);
                if (a.Confirmed && a.record != null && history.Find(h => h.requestId == a.record.requestId) == null)
                    history.Add(a.record);
                var record = a.record != null ? history.Find(h => h.requestId == a.record.requestId) ?? a.record : null;
                done(new GachaResult
                {
                    Outcome = OutcomeOf(a.status, a.code),
                    Record = record,
                    Message = a.status == EconomyCodes.AlreadyApplied ? "この取引はサーバーで確定済みです（保存済みの結果を表示します）" : a.message,
                });
            });
        }

        public void Exchange(string itemId, string requestId, Action<GachaResult> done)
        {
            Ask(() => session.Exchange(itemId, requestId), a =>
            {
                SavePending();
                if (a == null || a.status == EconomyCodes.Error)
                {
                    done(new GachaResult { Outcome = GachaOutcome.DataUnavailable, Message = "サーバーから応答がありません。交換は確定していません。もう一度押すと同じ取引IDで再送します" });
                    return;
                }
                if (session.Wallet != null)
                    Apply(session.Wallet);
                string name = shop.Find(i => i.itemId == itemId)?.displayName ?? itemId;
                done(new GachaResult
                {
                    Outcome = OutcomeOf(a.status, a.code),
                    Message = a.status == EconomyCodes.Ok ? $"{name} を交換しました（サーバーで確定）" : a.status == EconomyCodes.AlreadyApplied ? "この交換はサーバーで確定済みです" : a.message,
                });
            });
        }
    }
}
