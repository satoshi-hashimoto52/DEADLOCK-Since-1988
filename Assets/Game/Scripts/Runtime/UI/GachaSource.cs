using System;
using System.Collections.Generic;
using Deadlock.Gacha;

namespace Deadlock.UI
{
    /// <summary>
    /// Where the gacha screen's figures and actions come from (Stage 37E): the device's own Stage 34 data
    /// (<see cref="LocalGachaSource"/>, the only one in a release build), or — in development only — the account and
    /// economy service, which alone decides and keeps its figures (Deadlock.Dev's server source). The two never share
    /// data: the screen shows one of them, and nothing of the other is read or written. Actions answer through a
    /// callback (at once for the local data; when the service answers for the server).
    /// </summary>
    public interface IGachaSource
    {
        /// <summary>Shown above the gacha ("" for the local data).</summary>
        string Label { get; }
        /// <summary>Confirmed figures are known (the local data always; the server after its first answer).</summary>
        bool Ready { get; }
        /// <summary>An action is waiting for its answer.</summary>
        bool Busy { get; }
        int Points { get; }
        int Medals { get; }
        bool Owns(string itemId);
        /// <summary>Oldest first.</summary>
        IReadOnlyList<HistoryRecord> History { get; }
        /// <summary>The LEGENDARY event running now (null: none).</summary>
        GachaEvent ActiveEvent { get; }
        bool PoolOpen { get; }
        IReadOnlyList<string> PoolProblems { get; }
        /// <summary>Why nothing can be done now (unreadable data, no connection), or null.</summary>
        string Error { get; }
        bool UsesTestItems { get; }
        HistoryRecord Unrevealed();
        void MarkRevealed(string requestId);
        List<ItemDefinition> ShopLineup();
        void Draw(GachaKind kind, string requestId, Action<GachaResult> done);
        void Exchange(string itemId, string requestId, Action<GachaResult> done);
        /// <summary>Reads the figures again (the server: wallet, items, history, shop, and an unconfirmed action of before).</summary>
        void Refresh(Action done);
        /// <summary>Runs the answers that have arrived, on the main thread (the screen calls it every frame).</summary>
        void Pump();
    }

    /// <summary>The device's own gacha data (Stage 34's <see cref="GachaService"/>), unchanged.</summary>
    public sealed class LocalGachaSource : IGachaSource
    {
        readonly GachaService service;

        public LocalGachaSource(GachaService service) => this.service = service;

        public GachaService Service => service;
        public string Label => "";
        public bool Ready => true;
        public bool Busy => false;
        public int Points => service.State.points;
        public int Medals => service.State.medals;
        public bool Owns(string itemId) => service.State.Owns(itemId);
        public IReadOnlyList<HistoryRecord> History => service.State.history;
        public GachaEvent ActiveEvent => service.CurrentPool().Event;
        public bool PoolOpen => service.CurrentPool().IsOpen;
        public IReadOnlyList<string> PoolProblems => service.CurrentPool().Problems;
        public string Error => service.LoadError != null ? "保存データを読めません：" + service.LoadError : null;
        public bool UsesTestItems => GachaDev.UseTestItems;
        public HistoryRecord Unrevealed() => service.Unrevealed();
        public void MarkRevealed(string requestId) => service.MarkRevealed(requestId);
        public List<ItemDefinition> ShopLineup() => service.ShopLineup();
        public void Draw(GachaKind kind, string requestId, Action<GachaResult> done) => done(service.Draw(kind, requestId));
        public void Exchange(string itemId, string requestId, Action<GachaResult> done) => done(service.Exchange(itemId, requestId));
        public void Refresh(Action done) => done?.Invoke();
        public void Pump() { }
    }
}
