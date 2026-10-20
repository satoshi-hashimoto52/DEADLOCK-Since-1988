using Deadlock.Combat;
using NUnit.Framework;

namespace Deadlock.Tests
{
    /// <summary>Host-side checks of player requests in the online prototype (no network needed).</summary>
    public class AuthoritativeWeaponStateTests
    {
        const float Interval = 0.1f, ReloadTime = 1.8f, SwitchTime = 0.35f, SwingTime = 0.52f;

        static AuthoritativeWeaponState Create(int reserve = 90) =>
            new AuthoritativeWeaponState(30, 180, reserve, Interval, ReloadTime, SwitchTime, SwingTime);

        [Test]
        public void Fire_ConsumesOneRound_AndRespectsInterval_NoBurstAfterIdle()
        {
            var w = Create();
            Assert.AreEqual(ShotRejection.None, w.TryFire(10f));
            Assert.AreEqual(29, w.Ammo.Loaded);
            // A second request at the same moment (e.g. several sent in one frame) is refused, even after idling.
            Assert.AreEqual(ShotRejection.TooFast, w.TryFire(10f));
            Assert.AreEqual(ShotRejection.TooFast, w.TryFire(10.04f));
            // Slight network jitter (arriving a little early) is tolerated.
            Assert.AreEqual(ShotRejection.None, w.TryFire(10.06f));
            Assert.AreEqual(28, w.Ammo.Loaded);
        }

        [Test]
        public void Fire_SustainedRateNeverExceedsFireRate()
        {
            var w = Create(reserve: 0);
            int accepted = 0;
            // Requests every 10 ms for 1 s: at most 1 s / 0.1 s + 1 shots.
            for (int i = 0; i <= 100; i++)
                if (w.TryFire(i * 0.01f) == ShotRejection.None)
                    accepted++;
            Assert.LessOrEqual(accepted, 11);
            Assert.GreaterOrEqual(accepted, 10);
        }

        [Test]
        public void Fire_RefusedWhenDead_Reloading_EmptyOrKnifeOut()
        {
            var w = Create(reserve: 0);
            w.IsDead = true;
            Assert.AreEqual(ShotRejection.Dead, w.TryFire(1f));
            w.IsDead = false;

            Assert.AreEqual(ShotRejection.None, w.TryFire(1f));
            var r = Create();
            Assert.AreEqual(ShotRejection.None, r.TryFire(1f));
            Assert.IsTrue(r.TryStartReload(1.2f));
            Assert.AreEqual(ShotRejection.Reloading, r.TryFire(1.5f));
            Assert.AreEqual(ShotRejection.None, r.TryFire(1.2f + ReloadTime + 0.01f), "Reload finished on the host clock");
            Assert.AreEqual(29, r.Ammo.Loaded);

            var empty = new AuthoritativeWeaponState(1, 0, 0, Interval, ReloadTime, SwitchTime, SwingTime);
            Assert.AreEqual(ShotRejection.None, empty.TryFire(1f));
            Assert.AreEqual(ShotRejection.NoAmmo, empty.TryFire(2f));

            var k = Create();
            Assert.IsTrue(k.TrySwitch(WeaponSlot.Melee, 1f));
            Assert.AreEqual(ShotRejection.NotEquipped, k.TryFire(1.1f), "During the switch");
            Assert.AreEqual(ShotRejection.NotEquipped, k.TryFire(2f), "Knife out");
        }

        [Test]
        public void SwitchingAwayCancelsReload_WithoutAddingRounds()
        {
            var w = Create();
            for (int i = 0; i < 5; i++)
                w.TryFire(i);
            Assert.AreEqual(25, w.Ammo.Loaded);
            Assert.IsTrue(w.TryStartReload(10f));
            Assert.IsTrue(w.TrySwitch(WeaponSlot.Melee, 10.5f));
            Assert.IsTrue(w.TrySwitch(WeaponSlot.Gun, 11f));
            w.Tick(20f);
            Assert.IsFalse(w.IsReloading);
            Assert.AreEqual(25, w.Ammo.Loaded);
            Assert.AreEqual(90, w.Ammo.Reserve);
        }

        [Test]
        public void Swing_OnlyWithKnifeOut_OncePerSwingTime_NotWhenDead()
        {
            var w = Create();
            Assert.IsFalse(w.TrySwing(1f), "Gun out");
            Assert.IsTrue(w.TrySwitch(WeaponSlot.Melee, 1f));
            Assert.IsFalse(w.TrySwing(1.2f), "Still switching");
            Assert.IsTrue(w.TrySwing(2f));
            Assert.IsFalse(w.TrySwing(2.1f), "Mashing does not shorten the swing");
            Assert.IsTrue(w.TrySwing(2f + SwingTime));
            w.IsDead = true;
            Assert.IsFalse(w.TrySwing(5f));
        }

        [Test]
        public void Reset_GivesAFreshGun()
        {
            var w = Create();
            w.TryFire(1f);
            w.TrySwitch(WeaponSlot.Melee, 2f);
            w.IsDead = true;
            w.Reset();
            Assert.IsFalse(w.IsDead);
            Assert.AreEqual(WeaponSlot.Gun, w.Slot);
            Assert.AreEqual(30, w.Ammo.Loaded);
            Assert.AreEqual(90, w.Ammo.Reserve);
            Assert.AreEqual(ShotRejection.None, w.TryFire(3f));
        }
    }
}
