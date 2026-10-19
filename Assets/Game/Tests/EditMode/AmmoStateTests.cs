using Deadlock.Combat;
using NUnit.Framework;

namespace Deadlock.Tests
{
    public class AmmoStateTests
    {
        [Test]
        public void TryConsume_StopsAtZero_NeverNegative()
        {
            var ammo = new AmmoState(magazineSize: 3, reserveMax: 10, startLoaded: 3, startReserve: 0);

            Assert.IsTrue(ammo.TryConsume());
            Assert.IsTrue(ammo.TryConsume());
            Assert.IsTrue(ammo.TryConsume());
            Assert.IsFalse(ammo.TryConsume());
            Assert.IsFalse(ammo.TryConsume());
            Assert.AreEqual(0, ammo.Loaded);
            Assert.AreEqual(0, ammo.Reserve);
        }

        [Test]
        public void Reload_TopsUpWithoutDiscardingLoadedRounds()
        {
            var ammo = new AmmoState(30, 180, startLoaded: 30, startReserve: 90);
            for (int i = 0; i < 20; i++) ammo.TryConsume();

            int moved = ammo.Reload();

            Assert.AreEqual(20, moved);
            Assert.AreEqual(30, ammo.Loaded);
            Assert.AreEqual(70, ammo.Reserve);
        }

        [Test]
        public void Reload_WithSmallReserve_LoadsOnlyWhatIsLeft()
        {
            var ammo = new AmmoState(30, 180, startLoaded: 10, startReserve: 5);

            ammo.Reload();

            Assert.AreEqual(15, ammo.Loaded);
            Assert.AreEqual(0, ammo.Reserve);
            Assert.IsFalse(ammo.CanReload);
        }

        [Test]
        public void Reload_NeverCreatesRounds()
        {
            var ammo = new AmmoState(30, 180, startLoaded: 7, startReserve: 40);
            int total = ammo.Loaded + ammo.Reserve;

            ammo.Reload();
            ammo.Reload(); // full magazine: no-op
            Assert.AreEqual(total, ammo.Loaded + ammo.Reserve);
            Assert.AreEqual(0, ammo.Reload());
            Assert.AreEqual(total, ammo.Loaded + ammo.Reserve);
        }

        [Test]
        public void AddReserve_ClampsToMax_AndIgnoresNonPositive()
        {
            var ammo = new AmmoState(30, reserveMax: 100, startLoaded: 30, startReserve: 80);

            Assert.AreEqual(20, ammo.AddReserve(50));
            Assert.AreEqual(100, ammo.Reserve);
            Assert.AreEqual(30, ammo.Loaded, "Pickups must not refill the magazine directly.");
            Assert.AreEqual(0, ammo.AddReserve(10));
            Assert.AreEqual(0, ammo.AddReserve(-5));
            Assert.AreEqual(100, ammo.Reserve);
        }

        [Test]
        public void Constructor_ClampsStartValues()
        {
            var ammo = new AmmoState(30, 60, startLoaded: 99, startReserve: -4);

            Assert.AreEqual(30, ammo.Loaded);
            Assert.AreEqual(0, ammo.Reserve);
        }
    }
}
