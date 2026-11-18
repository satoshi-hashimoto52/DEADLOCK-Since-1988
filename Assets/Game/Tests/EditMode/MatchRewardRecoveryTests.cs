using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Gacha;
using Deadlock.Match;
using NUnit.Framework;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 35 completion: a match reward is written down before it is given, recovered after a failed save or a stop
    /// (restart), never given twice (also when it was given but not yet marked), never re-applied once completed, and
    /// damaged or wrong entries are rejected. The gacha data keeps its Stage 34 format.
    /// </summary>
    public class MatchRewardRecoveryTests
    {
        static GachaService Gacha(MemoryGachaStore store) =>
            new GachaService(store, new ItemCatalog(ItemCatalog.OfficialItems(WeaponCatalog.Load())), null, new FixedClock(System.DateTime.UtcNow), new SystemGachaRandom(1));

        static MatchResultRow Row(int slot, int rank, int points) => new MatchResultRow { SlotId = slot, Rank = rank, Points = points };

        [Test]
        public void FailedSave_ThenRestart_RecoveredOnce_CompletedNeverAgain()
        {
            var gachaStore = new MemoryGachaStore();
            var journalStore = new MemoryGachaStore();
            var gacha = Gacha(gachaStore);
            var journal = new MatchRewardJournal(journalStore);
            Assert.IsTrue(journal.RecordPending("m-a", Row(1, 2, 7), out _));
            gachaStore.FailNextWrites = 1;
            Assert.AreEqual(GachaOutcome.SaveFailed, gacha.GrantMatchReward("m-a", 1, 7).Outcome);
            Assert.AreEqual(0, gacha.State.points);
            Assert.AreEqual(1, journal.Pending.Count, "still pending");

            // The app stops here. Next start: both read again from their files.
            var gacha2 = Gacha(gachaStore);
            var journal2 = new MatchRewardJournal(journalStore);
            var messages = new List<string>();
            Assert.AreEqual(7, journal2.Recover(gacha2, messages));
            Assert.AreEqual(7, gacha2.State.points);
            Assert.IsEmpty(journal2.Pending);
            StringAssert.Contains("7pt", messages[0]);
            Assert.AreEqual(PendingReward.Completed, journal2.Entries[0].state);

            // Another start: nothing left to do.
            var gacha3 = Gacha(gachaStore);
            var journal3 = new MatchRewardJournal(journalStore);
            Assert.AreEqual(0, journal3.Recover(gacha3));
            Assert.AreEqual(7, gacha3.State.points);
            // A completed entry is never applied again, even if the result is shown again.
            Assert.IsTrue(journal3.RecordPending("m-a", Row(1, 2, 7), out _));
            Assert.IsEmpty(journal3.Pending);
        }

        [Test]
        public void GivenButNotMarked_OnlyMarked_NoSecondGrant()
        {
            var gachaStore = new MemoryGachaStore();
            var journalStore = new MemoryGachaStore();
            var gacha = Gacha(gachaStore);
            var journal = new MatchRewardJournal(journalStore);
            journal.RecordPending("m-b", Row(1, 1, 10), out _);
            Assert.AreEqual(GachaOutcome.Done, gacha.GrantMatchReward("m-b", 1, 10).Outcome);
            journalStore.FailNextWrites = 1; // the "completed" mark is lost (stop during the write)
            journal.Mark("m-b", 1, PendingReward.Completed, "x");
            var journal2 = new MatchRewardJournal(journalStore);
            Assert.AreEqual(1, journal2.Pending.Count);
            var gacha2 = Gacha(gachaStore);
            Assert.AreEqual(0, journal2.Recover(gacha2), "already in the gacha data: not given again");
            Assert.AreEqual(10, gacha2.State.points);
            Assert.IsEmpty(journal2.Pending);
        }

        [Test]
        public void SaveStillFailing_StaysPending_RetriedLater()
        {
            var gachaStore = new MemoryGachaStore();
            var journal = new MatchRewardJournal(new MemoryGachaStore());
            journal.RecordPending("m-c", Row(3, 4, 3), out _);
            var gacha = Gacha(gachaStore);
            gachaStore.FailNextWrites = 1;
            Assert.AreEqual(0, journal.Recover(gacha));
            Assert.AreEqual(1, journal.Pending.Count);
            Assert.AreEqual(3, journal.Recover(gacha));
            Assert.AreEqual(3, gacha.State.points);
        }

        [Test]
        public void DamagedOrWrongEntries_Rejected_NeverApplied()
        {
            var journalStore = new MemoryGachaStore();
            var journal = new MatchRewardJournal(journalStore);
            journal.RecordPending("m-d", Row(1, 1, 10), out _);
            // Points raised by hand (checksum), points not matching the rank, a wrong match id.
            journalStore.Main = journalStore.Main.Replace("\"points\": 10", "\"points\": 99");
            var tampered = new MatchRewardJournal(journalStore);
            var gacha = Gacha(new MemoryGachaStore());
            var messages = new List<string>();
            Assert.AreEqual(0, tampered.Recover(gacha, messages));
            Assert.AreEqual(0, gacha.State.points);
            Assert.AreEqual(PendingReward.Rejected, tampered.Entries[0].state);
            StringAssert.Contains("付与しません", messages[0]);

            var wrong = new PendingReward { matchId = "m-e", slotId = 1, rank = 2, points = 10, eligible = true, createdUtc = "t" };
            wrong.checksum = wrong.ComputeChecksum();
            StringAssert.Contains("一致しません", MatchRewardJournal.Check(wrong));
            var badId = new PendingReward { matchId = "x", slotId = 1, rank = 1, points = 10, eligible = true };
            badId.checksum = badId.ComputeChecksum();
            Assert.IsNotNull(MatchRewardJournal.Check(badId));
            var notEligible = new PendingReward { matchId = "m-f", slotId = 1, rank = 1, points = 10, eligible = false };
            notEligible.checksum = notEligible.ComputeChecksum();
            Assert.IsNotNull(MatchRewardJournal.Check(notEligible));
            var ok = new PendingReward { matchId = "m-g", slotId = 2, rank = 3, points = 5, eligible = true, createdUtc = "t" };
            ok.checksum = ok.ComputeChecksum();
            Assert.IsNull(MatchRewardJournal.Check(ok));

            // An unreadable journal: nothing applied, nothing overwritten.
            var broken = new MemoryGachaStore { Main = "{broken", Previous = "{broken" };
            var j = new MatchRewardJournal(broken);
            Assert.IsNotNull(j.LoadError);
            Assert.AreEqual(0, j.Recover(gacha));
            Assert.AreEqual("{broken", broken.Main);
        }

        [Test]
        public void GachaFormatUnchanged_Version2_OldDataStillReads()
        {
            var store = new MemoryGachaStore();
            var gacha = Gacha(store);
            gacha.GrantMatchReward("m-h", 1, 5);
            StringAssert.Contains("\"version\": 2", store.Main);
            var catalog = gacha.Catalog;
            Assert.IsNotNull(GachaSave.Parse(store.Main, catalog, out string error, out int version), error);
            Assert.AreEqual(2, version);
            // Stage 34 data without any match reward reads as before.
            var plain = GachaSave.Serialize(new GachaStateData { points = 300, medals = 2 });
            Assert.AreEqual(300, GachaSave.Parse(plain, catalog, out _, out _).points);
        }

        [Test]
        public void ZeroPointsNotWritten_FinishedEntriesBounded()
        {
            var store = new MemoryGachaStore();
            var journal = new MatchRewardJournal(store);
            Assert.IsTrue(journal.RecordPending("m-z", Row(5, 8, 0), out _));
            Assert.IsNull(store.Main, "nothing to record for 0 pt");
            var gacha = Gacha(new MemoryGachaStore());
            for (int i = 0; i < MatchRewardJournal.KeepDone + 10; i++)
            {
                journal.RecordPending("m-" + i, Row(1, 1, 10), out _);
                journal.Recover(gacha);
            }
            Assert.AreEqual(MatchRewardJournal.KeepDone, journal.Entries.Count);
        }
    }
}
