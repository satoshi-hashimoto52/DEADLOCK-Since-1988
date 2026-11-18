using System;
using System.Collections.Generic;
using Deadlock.Combat;

namespace Deadlock.Gacha
{
    public enum GachaKind
    {
        Single,
        Ten,
    }

    public enum GachaOutcome
    {
        Done,
        /// <summary>The same request was applied before: its result is returned, nothing is charged or given again.</summary>
        AlreadyApplied,
        NotEnoughPoints,
        NotEnoughMedals,
        /// <summary>The draw table is incomplete (a rarity without items): refused before anything is spent.</summary>
        InvalidConfiguration,
        NotOffered,
        AlreadyOwned,
        /// <summary>Saving failed: nothing changed (no point or medal lost).</summary>
        SaveFailed,
        /// <summary>The saved data could not be read: nothing is done until it is dealt with.</summary>
        DataUnavailable,
        Disabled,
    }

    public sealed class GachaResult
    {
        public GachaOutcome Outcome;
        public string Message;
        public HistoryRecord Record;

        public bool Ok => Outcome == GachaOutcome.Done || Outcome == GachaOutcome.AlreadyApplied;
    }

    /// <summary>
    /// The gacha, the exchange shop and the points / medals / owned items behind them (Stage 34, D-055 to D-059). Every
    /// change is one transaction: the new state is built on a copy, saved in one write, and only then becomes the state;
    /// a failed save leaves everything as it was. Every request has an id, and a request id seen before is never applied
    /// again (double taps, a retry after an interruption). The result is fixed and saved before any opening is shown.
    /// Local data is never trusted as official assets (D-059): the official game will hold this on the server.
    /// </summary>
    public sealed class GachaService
    {
        readonly IGachaStore store;
        readonly EventSchedule schedule;
        readonly IGachaClock clock;
        readonly IGachaRandom random;
        readonly ItemCatalog known;
        readonly Func<bool> useTestItems;
        readonly Func<GachaEvent> simulatedEvent;
        GachaStateData state = new GachaStateData();

        /// <summary>Every item the saved data may name (official and, in a development build, the test items).</summary>
        public ItemCatalog Catalog => known;
        public GachaStateData State => state;
        public bool HasSavedData { get; private set; }
        /// <summary>The saved data could not be read (damaged, newer): nothing is changed or written until resolved.</summary>
        public string LoadError { get; private set; }
        /// <summary>The last write was damaged and the one before it was used.</summary>
        public bool RecoveredFromPrevious { get; private set; }
        public event Action Changed;

        public GachaService(IGachaStore store, ItemCatalog known, EventSchedule schedule, IGachaClock clock, IGachaRandom random,
            Func<bool> useTestItems = null, Func<GachaEvent> simulatedEvent = null)
        {
            this.store = store;
            this.known = known;
            this.schedule = schedule ?? new EventSchedule(null);
            this.clock = clock ?? new DeviceClock();
            this.random = random ?? new SystemGachaRandom();
            this.useTestItems = useTestItems ?? (() => false);
            this.simulatedEvent = simulatedEvent ?? (() => null);
            Load();
        }

        public void Load()
        {
            LoadError = null;
            RecoveredFromPrevious = false;
            string json = store.Read();
            HasSavedData = json != null;
            if (json == null)
            {
                state = new GachaStateData();
                return;
            }
            var data = GachaSave.Parse(json, known, out string error, out int version);
            if (data == null)
            {
                var previous = GachaSave.Parse(store.ReadPrevious(), known, out _, out _);
                if (previous != null)
                {
                    state = previous;
                    RecoveredFromPrevious = true;
                    return;
                }
                state = new GachaStateData();
                LoadError = error;
                return;
            }
            state = data;
            if (version != GachaSave.CurrentVersion)
                TryCommit(state.Clone(), out _); // migrated: saved in the current version
        }

        // ------------------------------------------------------------------------------------------- what is offered

        /// <summary>The LEGENDARY event now: a development simulation, else the schedule by a trusted clock.</summary>
        public GachaEvent ActiveEvent => simulatedEvent() ?? schedule.Active(clock);

        IEnumerable<ItemDefinition> Offered()
        {
            bool tests = useTestItems();
            foreach (var i in known.Items)
                if (!i.devOnly || tests)
                    yield return i;
        }

        public GachaPool CurrentPool() => new GachaPool(Offered(), ActiveEvent);

        /// <summary>The exchange shop's line-up: exchangeable items of a decided rarity, LEGENDARY only in their event.</summary>
        public List<ItemDefinition> ShopLineup()
        {
            var ev = ActiveEvent;
            var list = new List<ItemDefinition>();
            foreach (var i in Offered())
            {
                if (!i.exchangeable || i.rarity == WeaponRarity.Unassigned)
                    continue;
                if (i.EventLimited && (ev == null || !ev.targetItemIds.Contains(i.itemId)))
                    continue;
                list.Add(i);
            }
            return list;
        }

        public static int Cost(GachaKind kind) => kind == GachaKind.Single ? GachaRates.SingleCost : GachaRates.TenCost;
        public static string KindId(GachaKind kind) => kind == GachaKind.Single ? "single" : "ten";

        public static string NewRequestId() => Guid.NewGuid().ToString("N");

        // ------------------------------------------------------------------------------------------- transactions

        /// <summary>Draws once (single) or 11 times (ten normal slots + one RARE-or-better slot), all in one transaction.</summary>
        public GachaResult Draw(GachaKind kind, string requestId)
        {
            if (LoadError != null)
                return Fail(GachaOutcome.DataUnavailable, "保存データを読めないため実行できません：" + LoadError);
            if (string.IsNullOrEmpty(requestId))
                throw new ArgumentException("request id");
            var done = state.FindRecord(requestId);
            if (done != null || state.FindRequest(requestId) != null)
                return new GachaResult { Outcome = GachaOutcome.AlreadyApplied, Record = done, Message = "この操作はすでに確定しています" };
            var pool = CurrentPool();
            if (!pool.IsOpen)
                return Fail(GachaOutcome.InvalidConfiguration, "ガチャの対象が揃っていないため実行できません（" + string.Join("／", pool.Problems) + "）");
            int cost = Cost(kind);
            if (state.points < cost)
                return Fail(GachaOutcome.NotEnoughPoints, $"ポイントが足りません（{state.points} / {cost} pt）");

            var drawn = new List<DrawnItem>();
            if (kind == GachaKind.Single)
                drawn.Add(pool.Draw(random, false));
            else
            {
                for (int i = 0; i < GachaRates.TenNormalSlots; i++)
                    drawn.Add(pool.Draw(random, false));
                drawn.Add(pool.Draw(random, true));
            }

            var next = state.Clone();
            next.points -= cost;
            string now = GachaSave.Now();
            var record = new HistoryRecord
            {
                requestId = requestId,
                timeUtc = now,
                kind = KindId(kind),
                cost = cost,
                eventId = pool.Event != null ? pool.Event.eventId : "",
                revealed = false,
            };
            // In slot order: the first time an item comes out it is owned, every later time (also within this 11-draw)
            // it is a duplicate turned into medals.
            for (int slot = 0; slot < drawn.Count; slot++)
            {
                var item = drawn[slot].Item;
                var owned = next.FindOwned(item.itemId);
                int medals = 0;
                if (owned == null)
                    next.owned.Add(new OwnedItem { itemId = item.itemId, acquiredUtc = now, source = "gacha" });
                else
                {
                    owned.duplicates++;
                    medals = GachaRates.DuplicateMedals(item.rarity);
                    next.medals += medals;
                }
                record.results.Add(new DrawResult
                {
                    slot = slot,
                    itemId = item.itemId,
                    displayName = item.displayName,
                    rarity = item.rarity.ToString(),
                    category = item.category.ToString(),
                    guaranteed = drawn[slot].Guaranteed,
                    firstTime = owned == null,
                    medals = medals,
                });
            }
            next.history.Add(record);
            while (next.history.Count > GachaSave.HistoryLimit)
                next.history.RemoveAt(0); // the oldest; owned items, medals and points stay
            AddRequest(next, new RequestRecord { requestId = requestId, kind = "draw", timeUtc = now });
            if (!TryCommit(next, out string error))
                return Fail(GachaOutcome.SaveFailed, "保存に失敗したため、何も変わっていません：" + error);
            return new GachaResult { Outcome = GachaOutcome.Done, Record = record };
        }

        /// <summary>Exchanges medals for an item of the shop's line-up, in one transaction.</summary>
        public GachaResult Exchange(string itemId, string requestId)
        {
            if (LoadError != null)
                return Fail(GachaOutcome.DataUnavailable, "保存データを読めないため実行できません：" + LoadError);
            if (state.FindRequest(requestId) != null)
                return new GachaResult { Outcome = GachaOutcome.AlreadyApplied, Message = "この交換はすでに確定しています" };
            var item = ShopLineup().Find(i => i.itemId == itemId);
            if (item == null)
                return Fail(GachaOutcome.NotOffered, "このアイテムは交換できません");
            if (state.Owns(itemId))
                return Fail(GachaOutcome.AlreadyOwned, "所持済みのため交換できません");
            int price = GachaRates.ExchangePrice(item.rarity);
            if (state.medals < price)
                return Fail(GachaOutcome.NotEnoughMedals, $"交換メダルが足りません（{state.medals} / {price} 枚）");
            var next = state.Clone();
            string now = GachaSave.Now();
            next.medals -= price;
            next.owned.Add(new OwnedItem { itemId = itemId, acquiredUtc = now, source = "exchange" });
            AddRequest(next, new RequestRecord { requestId = requestId, kind = "exchange", itemId = itemId, timeUtc = now });
            if (!TryCommit(next, out string error))
                return Fail(GachaOutcome.SaveFailed, "保存に失敗したため、何も変わっていません：" + error);
            return new GachaResult { Outcome = GachaOutcome.Done, Message = $"{item.displayName} を交換しました" };
        }

        /// <summary>The opening of a draw was shown or skipped (nothing else changes). Not shown yet: <see cref="Unrevealed"/>.</summary>
        public void MarkRevealed(string requestId)
        {
            var rec = state.FindRecord(requestId);
            if (rec == null || rec.revealed || LoadError != null)
                return;
            var next = state.Clone();
            next.FindRecord(requestId).revealed = true;
            TryCommit(next, out _); // on failure it is simply shown again next time
        }

        /// <summary>A draw saved but not shown (the app was closed during it): shown when the gacha opens again.</summary>
        public HistoryRecord Unrevealed()
        {
            for (int i = state.history.Count - 1; i >= 0; i--)
                if (!state.history[i].revealed)
                    return state.history[i];
            return null;
        }

        /// <summary>The request id of a match reward: one per match and place, so it is never given twice.</summary>
        public static string MatchRewardRequestId(string matchId, int slotId) => $"match:{matchId}:{slotId}";

        /// <summary>
        /// Points of a normal match (Stage 35, D-061), in one transaction like a draw: given once per match and place
        /// (the request id), nothing changes when saving fails. The result screen shown again or a restart never gives
        /// them again. The saved format is unchanged (a request of kind "match-reward").
        /// </summary>
        public GachaResult GrantMatchReward(string matchId, int slotId, int points)
        {
            if (LoadError != null)
                return Fail(GachaOutcome.DataUnavailable, "保存データを読めないため、ポイントを保存できません：" + LoadError);
            string requestId = MatchRewardRequestId(matchId, slotId);
            if (state.FindRequest(requestId) != null)
                return new GachaResult { Outcome = GachaOutcome.AlreadyApplied, Message = "この試合の報酬は付与済みです" };
            if (points <= 0)
                return new GachaResult { Outcome = GachaOutcome.Done, Message = "獲得ポイントはありません" };
            var next = state.Clone();
            next.points = (int)Math.Min(int.MaxValue, (long)next.points + points);
            AddRequest(next, new RequestRecord { requestId = requestId, kind = "match-reward", itemId = "", timeUtc = GachaSave.Now() });
            if (!TryCommit(next, out string error))
                return Fail(GachaOutcome.SaveFailed, "ポイントの保存に失敗しました（付与していません）：" + error);
            return new GachaResult { Outcome = GachaOutcome.Done, Message = $"{points}pt を獲得しました" };
        }

        /// <summary>Adds points (development builds only until matches give points, Stage 35).</summary>
        public GachaResult GrantDevPoints(int amount)
        {
            if (!GachaDev.Enabled)
                return Fail(GachaOutcome.Disabled, "開発用の機能は無効です");
            if (LoadError != null)
                return Fail(GachaOutcome.DataUnavailable, LoadError);
            var next = state.Clone();
            next.points = (int)Math.Min(int.MaxValue, (long)next.points + amount);
            if (!TryCommit(next, out string error))
                return Fail(GachaOutcome.SaveFailed, error);
            return new GachaResult { Outcome = GachaOutcome.Done };
        }

        // ------------------------------------------------------------------------------------------- export / import

        /// <summary>The whole gacha data as structured JSON (development builds only).</summary>
        public string Export() => GachaDev.Enabled ? GachaSave.Serialize(state) : null;

        public sealed class ImportCheck
        {
            public GachaStateData Data;
            public int Version;
            public string Error;
            public bool Ok => Data != null;
            public string Summary => Data == null ? Error
                : $"版{Version}・ポイント {Data.points}・メダル {Data.medals}・所持品 {Data.owned.Count} 件・履歴 {Data.history.Count} 件";
        }

        /// <summary>Step 1 of an import: format, version (older ones migrated), checksum and every value checked. Changes nothing.</summary>
        public ImportCheck CheckImport(string json)
        {
            if (!GachaDev.Enabled)
                return new ImportCheck { Error = "開発用の機能は無効です" };
            var data = GachaSave.Parse(json, known, out string error, out int version);
            return new ImportCheck { Data = data, Version = version, Error = error };
        }

        /// <summary>
        /// After the user confirmed: the current data is backed up, the gacha data replaced as a whole in one write, read
        /// back and checked again. If anything fails the previous data is written back. Returns the backup's place.
        /// </summary>
        public GachaResult ApplyImport(ImportCheck check)
        {
            if (!GachaDev.Enabled)
                return Fail(GachaOutcome.Disabled, "開発用の機能は無効です");
            if (check == null || !check.Ok)
                return Fail(GachaOutcome.NotOffered, "検証に合格していないデータは読み込めません");
            string current = store.Read();
            string backup;
            try
            {
                backup = store.Backup(current ?? "", "before_import");
            }
            catch (Exception e)
            {
                return Fail(GachaOutcome.SaveFailed, "バックアップを作れないため読み込みを中止しました：" + e.Message);
            }
            var before = state;
            if (!TryCommit(check.Data.Clone(), out string error, "local-imported-unverified"))
                return Fail(GachaOutcome.SaveFailed, "保存に失敗したため元のデータのままです：" + error);
            var reread = GachaSave.Parse(store.Read(), known, out string rereadError, out _);
            if (reread == null)
            {
                try
                {
                    if (current != null)
                        store.Write(current);
                }
                catch (Exception)
                {
                    // the backup file still holds it
                }
                state = before;
                Changed?.Invoke();
                return Fail(GachaOutcome.SaveFailed, "読み込み後の確認に失敗したため元に戻しました：" + rereadError);
            }
            state = reread;
            HasSavedData = true;
            Changed?.Invoke();
            return new GachaResult { Outcome = GachaOutcome.Done, Message = "読み込みました（バックアップ：" + backup + "）" };
        }

        // ------------------------------------------------------------------------------------------- internals

        static void AddRequest(GachaStateData next, RequestRecord r)
        {
            next.requests.Add(r);
            while (next.requests.Count > GachaSave.RequestLimit)
                next.requests.RemoveAt(0);
        }

        bool TryCommit(GachaStateData next, out string error, string origin = "local-unverified")
        {
            error = null;
            try
            {
                store.Write(GachaSave.Serialize(next, origin));
            }
            catch (Exception e)
            {
                error = e.Message;
                return false; // the state in memory and on disk stay as they were
            }
            state = next;
            HasSavedData = true;
            Changed?.Invoke();
            return true;
        }

        static GachaResult Fail(GachaOutcome outcome, string message) => new GachaResult { Outcome = outcome, Message = message };
    }

    /// <summary>
    /// Development-only switches of the gacha (Editor and development builds; never in a release build): the test items
    /// in the pool, a simulated LEGENDARY event, points for testing, export / import. Session only, never saved.
    /// </summary>
    public static class GachaDev
    {
        public static bool Enabled => UnityEngine.Debug.isDebugBuild && !SwitchedOff;
        /// <summary>Turns the development functions off (as in a release build), for checks.</summary>
        public static bool SwitchedOff { get; set; }

        static bool useTestItems;
        static GachaEvent simulatedEvent;

        public static bool UseTestItems
        {
            get => Enabled && useTestItems;
            set => useTestItems = value;
        }

        public static GachaEvent SimulatedEvent
        {
            get => Enabled ? simulatedEvent : null;
            set => simulatedEvent = value;
        }

        /// <summary>
        /// A simulated event with the test LEGENDARY items: the first run brings both; the rerun brings back only the
        /// skin (a rerun names the past items that return).
        /// </summary>
        public static GachaEvent TestEvent(bool rerun) => new GachaEvent
        {
            eventId = rerun ? "devtest_event_1_rerun" : "devtest_event_1",
            startUtc = "2000-01-01T00:00:00Z",
            endUtc = "2100-01-01T00:00:00Z",
            rerun = rerun,
            targetItemIds = rerun
                ? new List<string> { ItemCatalog.DevLegendarySkin }
                : new List<string> { ItemCatalog.DevLegendarySkin, ItemCatalog.DevLegendaryWeapon },
        };

        public static void Reset()
        {
            useTestItems = false;
            simulatedEvent = null;
            SwitchedOff = false;
        }
    }
}
