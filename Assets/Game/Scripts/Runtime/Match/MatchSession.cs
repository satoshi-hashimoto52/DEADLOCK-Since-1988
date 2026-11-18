using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deadlock.Match
{
    /// <summary>The confirmed numbers of the normal match (D-060 / D-061).</summary>
    public static class MatchRules
    {
        public const int Slots = 8;
        public const float MatchmakingSeconds = 15f;
        public const float LoadSeconds = 30f;
        public const float CountdownSeconds = 5f;
        public const float MatchSeconds = 300f;
        public const float MaxHealth = 100f;

        static readonly int[] RankPoints = { 10, 7, 5, 3, 1, 1, 1, 1 };

        /// <summary>Points of a rank (1 to 8) for an eligible player.</summary>
        public static int PointsForRank(int rank) => rank >= 1 && rank <= RankPoints.Length ? RankPoints[rank - 1] : 0;
    }

    public enum MatchPhase
    {
        Matchmaking,
        Loading,
        Countdown,
        Playing,
        Finished,
        /// <summary>Not started: cancelled before the confirmation, or no human at the confirmation.</summary>
        Cancelled,
    }

    public enum SlotController
    {
        Human,
        Npc,
    }

    public enum SlotConnection
    {
        Connected,
        Disconnected,
        Left,
        LoadFailed,
    }

    public enum SlotLoad
    {
        Loading,
        Ready,
        Failed,
    }

    public enum MatchEndReason
    {
        None,
        TimeUp,
        LastSurvivor,
        NoSurvivor,
    }

    /// <summary>
    /// One of the eight places of a match: its fixed id in the match (1 to 8), who controls it (a human, or an NPC from the
    /// start or since a takeover), connection and load state, life, and its own record. The record belongs to the
    /// place: a takeover keeps it, and a new match starts with new places.
    /// </summary>
    public sealed class MatchSlot
    {
        public int Id;
        public string Name;
        public SlotController Controller;
        public bool StartedAsHuman;
        public bool IsLocal;
        public SlotConnection Connection = SlotConnection.Connected;
        public SlotLoad Load = SlotLoad.Loading;
        /// <summary>0..1, reported by the loading (never made up).</summary>
        public float LoadProgress;
        public bool Alive = true;
        public float Health = MatchRules.MaxHealth;
        public float MaxHealth = MatchRules.MaxHealth;
        public int Kills;
        public float DamageDealt;
        public float DamageTaken;
        public bool TakenOverByNpc;
        public SlotConnection TakeoverReason;
        public int Rank;
        public int Points;

        public bool IsNpc => Controller == SlotController.Npc;

        /// <summary>A human who is no longer in the match (left, disconnected, failed to load).</summary>
        public bool Departed => StartedAsHuman && TakenOverByNpc;

        /// <summary>Gets points (D-061): a human still in control and connected, alive at the end. Never an NPC.</summary>
        public bool RewardEligible => StartedAsHuman && Controller == SlotController.Human && Connection == SlotConnection.Connected && Alive;
    }

    public sealed class MatchResultRow
    {
        public int Rank;
        public int SlotId;
        public string Name;
        public bool Alive;
        public bool Departed;
        public bool StartedAsHuman;
        public bool TakenOverByNpc;
        public bool IsLocal;
        public int Kills;
        public float DamageDealt;
        public float DamageTaken;
        public float Health;
        public int Points;

        public string StateLabel => Departed ? "途中離脱" : Alive ? "生存" : "死亡";
    }

    /// <summary>The fixed outcome of one match: decided once, never changed afterwards.</summary>
    public sealed class MatchResult
    {
        public string MatchId;
        public MatchEndReason Reason;
        public float Duration;
        public readonly List<MatchResultRow> Rows = new List<MatchResultRow>();

        public MatchResultRow Row(int slotId) => Rows.Find(r => r.SlotId == slotId);
    }

    /// <summary>
    /// The order of the eight places (D-061): survivors above the dead; then more kills, more damage dealt, more health
    /// left, less damage taken, and the lower fixed id. Kills and damage on NPCs count the same as on humans.
    /// </summary>
    public static class MatchRanking
    {
        public static int Compare(MatchSlot a, MatchSlot b)
        {
            int c = b.Alive.CompareTo(a.Alive);
            if (c != 0) return c;
            c = b.Kills.CompareTo(a.Kills);
            if (c != 0) return c;
            c = b.DamageDealt.CompareTo(a.DamageDealt);
            if (c != 0) return c;
            c = b.Health.CompareTo(a.Health);
            if (c != 0) return c;
            c = a.DamageTaken.CompareTo(b.DamageTaken);
            if (c != 0) return c;
            return a.Id.CompareTo(b.Id);
        }

        public static List<MatchSlot> Order(IEnumerable<MatchSlot> slots)
        {
            var list = new List<MatchSlot>(slots);
            list.Sort(Compare);
            return list;
        }
    }

    /// <summary>
    /// The state of one normal match, driven by explicit times (<see cref="Tick"/>) so every rule can be checked without
    /// a scene or a network: matchmaking (up to 15 s, ends at 8 humans, NPCs fill the rest, no match without a human,
    /// cancel only before the confirmation), loading (up to 30 s; a human not ready by then is replaced by an NPC and
    /// gets nothing), the 5 s countdown (no combat; a disconnection is replaced at once and the countdown goes on), the
    /// match (up to 300 s from the end of the countdown; ends at one or no survivor), and the result, decided once.
    /// Today this runs on the device for the offline check; on a server later (the decisions are all here).
    /// </summary>
    public sealed class MatchSession
    {
        readonly List<MatchSlot> queue = new List<MatchSlot>();
        readonly List<MatchSlot> slots = new List<MatchSlot>();
        float phaseStart;
        float playStart;
        int npcNumber;

        public string MatchId { get; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Matchmaking;
        public IReadOnlyList<MatchSlot> Slots => slots;
        public IReadOnlyList<MatchSlot> Queue => queue;
        public MatchResult Result { get; private set; }
        public string CancelReason { get; private set; }
        public float LastTick { get; private set; }
        /// <summary>What happened (load timeouts, takeovers, the end), for checks and later diagnostics.</summary>
        public readonly List<string> Log = new List<string>();

        public event Action<MatchPhase> PhaseChanged;
        public event Action<MatchSlot> TakenOver;
        public event Action<MatchResult> Finished;

        /// <summary>Length of play (300 s, D-061). Only development checks pass a shorter one (online server -dev-match-seconds).</summary>
        public float MatchSeconds { get; }

        public MatchSession(string matchId, float now, float matchSeconds = MatchRules.MatchSeconds)
        {
            MatchId = matchId;
            MatchSeconds = matchSeconds;
            phaseStart = now;
            LastTick = now;
        }

        public static string NewMatchId() => "m-" + Guid.NewGuid().ToString("N");

        public bool Confirmed => Phase != MatchPhase.Matchmaking && Phase != MatchPhase.Cancelled;
        public bool CombatAllowed => Phase == MatchPhase.Playing;
        public int HumanCount => Confirmed ? slots.FindAll(s => s.StartedAsHuman).Count : queue.Count;
        public int ReadyCount => slots.FindAll(s => s.Load == SlotLoad.Ready).Count;
        public int AliveCount => slots.FindAll(s => s.Alive).Count;
        public MatchSlot Local => (Confirmed ? slots : queue).Find(s => s.IsLocal);
        public MatchSlot Slot(int id) => slots.Find(s => s.Id == id);

        public float MatchmakingRemaining(float now) => Phase == MatchPhase.Matchmaking ? Math.Max(0f, MatchRules.MatchmakingSeconds - (now - phaseStart)) : 0f;
        public float LoadingRemaining(float now) => Phase == MatchPhase.Loading ? Math.Max(0f, MatchRules.LoadSeconds - (now - phaseStart)) : 0f;
        public float CountdownRemaining(float now) => Phase == MatchPhase.Countdown ? Math.Max(0f, MatchRules.CountdownSeconds - (now - phaseStart)) : 0f;
        public float MatchElapsed(float now) => Phase == MatchPhase.Playing ? now - playStart : Phase == MatchPhase.Finished ? Result.Duration : 0f;
        public float MatchRemaining(float now) => Math.Max(0f, MatchSeconds - MatchElapsed(now));

        // ------------------------------------------------------------------------------------------- matchmaking

        /// <summary>A human joins the queue (up to 8). Null when it is full or no longer matchmaking.</summary>
        public MatchSlot JoinHuman(string name, bool isLocal)
        {
            if (Phase != MatchPhase.Matchmaking || queue.Count >= MatchRules.Slots)
                return null;
            var s = new MatchSlot { Name = name, Controller = SlotController.Human, StartedAsHuman = true, IsLocal = isLocal };
            queue.Add(s);
            return s;
        }

        /// <summary>
        /// Cancels a human's matchmaking: allowed (no confirmation, no penalty) only before the match is confirmed.
        /// The local player cancelling ends the matchmaking for this device.
        /// </summary>
        public bool Cancel(MatchSlot human, out string reason)
        {
            if (Phase != MatchPhase.Matchmaking)
            {
                reason = "マッチングが確定したためキャンセルできません";
                return false;
            }
            reason = null;
            queue.Remove(human);
            if (human != null && human.IsLocal)
                SetCancelled("キャンセルしました");
            return true;
        }

        void Confirm(float now)
        {
            int id = 1;
            foreach (var h in queue)
            {
                h.Id = id++;
                slots.Add(h);
            }
            while (slots.Count < MatchRules.Slots)
            {
                npcNumber++;
                slots.Add(new MatchSlot { Id = id++, Name = $"NPC-{npcNumber}", Controller = SlotController.Npc, Load = SlotLoad.Ready, LoadProgress = 1f });
            }
            int humans = queue.Count; // counted before the queue is emptied (HumanCount reads the queue until the phase changes)
            queue.Clear();
            Log.Add($"confirmed: {humans} humans, {MatchRules.Slots - humans} NPCs");
            SetPhase(MatchPhase.Loading, now);
        }

        // ------------------------------------------------------------------------------------------- loading

        public void ReportLoad(MatchSlot slot, float progress)
        {
            if (Phase == MatchPhase.Loading && slot.Load == SlotLoad.Loading)
                slot.LoadProgress = Math.Max(slot.LoadProgress, Math.Min(1f, Math.Max(0f, progress)));
        }

        public void ReportReady(MatchSlot slot, float now)
        {
            if (Phase != MatchPhase.Loading || slot.Load != SlotLoad.Loading || slot.Controller != SlotController.Human)
                return;
            slot.Load = SlotLoad.Ready;
            slot.LoadProgress = 1f;
            Tick(now);
        }

        /// <summary>A human's loading failed (could not be set up): replaced by an NPC like a load timeout, nothing for them.</summary>
        public void FailLoad(MatchSlot slot, float now)
        {
            if (Phase != MatchPhase.Loading || slot.Load == SlotLoad.Ready)
                return;
            slot.Load = SlotLoad.Failed;
            TakeOver(slot, SlotConnection.LoadFailed);
            Tick(now);
        }

        // ------------------------------------------------------------------------------------------- leaving

        /// <summary>
        /// A human's connection is lost. Before the confirmation: out of the queue. After it: the place is taken over by
        /// an NPC at once (during the countdown too, which goes on). The human gets nothing and cannot come back.
        /// </summary>
        public void Disconnect(MatchSlot slot, float now)
        {
            if (Phase == MatchPhase.Matchmaking)
            {
                queue.Remove(slot);
                return;
            }
            if (slot.Load == SlotLoad.Loading && Phase == MatchPhase.Loading)
                slot.Load = SlotLoad.Failed;
            TakeOver(slot, SlotConnection.Disconnected);
            Tick(now);
        }

        /// <summary>The human leaves the match on purpose (after the confirmation dialog): taken over like a disconnection.</summary>
        public void Leave(MatchSlot slot, float now)
        {
            if (Phase == MatchPhase.Matchmaking)
            {
                Cancel(slot, out _);
                return;
            }
            TakeOver(slot, SlotConnection.Left);
            Tick(now);
        }

        /// <summary>A human who left, disconnected or failed to load never comes back into the same match.</summary>
        public bool CanRejoin(MatchSlot slot) => slot != null && !slot.Departed && Phase != MatchPhase.Finished && Phase != MatchPhase.Cancelled;

        void TakeOver(MatchSlot slot, SlotConnection reason)
        {
            if (slot == null || !slot.StartedAsHuman || slot.TakenOverByNpc || Phase == MatchPhase.Finished || Phase == MatchPhase.Cancelled)
                return;
            slot.Controller = SlotController.Npc;
            slot.Connection = reason;
            slot.TakenOverByNpc = true;
            slot.TakeoverReason = reason;
            if (slot.Load != SlotLoad.Ready)
                slot.Load = SlotLoad.Ready; // the NPC is ready at once
            Log.Add($"slot {slot.Id}: {reason} → NPC (record kept)");
            TakenOver?.Invoke(slot);
        }

        // ------------------------------------------------------------------------------------------- combat

        /// <summary>
        /// Damage from one place to another, only while playing. The record and health are the session's: damage
        /// dealt / taken, and the kill when the victim's health reaches 0. Returns the damage actually applied.
        /// </summary>
        public float RecordHit(int attackerId, int victimId, float amount, out bool killed)
        {
            killed = false;
            if (Phase != MatchPhase.Playing || amount <= 0f || attackerId == victimId)
                return 0f;
            var attacker = Slot(attackerId);
            var victim = Slot(victimId);
            if (attacker == null || victim == null || !victim.Alive)
                return 0f;
            float applied = Math.Min(amount, victim.Health);
            victim.Health -= applied;
            victim.DamageTaken += applied;
            attacker.DamageDealt += applied;
            if (victim.Health <= 0f)
            {
                victim.Health = 0f;
                victim.Alive = false;
                attacker.Kills++;
                killed = true;
            }
            return applied;
        }

        // ------------------------------------------------------------------------------------------- time

        /// <summary>Moves the session on to <paramref name="now"/> (seconds). Call once a frame; the end is decided here only.</summary>
        public void Tick(float now)
        {
            LastTick = now;
            switch (Phase)
            {
                case MatchPhase.Matchmaking:
                    if (queue.Count >= MatchRules.Slots)
                        Confirm(now);
                    else if (now - phaseStart >= MatchRules.MatchmakingSeconds)
                    {
                        if (queue.Count == 0)
                            SetCancelled("人間のプレイヤーがいないため、試合を開始しませんでした");
                        else
                            Confirm(now);
                    }
                    break;
                case MatchPhase.Loading:
                    if (slots.TrueForAll(s => s.Load == SlotLoad.Ready))
                        SetPhase(MatchPhase.Countdown, now);
                    else if (now - phaseStart >= MatchRules.LoadSeconds)
                    {
                        foreach (var s in slots)
                            if (s.Load != SlotLoad.Ready)
                            {
                                s.Load = SlotLoad.Failed;
                                TakeOver(s, SlotConnection.LoadFailed);
                            }
                        SetPhase(MatchPhase.Countdown, now);
                    }
                    break;
                case MatchPhase.Countdown:
                    if (now - phaseStart >= MatchRules.CountdownSeconds)
                    {
                        playStart = now;
                        SetPhase(MatchPhase.Playing, now);
                    }
                    break;
                case MatchPhase.Playing:
                    int alive = AliveCount;
                    if (alive == 0)
                        Finish(now, MatchEndReason.NoSurvivor);
                    else if (alive == 1)
                        Finish(now, MatchEndReason.LastSurvivor);
                    else if (now - playStart >= MatchSeconds)
                        Finish(now, MatchEndReason.TimeUp);
                    break;
            }
        }

        /// <summary>Decides the result once: ranks for all eight, points for the eligible. Later calls change nothing.</summary>
        void Finish(float now, MatchEndReason reason)
        {
            if (Phase == MatchPhase.Finished)
                return;
            var result = new MatchResult
            {
                MatchId = MatchId,
                Reason = reason,
                Duration = Math.Min(MatchSeconds, now - playStart),
            };
            int rank = 1;
            foreach (var s in MatchRanking.Order(slots))
            {
                s.Rank = rank++;
                s.Points = s.RewardEligible ? MatchRules.PointsForRank(s.Rank) : 0;
                result.Rows.Add(new MatchResultRow
                {
                    Rank = s.Rank,
                    SlotId = s.Id,
                    Name = s.Name,
                    Alive = s.Alive,
                    Departed = s.Departed,
                    StartedAsHuman = s.StartedAsHuman,
                    TakenOverByNpc = s.TakenOverByNpc,
                    IsLocal = s.IsLocal,
                    Kills = s.Kills,
                    DamageDealt = s.DamageDealt,
                    DamageTaken = s.DamageTaken,
                    Health = s.Health,
                    Points = s.Points,
                });
            }
            Result = result;
            Log.Add($"finished: {reason} at {result.Duration.ToString("0.0", CultureInfo.InvariantCulture)} s");
            SetPhase(MatchPhase.Finished, now);
            Finished?.Invoke(result);
        }

        void SetCancelled(string reason)
        {
            CancelReason = reason;
            Log.Add("cancelled: " + reason);
            SetPhase(MatchPhase.Cancelled, LastTick);
        }

        void SetPhase(MatchPhase phase, float now)
        {
            Phase = phase;
            phaseStart = now;
            PhaseChanged?.Invoke(phase);
            if (phase == MatchPhase.Countdown || phase == MatchPhase.Loading)
                Tick(now); // a phase may end at once (everyone ready)
        }
    }
}
