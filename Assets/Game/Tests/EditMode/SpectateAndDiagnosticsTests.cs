using System;
using System.Linq;
using Deadlock.Gacha;
using Deadlock.Match;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 36 without a scene: the recorded past (state 10 s ago, interpolation, nothing made up before the record or
    /// outside it, bounded memory, sound events at their times) and the load diagnostics (steps, the whole time not a
    /// sum, SLOW_LOAD over 10 s only, 100 entries, 30 days, failures in a row and the reset, JSON, development / release).
    /// </summary>
    public class SpectateAndDiagnosticsTests
    {
        static ReplaySample S(float t, Vector3 p, float yaw, float hp = 100f, string weapon = "proto_rifle", int kills = 0, bool alive = true, float pitch = 0f) =>
            new ReplaySample { Time = t, Position = p, Yaw = yaw, Pitch = pitch, Health = hp, WeaponId = weapon, Kills = kills, Alive = alive, EyeHeight = 1.5f };

        // ------------------------------------------------------------------ the recorded past

        [Test]
        public void Replay_StateTenSecondsAgo_Interpolated_StepValuesFromTheEarlierSample()
        {
            var r = new MatchReplay();
            for (int i = 0; i <= 30; i++)
                r.Record(2, S(i * 0.5f, new Vector3(i, 0f, 0f), i * 2f, 100f - i, i < 20 ? "proto_rifle" : "proto_smg", i / 10, true, i));
            Assert.IsTrue(r.TryPlaybackTime(15f, out float t));
            Assert.AreEqual(5f, t);
            Assert.IsTrue(r.TryState(2, 5f, out var s));
            Assert.AreEqual(new Vector3(10f, 0f, 0f), s.Position);
            Assert.AreEqual(20f, s.Yaw);
            Assert.AreEqual(90f, s.Health);
            Assert.AreEqual(1, s.Kills);
            Assert.IsTrue(r.TryState(2, 5.25f, out var mid));
            Assert.AreEqual(10.5f, mid.Position.x, 1e-4f, "positions between samples");
            Assert.AreEqual(21f, mid.Yaw, 1e-4f);
            Assert.AreEqual(10.5f, mid.Pitch, 1e-4f, "the look too");
            Assert.AreEqual(90f, mid.Health, "health of the earlier sample, not made up");
            Assert.IsTrue(r.TryState(2, 10.1f, out var later));
            Assert.AreEqual("proto_smg", later.WeaponId);
            Assert.AreEqual(2, later.Kills);
        }

        [Test]
        public void Replay_NothingBeforeTheRecord_NothingAfterIt_UnknownParticipant()
        {
            var r = new MatchReplay();
            Assert.IsFalse(r.TryPlaybackTime(30f, out _), "nothing recorded yet");
            r.Record(1, S(3f, Vector3.zero, 0f));
            r.Record(1, S(4f, Vector3.one, 0f));
            Assert.IsFalse(r.TryPlaybackTime(12.9f, out _), "less than 10 s recorded: waiting, not a made-up past");
            Assert.IsTrue(r.TryPlaybackTime(13f, out float t));
            Assert.AreEqual(3f, t);
            Assert.IsFalse(r.TryState(1, 2.9f, out _));
            Assert.IsFalse(r.TryState(1, 4.1f, out _), "after the last sample: unknown");
            Assert.IsFalse(r.TryState(9, 3.5f, out _));
            r.Record(1, S(3.5f, Vector3.zero, 0f));
            Assert.AreEqual(2, r.SampleCount, "time only goes forward");
        }

        [Test]
        public void Replay_KeepsTwelveSeconds_BoundedMemory()
        {
            var r = new MatchReplay();
            for (int i = 0; i < 60 * 40; i++) // 40 s at 60 per second, 8 participants
                for (int slot = 1; slot <= 8; slot++)
                    r.Record(slot, S(i / 60f, Vector3.zero, 0f));
            float last = (60 * 40 - 1) / 60f;
            Assert.LessOrEqual(r.SampleCount, 8 * MatchReplay.MaxSamplesPerSlot);
            Assert.GreaterOrEqual(r.FirstTime, last - MatchReplay.KeepSeconds - 0.05f);
            Assert.IsTrue(r.TryState(3, last - MatchReplay.DelaySeconds, out _), "10 s ago is still there");
            Assert.IsFalse(r.TryState(3, last - 20f, out _), "older than kept: gone");
            for (int i = 0; i < MatchReplay.MaxEvents + 100; i++)
                r.AddEvent(new ReplayEvent { Time = last, Kind = ReplayEventKind.Footstep });
            Assert.LessOrEqual(r.EventCount, MatchReplay.MaxEvents);
        }

        [Test]
        public void Replay_SoundEvents_AtTheirPastTimes_InOrder_PlaceKept()
        {
            var r = new MatchReplay();
            r.Record(1, S(0f, Vector3.zero, 0f));
            r.AddEvent(new ReplayEvent { Time = 2.0f, SlotId = 3, Kind = ReplayEventKind.Gunshot, Position = new Vector3(5f, 1f, 2f), Direction = Vector3.forward });
            r.AddEvent(new ReplayEvent { Time = 1.5f, SlotId = 4, Kind = ReplayEventKind.Footstep, Position = new Vector3(1f, 0f, 1f) });
            r.AddEvent(new ReplayEvent { Time = 2.5f, SlotId = 3, Kind = ReplayEventKind.Reload });
            // Real time 12.0 → shown time 2.0: the shot of 2.0 and the step of 1.5, not the reload of 2.5.
            var due = r.EventsBetween(1f, 12f - MatchReplay.DelaySeconds);
            CollectionAssert.AreEqual(new[] { ReplayEventKind.Footstep, ReplayEventKind.Gunshot }, due.Select(e => e.Kind).ToArray());
            Assert.AreEqual(new Vector3(5f, 1f, 2f), due[1].Position);
            Assert.IsEmpty(r.EventsBetween(2f, 2f), "each event once");
            Assert.AreEqual(ReplayEventKind.Reload, r.EventsBetween(2f, 2.5f).Single().Kind);
        }

        // ------------------------------------------------------------------ load diagnostics

        static LoadDiagnostics Diag(MemoryGachaStore store, bool dev, Func<DateTime> now = null) => new LoadDiagnostics(store, dev, now);

        static LoadTrace FullLoad(float scene, float assets, float ready)
        {
            var t = new LoadTrace(100f);
            t.Begin(LoadStage.SceneLoad, 100f);
            t.End(LoadStage.SceneLoad, 100f + scene);
            t.Begin(LoadStage.AssetInit, 100f + scene);
            t.End(LoadStage.AssetInit, 100f + scene + assets);
            t.MarkNotSupported(LoadStage.NetworkSync, "offline");
            t.Begin(LoadStage.ParticipantReady, 100f + scene + assets);
            t.End(LoadStage.ParticipantReady, 100f + scene + assets + ready);
            return t;
        }

        [Test]
        public void Trace_StepsMeasured_TotalIsWallTime_NotRunHasNoTime()
        {
            var t = new LoadTrace(10f);
            t.Begin(LoadStage.SceneLoad, 10f);
            t.Begin(LoadStage.AssetInit, 11f); // overlapping steps
            t.End(LoadStage.SceneLoad, 14f);
            t.End(LoadStage.AssetInit, 15f);
            var recs = t.Records();
            Assert.AreEqual(4f, recs[0].seconds);
            Assert.AreEqual(0f, recs[0].startSeconds);
            Assert.AreEqual(4f, recs[1].seconds);
            Assert.AreEqual(1f, recs[1].startSeconds);
            Assert.AreEqual(5f, t.Total(15f), "start to end, not 4 + 4");
            Assert.AreEqual(LoadTrace.NotRun, recs[2].status);
            Assert.AreEqual(-1f, recs[2].seconds, "no made-up time");
            t.MarkNotSupported(LoadStage.NetworkSync, "offline");
            Assert.AreEqual(LoadTrace.NotSupported, t.Records()[2].status);
            Assert.AreEqual(-1f, t.Records()[2].seconds);
        }

        [Test]
        public void SlowLoad_Over10sOnly_NoStop_DevelopmentRecordsSuccess_ReleaseDoesNot()
        {
            var store = new MemoryGachaStore();
            var dev = Diag(store, true);
            dev.Success(FullLoad(3f, 2f, 1f), 106f);
            Assert.AreEqual(new[] { "LOAD_SUCCESS" }, dev.Entries.Select(e => e.eventType).ToArray(), "6 s: no SLOW_LOAD");
            dev.Success(FullLoad(6f, 4f, 0.5f), 110.5f);
            CollectionAssert.AreEqual(new[] { "LOAD_SUCCESS", "LOAD_SUCCESS", "SLOW_LOAD" }, dev.Entries.Select(e => e.eventType).ToArray());
            var slow = dev.Entries.Last();
            Assert.AreEqual(10.5f, slow.totalSeconds, 1e-4f);
            Assert.AreEqual(4, slow.stages.Count);
            Assert.AreEqual("scene_load", slow.stages[0].stage);
            Assert.AreEqual(6f, slow.stages[0].seconds, 1e-4f);
            Assert.AreEqual(LoadTrace.NotSupported, slow.stages[2].status);
            Assert.AreEqual("development", slow.build);
            Assert.AreEqual(0, dev.ConsecutiveFailures, "slow is not a failure");
            dev.Success(FullLoad(5f, 5f, 0f), 110f);
            Assert.AreEqual("LOAD_SUCCESS", dev.Entries.Last().eventType, "exactly 10 s is not over 10 s");
            Assert.AreEqual(1, dev.Entries.Count(e => e.eventType == "SLOW_LOAD"));

            var release = Diag(new MemoryGachaStore(), false);
            release.Success(FullLoad(3f, 2f, 1f), 106f);
            Assert.IsEmpty(release.Entries, "a release build keeps no ordinary success");
            release.Success(FullLoad(8f, 4f, 1f), 113f);
            Assert.AreEqual("SLOW_LOAD", release.Entries.Single().eventType);
            Assert.AreEqual("release", release.Entries[0].build);
            Assert.IsNull(release.ExportJson(), "no export in a release build");
        }

        [Test]
        public void Timeout_FailuresInARow_WarnAt3_ResetOnParticipation()
        {
            var store = new MemoryGachaStore();
            var d = Diag(store, false);
            for (int i = 1; i <= 3; i++)
            {
                var t = new LoadTrace(0f);
                t.Begin(LoadStage.SceneLoad, 0f);
                d.Failure(t, 30f, "TIMEOUT", true);
                Assert.AreEqual(i, d.ConsecutiveFailures);
                Assert.AreEqual(i >= 3, d.ShouldWarn);
            }
            var timeout = d.Entries.First(e => e.eventType == "LOAD_TIMEOUT");
            Assert.AreEqual("scene_load", timeout.stage, "where it stopped");
            Assert.AreEqual(LoadTrace.Failed, timeout.stages[0].status);
            Assert.AreEqual(30f, timeout.totalSeconds);
            Assert.AreEqual("TIMEOUT", timeout.errorCategory);
            Assert.IsTrue(d.Entries.Any(e => e.eventType == "SLOW_LOAD"), "30 s is also slow");
            var setup = new LoadTrace(0f);
            setup.Begin(LoadStage.AssetInit, 1f);
            d.Failure(setup, 2f, "SETUP_FAILED", false);
            Assert.AreEqual("LOAD_FAILED", d.Entries.Last().eventType);
            Assert.AreEqual(4, d.ConsecutiveFailures);
            // Kept across a restart.
            Assert.AreEqual(4, Diag(store, false).ConsecutiveFailures);
            d.ParticipatedNormally();
            Assert.AreEqual(0, d.ConsecutiveFailures);
            Assert.IsFalse(d.ShouldWarn);
            Assert.AreEqual(0, Diag(store, false).ConsecutiveFailures);
        }

        [Test]
        public void AtMost100Entries_30Days_OldestFirst()
        {
            var store = new MemoryGachaStore();
            DateTime now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var d = Diag(store, true, () => now);
            for (int i = 0; i < 120; i++)
                d.Success(FullLoad(1f, 1f, 1f), 103f);
            Assert.AreEqual(LoadDiagnostics.MaxEntries, d.Entries.Count);
            now = now.AddDays(31);
            var later = Diag(store, true, () => now);
            Assert.IsEmpty(later.Entries, "older than 30 days: dropped");
            later.Success(FullLoad(1f, 1f, 1f), 103f);
            Assert.AreEqual(1, later.Entries.Count);
            now = now.AddDays(29);
            later.PruneNow();
            Assert.AreEqual(1, later.Entries.Count, "29 days: kept");
        }

        [Test]
        public void Export_StructuredJson_NoPersonalData_ApartFromOtherData()
        {
            var store = new MemoryGachaStore();
            var d = Diag(store, true);
            d.Success(FullLoad(6f, 5f, 0.2f), 111.2f);
            string json = d.ExportJson();
            Assert.IsNotNull(json);
            StringAssert.Contains("\"format\": \"deadlock.load-diagnostics\"", json);
            StringAssert.Contains("SLOW_LOAD", json);
            StringAssert.Contains("participant_ready", json);
            string homeFolder = "/" + "Users" + "/"; // a Mac home path (built here so this file holds none)
            foreach (var word in new[] { homeFolder, "deviceUniqueIdentifier", "token", "password", "@" })
                StringAssert.DoesNotContain(word, json);
            Assert.AreNotEqual(GachaAccount.FileName, LoadDiagnosticsAccount.FileName);
            Assert.AreNotEqual(GachaAccount.FolderName, LoadDiagnosticsAccount.FolderName);
            Assert.AreNotEqual(MatchRewardAccount.FolderName, LoadDiagnosticsAccount.FolderName);
            // A damaged log is started again, never a reason to stop a load.
            var broken = new MemoryGachaStore { Main = "{broken" };
            var fresh = Diag(broken, true);
            Assert.IsEmpty(fresh.Entries);
            fresh.Success(FullLoad(1f, 1f, 1f), 103f);
            Assert.AreEqual(1, fresh.Entries.Count);
            var failing = new MemoryGachaStore { FailNextWrites = 5 };
            var f = Diag(failing, true);
            Assert.DoesNotThrow(() => f.Success(FullLoad(1f, 1f, 1f), 103f));
            Assert.IsNotNull(f.LastError);
        }
    }
}
