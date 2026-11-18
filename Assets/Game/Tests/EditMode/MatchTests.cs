using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Gacha;
using Deadlock.Match;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 35 without a scene (the session runs on explicit times): eight places, matchmaking and NPC filling,
    /// cancelling, loading and its timeout, the countdown, the 300 s limit and early ends, ranking with every tie-break,
    /// points only for living humans, takeovers keeping the record, no way back, the reward given once through the
    /// gacha data (failed saves, retries, restarts), a new match starting clean, and the start places.
    /// </summary>
    public class MatchTests
    {
        static MatchSession Confirmed(int humans, out List<MatchSlot> joined, float now = 0f)
        {
            var s = new MatchSession("m-test", now);
            joined = new List<MatchSlot>();
            for (int i = 0; i < humans; i++)
                joined.Add(s.JoinHuman(i == 0 ? "あなた" : $"P{i + 1}", i == 0));
            s.Tick(now + MatchRules.MatchmakingSeconds);
            return s;
        }

        /// <summary>A match in play at t = 50 (confirmed at 15, everyone ready at 20, countdown to 25).</summary>
        static MatchSession Playing(int humans, out List<MatchSlot> joined)
        {
            var s = Confirmed(humans, out joined);
            foreach (var h in joined)
                s.ReportReady(h, 20f);
            s.Tick(25f);
            Assert.AreEqual(MatchPhase.Playing, s.Phase);
            return s;
        }

        static void Kill(MatchSession s, int attacker, int victim) => s.RecordHit(attacker, victim, 1000f, out _);

        // ------------------------------------------------------------------ 1 to 5: places and matchmaking

        [Test]
        public void EightPlaces_NpcsFillAfter15s_UniqueIds()
        {
            var s = new MatchSession("m", 0f);
            var me = s.JoinHuman("あなた", true);
            s.JoinHuman("P2", false);
            s.Tick(14.9f);
            Assert.AreEqual(MatchPhase.Matchmaking, s.Phase);
            Assert.AreEqual(1f, s.MatchmakingRemaining(14f), 1e-4f);
            s.Tick(15f);
            Assert.AreEqual(MatchPhase.Loading, s.Phase);
            Assert.AreEqual(MatchRules.Slots, s.Slots.Count);
            Assert.AreEqual(2, s.Slots.Count(x => x.StartedAsHuman));
            Assert.AreEqual(6, s.Slots.Count(x => x.IsNpc));
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), s.Slots.Select(x => x.Id).ToArray(), "fixed ids 1 to 8");
            Assert.AreEqual(1, me.Id);
            Assert.IsTrue(s.Slots.Where(x => x.IsNpc).All(x => x.Load == SlotLoad.Ready), "NPCs are ready at once");
            Assert.IsNull(s.JoinHuman("late", false), "no one joins after the confirmation");
        }

        [Test]
        public void EightHumans_ConfirmedAtOnce()
        {
            var s = new MatchSession("m", 0f);
            for (int i = 0; i < 8; i++)
                Assert.IsNotNull(s.JoinHuman("P" + i, i == 0));
            Assert.IsNull(s.JoinHuman("ninth", false));
            s.Tick(3f);
            Assert.AreEqual(MatchPhase.Loading, s.Phase, "no waiting for the 15 s");
            Assert.AreEqual(0, s.Slots.Count(x => x.IsNpc));
        }

        [Test]
        public void NoHuman_NoMatch_CancelBeforeConfirmOnly()
        {
            var s = new MatchSession("m", 0f);
            var me = s.JoinHuman("あなた", true);
            Assert.IsTrue(s.Cancel(me, out _), "before the confirmation: no question, no penalty");
            Assert.AreEqual(MatchPhase.Cancelled, s.Phase);
            Assert.IsEmpty(s.Slots);

            var empty = new MatchSession("m2", 0f);
            var other = empty.JoinHuman("P", false);
            empty.Cancel(other, out _); // the only human left the queue
            empty.Tick(15f);
            Assert.AreEqual(MatchPhase.Cancelled, empty.Phase, "a match without a human never starts");
            StringAssert.Contains("人間", empty.CancelReason);

            var c = Confirmed(1, out var joined);
            Assert.IsFalse(c.Cancel(joined[0], out string reason), "after the confirmation");
            StringAssert.Contains("確定", reason);
            Assert.AreEqual(MatchPhase.Loading, c.Phase);
        }

        // ------------------------------------------------------------------ 6 to 9: loading and countdown

        [Test]
        public void Loading_TimeoutAt30s_NotReadyHumanReplaced_ZeroPoints()
        {
            var s = Confirmed(3, out var h); // confirmed at 15
            s.ReportLoad(h[0], 0.4f);
            s.ReportLoad(h[0], 0.2f);
            Assert.AreEqual(0.4f, h[0].LoadProgress, "progress only goes forward");
            s.ReportReady(h[0], 20f);
            s.ReportReady(h[1], 22f);
            s.Tick(44.9f);
            Assert.AreEqual(MatchPhase.Loading, s.Phase);
            Assert.AreEqual(7, s.ReadyCount);
            s.Tick(45f);
            Assert.AreEqual(MatchPhase.Countdown, s.Phase);
            Assert.IsTrue(h[2].TakenOverByNpc);
            Assert.AreEqual(SlotConnection.LoadFailed, h[2].Connection);
            Assert.AreEqual(SlotController.Npc, h[2].Controller);
            Assert.IsFalse(s.CanRejoin(h[2]));
            Assert.IsFalse(h[0].TakenOverByNpc);
            // Everyone ready ends the wait early.
            var e = Confirmed(2, out var g);
            e.ReportReady(g[0], 16f);
            e.ReportReady(g[1], 17f);
            Assert.AreEqual(MatchPhase.Countdown, e.Phase);
            // A failed set-up is a load failure too.
            var f = Confirmed(1, out var k);
            f.FailLoad(k[0], 18f);
            Assert.AreEqual(SlotConnection.LoadFailed, k[0].Connection);
            Assert.AreEqual(MatchPhase.Countdown, f.Phase);
        }

        [Test]
        public void Countdown_5s_NoCombat_DisconnectReplacedAtOnce_CountdownGoesOn()
        {
            var s = Confirmed(2, out var h);
            s.ReportReady(h[0], 16f);
            s.ReportReady(h[1], 16f); // countdown from 16
            Assert.AreEqual(MatchPhase.Countdown, s.Phase);
            Assert.IsFalse(s.CombatAllowed);
            Assert.AreEqual(0f, s.RecordHit(1, 3, 50f, out _), "no damage during the countdown");
            s.Tick(18f);
            Assert.AreEqual(3f, s.CountdownRemaining(18f), 1e-4f);
            s.Disconnect(h[1], 18f);
            Assert.IsTrue(h[1].TakenOverByNpc, "replaced at once");
            Assert.AreEqual(SlotConnection.Disconnected, h[1].Connection);
            Assert.AreEqual(3f, s.CountdownRemaining(18f), 1e-4f, "the countdown is not reset");
            s.Tick(20.9f);
            Assert.AreEqual(MatchPhase.Countdown, s.Phase);
            s.Tick(21f);
            Assert.AreEqual(MatchPhase.Playing, s.Phase);
            Assert.IsTrue(s.CombatAllowed);
            Assert.AreEqual(MatchRules.MatchSeconds, s.MatchRemaining(21f), 1e-4f, "the match clock starts at the end of the countdown");
        }

        // ------------------------------------------------------------------ 11 to 13: the end

        [Test]
        public void Ends_At300s_OneSurvivor_NoSurvivor_DecidedOnce()
        {
            var t = Playing(1, out _);
            t.Tick(25f + 299.9f);
            Assert.AreEqual(MatchPhase.Playing, t.Phase);
            t.Tick(25f + 300f);
            Assert.AreEqual(MatchPhase.Finished, t.Phase);
            Assert.AreEqual(MatchEndReason.TimeUp, t.Result.Reason);
            Assert.AreEqual(300f, t.Result.Duration, 1e-3f);

            var one = Playing(1, out _);
            for (int v = 2; v <= 8; v++)
                Kill(one, 1, v);
            int finishedCount = 0;
            one.Finished += _ => finishedCount++;
            one.Tick(40f);
            Assert.AreEqual(MatchEndReason.LastSurvivor, one.Result.Reason);
            var first = one.Result;
            one.Tick(400f);
            Kill(one, 1, 1);
            Assert.AreSame(first, one.Result, "never decided again");
            Assert.AreEqual(1, finishedCount);
            Assert.AreEqual(0f, one.RecordHit(1, 2, 10f, out _), "no more damage after the end");

            // Simultaneous deaths in one frame: the end is decided at the tick, once.
            var none = Playing(1, out _);
            for (int v = 3; v <= 8; v++)
                Kill(none, 2, v);
            Kill(none, 2, 1);
            Kill(none, 1, 2); // already dead attacker's victim was killed in the same frame (order of events)
            none.Tick(30f);
            Assert.AreEqual(MatchPhase.Finished, none.Phase);
        }

        [Test]
        public void NoSurvivor_EveryoneZero()
        {
            var s = Playing(2, out var h);
            for (int v = 3; v <= 8; v++)
                Kill(s, 1, v);
            // 1 and 2 kill each other in the same frame: 1's shot is recorded first, then 2's (already fired) shot.
            Kill(s, 1, 2);
            Kill(s, 2, 1);
            s.Tick(30f);
            Assert.AreEqual(MatchEndReason.NoSurvivor, s.Result.Reason);
            Assert.IsTrue(s.Result.Rows.All(r => r.Points == 0), "no survivor: nobody gets points");
        }

        // ------------------------------------------------------------------ 14 to 20: ranking and points

        [Test]
        public void Ranking_SurvivorsFirst_ThenKillsDamageHealthTakenId()
        {
            var a = new MatchSlot { Id = 5, Alive = false, Kills = 9 };
            var b = new MatchSlot { Id = 6, Alive = true, Kills = 0 };
            Assert.Less(MatchRanking.Compare(b, a), 0, "alive above dead whatever the kills");
            var k1 = new MatchSlot { Id = 2, Kills = 2, DamageDealt = 10 };
            var k2 = new MatchSlot { Id = 1, Kills = 1, DamageDealt = 900 };
            Assert.Less(MatchRanking.Compare(k1, k2), 0, "kills");
            var d1 = new MatchSlot { Id = 2, Kills = 1, DamageDealt = 300 };
            var d2 = new MatchSlot { Id = 1, Kills = 1, DamageDealt = 200, Health = 100 };
            Assert.Less(MatchRanking.Compare(d1, d2), 0, "damage dealt");
            var h1 = new MatchSlot { Id = 2, Kills = 1, DamageDealt = 200, Health = 80 };
            var h2 = new MatchSlot { Id = 1, Kills = 1, DamageDealt = 200, Health = 40 };
            Assert.Less(MatchRanking.Compare(h1, h2), 0, "health left");
            var t1 = new MatchSlot { Id = 2, Kills = 1, DamageDealt = 200, Health = 40, DamageTaken = 10 };
            var t2 = new MatchSlot { Id = 1, Kills = 1, DamageDealt = 200, Health = 40, DamageTaken = 60 };
            Assert.Less(MatchRanking.Compare(t1, t2), 0, "less damage taken");
            var i1 = new MatchSlot { Id = 1 };
            var i2 = new MatchSlot { Id = 2 };
            Assert.Less(MatchRanking.Compare(i1, i2), 0, "lower id");
            var order = MatchRanking.Order(new[] { i2, a, t2, b, i1 });
            Assert.AreEqual(5, order.Count);
            Assert.AreSame(a, order.Last(), "the dead one last");
        }

        [Test]
        public void NpcsRanked_KillsOnNpcsCount_PointsOnlyForLivingHumans()
        {
            Assert.AreEqual(new[] { 10, 7, 5, 3, 1, 1, 1, 1 }, Enumerable.Range(1, 8).Select(MatchRules.PointsForRank).ToArray());
            Assert.AreEqual(0, MatchRules.PointsForRank(9));
            var s = Playing(3, out var h); // humans 1 (local), 2, 3; NPCs 4..8
            Kill(s, 4, 5); // an NPC kills an NPC
            Kill(s, 4, 6);
            Kill(s, 4, 7);
            Kill(s, 1, 8); // the local human kills an NPC: counts like any kill
            Assert.AreEqual(1, s.Slot(1).Kills);
            Assert.AreEqual(100f, s.Slot(1).DamageDealt);
            Assert.AreEqual(100f, s.Slot(8).DamageTaken);
            s.RecordHit(4, 1, 30f, out _);
            s.RecordHit(1, 4, 20f, out _);
            Kill(s, 4, 3); // human 3 dies
            s.Leave(h[1], 40f); // human 2 leaves (NPC takes over, record kept)
            s.Tick(25f + 300f);
            var r = s.Result;
            // Survivors 4 (NPC, 4 kills), 1 (human, 1 kill), 2 (left, 0 kills); dead: 3, 5, 6, 7, 8.
            CollectionAssert.AreEqual(new[] { 4, 1, 2 }, r.Rows.Take(3).Select(x => x.SlotId).ToArray());
            Assert.AreEqual(1, r.Row(4).Rank);
            Assert.AreEqual(0, r.Row(4).Points, "an NPC never gets points");
            Assert.AreEqual(2, r.Row(1).Rank);
            Assert.AreEqual(7, r.Row(1).Points, "2nd behind an NPC: 7 pt");
            Assert.AreEqual(0, r.Row(2).Points, "left: 0 pt");
            Assert.AreEqual("途中離脱", r.Row(2).StateLabel);
            Assert.IsTrue(r.Row(2).TakenOverByNpc);
            Assert.AreEqual(0, r.Row(3).Points, "dead: 0 pt");
            Assert.AreEqual("死亡", r.Row(3).StateLabel);
            Assert.AreEqual(8, r.Rows.Select(x => x.Rank).Distinct().Count(), "every place ranked once");
            Assert.AreEqual(70f, r.Row(1).Health);
        }

        [Test]
        public void Disconnected_ZeroPoints_NoRejoin_RecordKept()
        {
            var s = Playing(2, out var h);
            Kill(s, 2, 5);
            s.RecordHit(2, 6, 40f, out _);
            s.RecordHit(7, 2, 25f, out _);
            s.Disconnect(h[1], 30f);
            var slot = s.Slot(2);
            Assert.AreEqual(SlotController.Npc, slot.Controller);
            Assert.AreEqual(1, slot.Kills, "the place keeps its record");
            Assert.AreEqual(140f, slot.DamageDealt);
            Assert.AreEqual(75f, slot.Health);
            Assert.IsFalse(s.CanRejoin(slot));
            // The NPC goes on in the same place: its kills still count there.
            Kill(s, 2, 6);
            Assert.AreEqual(2, slot.Kills);
            s.Tick(25f + 300f);
            Assert.AreEqual(0, s.Result.Row(2).Points);
            Assert.IsTrue(s.Result.Row(2).Alive);
            Assert.IsTrue(s.Result.Row(2).Departed);
        }

        // ------------------------------------------------------------------ 21, 22: the reward

        static GachaService Gacha(MemoryGachaStore store) =>
            new GachaService(store, new ItemCatalog(ItemCatalog.OfficialItems(WeaponCatalog.Load())), null, new FixedClock(System.DateTime.UtcNow), new SystemGachaRandom(1));

        [Test]
        public void Reward_OncePerMatchAndPlace_FailedSaveChangesNothing_RetryAndRestart()
        {
            var store = new MemoryGachaStore();
            var g = Gacha(store);
            store.FailNextWrites = 1;
            var failed = g.GrantMatchReward("m-1", 1, 10);
            Assert.AreEqual(GachaOutcome.SaveFailed, failed.Outcome);
            Assert.AreEqual(0, g.State.points, "no point when the save failed");
            Assert.IsNull(store.Main);
            var ok = g.GrantMatchReward("m-1", 1, 10); // retry
            Assert.AreEqual(GachaOutcome.Done, ok.Outcome);
            Assert.AreEqual(10, g.State.points);
            Assert.AreEqual(GachaOutcome.AlreadyApplied, g.GrantMatchReward("m-1", 1, 10).Outcome, "the result screen again");
            Assert.AreEqual(10, g.State.points);
            var restarted = Gacha(store);
            Assert.AreEqual(GachaOutcome.AlreadyApplied, restarted.GrantMatchReward("m-1", 1, 10).Outcome, "after a restart");
            Assert.AreEqual(10, restarted.State.points);
            Assert.AreEqual(GachaOutcome.Done, restarted.GrantMatchReward("m-2", 1, 7).Outcome, "another match");
            Assert.AreEqual(17, restarted.State.points);
            Assert.AreEqual(GachaOutcome.Done, restarted.GrantMatchReward("m-3", 1, 0).Outcome);
            Assert.AreEqual(17, restarted.State.points, "nothing for 0");
            // The saved format is the same as Stage 34's (version 2), readable as before.
            StringAssert.Contains("\"version\": 2", store.Main);
            StringAssert.Contains("match-reward", store.Main);
            Assert.IsNotNull(GachaSave.Parse(store.Main, g.Catalog, out string error, out _), error);
            Assert.IsEmpty(restarted.State.history, "no draw recorded");
        }

        // ------------------------------------------------------------------ 25: a new match starts clean

        [Test]
        public void NewMatch_NewPlaces_NoRecordCarriedOver()
        {
            var s = Playing(1, out _);
            Kill(s, 1, 2);
            s.RecordHit(3, 1, 60f, out _);
            var again = Playing(1, out _);
            var me = again.Slot(1);
            Assert.AreEqual(0, me.Kills);
            Assert.AreEqual(0f, me.DamageDealt);
            Assert.AreEqual(MatchRules.MaxHealth, me.Health);
            Assert.IsTrue(again.Slots.All(x => x.Alive && x.Kills == 0));
            Assert.AreNotSame(s.Slot(1), me);
        }

        // ------------------------------------------------------------------ 10: start places

        [Test]
        public void SpawnPlanner_FarApart_OutOfSight_Distinct_Varies_RefusesWhenImpossible()
        {
            // A 40 x 40 m field of points, a wall along x = 0 blocking sight between the halves.
            var points = new List<Vector3>();
            for (int x = -20; x <= 20; x += 2)
                for (int z = -20; z <= 20; z += 2)
                    points.Add(new Vector3(x, 0f, z));
            points.Add(points[0]); // a duplicate point is never used twice
            bool Sight(Vector3 a, Vector3 b) => Mathf.Sign(a.x) == Mathf.Sign(b.x) && Mathf.Abs(a.x) > 0.1f && Mathf.Abs(b.x) > 0.1f && Vector3.Distance(a, b) < 12f;
            var planner = new SpawnPlanner { WantedDistance = 14f, FloorDistance = 5f, LineOfSight = Sight, Shelter = p => Mathf.Abs(p.x) < 3f ? 1f : 0f };
            Assert.IsTrue(planner.TryPlan(points, 8, 1, out var spawns), string.Join(";", planner.Log));
            Assert.AreEqual(8, spawns.Count);
            for (int i = 0; i < 8; i++)
                for (int j = i + 1; j < 8; j++)
                {
                    Assert.GreaterOrEqual(Vector3.Distance(spawns[i], spawns[j]), 5f - 1e-4f, "apart");
                    Assert.Greater(Vector3.Distance(spawns[i], spawns[j]), 0.1f, "never the same point");
                    Assert.IsFalse(Sight(spawns[i], spawns[j]), "out of direct sight");
                }
            Assert.IsTrue(spawns.All(p => points.Contains(p)), "only valid candidate points");
            Assert.IsTrue(planner.TryPlan(points, 8, 2, out var other));
            Assert.IsFalse(spawns.SequenceEqual(other), "differs from match to match");

            // Too few points: nothing placed, the reason logged.
            var few = new SpawnPlanner();
            Assert.IsFalse(few.TryPlan(points.Take(5).ToList(), 8, 1, out var none));
            Assert.IsNull(none);
            StringAssert.Contains("足りません", few.Log[0]);
            // Points too close together for the lowest distance: refused, not forced.
            var tight = new List<Vector3>();
            for (int i = 0; i < 12; i++)
                tight.Add(new Vector3(i * 2f, 0f, 0f));
            var strict = new SpawnPlanner { WantedDistance = 10f, FloorDistance = 5f };
            Assert.IsFalse(strict.TryPlan(tight, 8, 1, out _));
            Assert.IsNotEmpty(strict.Log);
        }
    }
}
