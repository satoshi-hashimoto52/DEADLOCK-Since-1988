using System.Collections;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Network;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// PvPvE check in one Editor process: the host and a host-spawned stand-in player ("dummy") with host-run zombies.
    /// Two real processes / devices are checked by NetworkCheckRunner.
    /// </summary>
    public class PvPvETests : FiringRangeTestBase
    {
        NetworkSession session;
        OnlineSessionUi ui;
        NetworkAvatar dummy;

        PvPvERound Round => PvPvERound.Instance;
        NetworkAvatar Mine => NetworkAvatar.All.Single(a => a.IsLocalPlayerAvatar);
        NetworkZombie[] Zombies => NetworkZombie.All.ToArray();

        IEnumerator HostWithDummy(bool start = true)
        {
            yield return LoadScene(InputMode.Desktop, GameScenes.Online);
            session = NetworkSession.Instance;
            ui = Object.FindAnyObjectByType<OnlineSessionUi>();
            ui.HostButton.onClick.Invoke();
            for (int i = 0; i < 60 && (NetworkAvatar.All.Count == 0 || Round == null); i++)
                yield return null;
            var go = Object.Instantiate(session.Manager.NetworkConfig.PlayerPrefab, new Vector3(0f, 0.05f, 8f), Quaternion.Euler(0f, 180f, 0f));
            dummy = go.GetComponent<NetworkAvatar>();
            dummy.MarkAsDummy();
            go.GetComponent<NetworkObject>().Spawn(true);
            yield return null;
            if (start)
            {
                ui.StartPvPvE();
                yield return null;
                yield return null;
                Assert.AreEqual(RoundPhase.Running, Round.Phase);
            }
        }

        [UnityTearDown]
        public IEnumerator EndSession()
        {
            if (session != null && session.IsActive)
                session.Leave();
            for (int i = 0; i < 5; i++)
                yield return null;
        }

        void MoveDummy(Vector3 position, Posture posture = Posture.Standing)
        {
            var s = dummy.State.Value;
            s.Position = position;
            s.Posture = (byte)posture;
            dummy.State.Value = s;
            dummy.transform.position = position;
        }

        static void Warp(NetworkZombie z, Vector3 position) => z.GetComponent<NavMeshAgent>().Warp(position);

        [UnityTest]
        public IEnumerator Start_NeedsTwoPlayers_Spawns3ZombiesAnd2Boxes_ClosesJoining_NoFreeRespawn()
        {
            yield return LoadScene(InputMode.Desktop, GameScenes.Online);
            session = NetworkSession.Instance;
            ui = Object.FindAnyObjectByType<OnlineSessionUi>();
            ui.HostButton.onClick.Invoke();
            for (int i = 0; i < 60 && Round == null; i++)
                yield return null;
            Assert.IsFalse(Round.ServerStart(out string message), "Host alone: not started");
            StringAssert.Contains("2人", message);
            Assert.AreEqual(0, NetworkZombie.All.Count);

            var go = Object.Instantiate(session.Manager.NetworkConfig.PlayerPrefab, new Vector3(0f, 0.05f, 8f), Quaternion.identity);
            dummy = go.GetComponent<NetworkAvatar>();
            dummy.MarkAsDummy();
            go.GetComponent<NetworkObject>().Spawn(true);
            yield return null;
            ui.StartPvPvE();
            yield return null;
            Assert.AreEqual(RoundPhase.Running, Round.Phase);
            Assert.AreEqual(3, NetworkZombie.All.Count);
            Assert.AreEqual(2, NetworkAmmoBox.All.Count);
            Assert.IsTrue(NetworkZombie.All.All(z => z.Brain.enabled), "AI runs on the host");
            Assert.IsNotNull(NetworkSession.RefusalReason(NetworkAvatar.ProtocolVersion, 1, RoundPhase.Running), "Joining closed during the fight");
            Assert.IsNull(NetworkSession.RefusalReason(NetworkAvatar.ProtocolVersion, 1, RoundPhase.Ended));

            dummy.ServerTakeDamage(1000f, Vector3.zero);
            dummy.ServerRespawn();
            Assert.IsTrue(dummy.Dead.Value, "No free respawn during PvPvE");
        }

        [UnityTest]
        public IEnumerator Zombies_ChaseNearest_KeepTargetWhenSimilar_SwitchWhenTargetDies()
        {
            yield return HostWithDummy();
            var z = Zombies[0];
            // Host far away, the dummy close: the dummy is chased.
            Teleport(new Vector3(8f, 0.05f, -10f));
            MoveDummy(new Vector3(-6f, 0.05f, 9f));
            Warp(z, new Vector3(-8f, 0f, 9f));
            yield return Wait(0.7f);
            Assert.AreEqual(dummy.ZombieTarget, z.Brain.CurrentTarget, "Nearest player");

            // The host comes slightly closer than the dummy (within the switch margin): no flip.
            Vector3 zp = z.transform.position;
            Teleport(zp + new Vector3(1.8f, 0.05f, 0f));
            MoveDummy(zp + new Vector3(-2.3f, 0.05f, 0f));
            yield return Wait(0.7f);
            Assert.AreEqual(dummy.ZombieTarget, z.Brain.CurrentTarget, "Similar distance: keeps its target");

            // The dummy dies: switch to the living host.
            dummy.ServerTakeDamage(1000f, Vector3.zero);
            yield return Wait(0.7f);
            Assert.AreEqual(Mine.ZombieTarget, z.Brain.CurrentTarget, "Target died: the living player");
        }

        [UnityTest]
        public IEnumerator Attack_TargetFixedAtWindup_NoHitOnSomeoneElse_DodgeWorks_MenuDoesNotProtect()
        {
            yield return HostWithDummy();
            var z = Zombies[0];
            foreach (var other in Zombies.Skip(1))
                Warp(other, new Vector3(10f, 0f, -11f)); // out of the way
            Teleport(new Vector3(8f, 0.05f, -12f));
            Vector3 spot = new Vector3(-6f, 0.05f, 6f);
            Warp(z, spot + new Vector3(0f, 0f, 1.1f));
            MoveDummy(spot);
            for (float end = Time.time + 3f; Time.time < end && !z.Brain.InAttackWindup; )
                yield return null;
            Assert.IsTrue(z.Brain.InAttackWindup);
            Assert.AreEqual(dummy.ZombieTarget, z.Brain.AttackTarget);
            // During the windup the dummy steps away and the host steps right in front of the zombie.
            MoveDummy(spot + new Vector3(0f, 0f, -4f));
            Teleport(spot + new Vector3(0.3f, 0f, 0.4f));
            yield return Wait(0.6f);
            Assert.AreEqual(100f, dummy.Health.Value, "Dodged by stepping away during the windup");
            Assert.AreEqual(100f, Mine.Health.Value, "Not hit instead: the attack's target was fixed");

            // Menu open: the match goes on and the host is hit by the next attack.
            var menu = Object.FindAnyObjectByType<GameMenu>();
            MoveDummy(new Vector3(9f, 0.05f, 12f));
            menu.Open();
            yield return Wait(3f);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.Less(Mine.Health.Value, 100f, "Hit while the menu is open");
            menu.Close();
        }

        [UnityTest]
        public IEnumerator Damage_HostDecides_GunAndKnife_DeathOnce_SimultaneousHits()
        {
            yield return HostWithDummy();
            var z = Zombies[0];
            int died = 0;
            z.Died += _ => died++;
            int alive = Round.ZombiesAlive.Value;
            // Two hits in the same frame that are both lethal: one death.
            Assert.AreEqual(DamageResult.Damaged, z.Brain.ApplyDamage(90f, z.transform.position, Vector3.forward));
            var first = z.Brain.ApplyDamage(50f, z.transform.position, Vector3.forward);
            var second = z.Brain.ApplyDamage(50f, z.transform.position, Vector3.forward);
            Assert.AreEqual(DamageResult.Destroyed, first);
            Assert.AreEqual(DamageResult.Ignored, second);
            yield return null;
            Assert.AreEqual(1, died);
            Assert.AreEqual(alive - 1, Round.ZombiesAlive.Value);
            Assert.AreEqual(0f, z.Health.Value);
            Assert.IsTrue(z.IsDead);

            // Real shot from the host's gun (through the host's projectile system).
            var z2 = Zombies[1];
            foreach (var other in Zombies.Skip(2))
                Warp(other, new Vector3(10f, 0f, -11f));
            z2.Brain.Active = false; // stand still for the check
            Warp(z2, new Vector3(4f, 0f, 3f));
            Teleport(new Vector3(4f, 0.05f, -1f));
            yield return Wait(0.2f);
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Vector3 d = z2.transform.position + Vector3.up * 1.0f - Camera.main.transform.position;
            motor.SetYawPitch(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
            yield return Tap(mouse.leftButton);
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Assert.AreEqual(75f, z2.Health.Value, "Gun: 25 decided by the host");

            // Knife.
            Teleport(z2.transform.position + new Vector3(0f, 0.05f, -1.2f));
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            d = z2.transform.position + Vector3.up * 1.0f - Camera.main.transform.position;
            motor.SetYawPitch(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(25f, z2.Health.Value, "Knife: 50 decided by the host");

            // Players can still hurt each other.
            dummy.ServerTakeDamage(25f, Vector3.zero);
            Assert.AreEqual(75f, dummy.Health.Value);
        }

        [UnityTest]
        public IEnumerator AmmoBox_OneTakerWhenTouchedTogether_RestoredOnRestart()
        {
            yield return HostWithDummy();
            foreach (var z in Zombies)
                z.Brain.Active = false;
            // Both spend some reserve so both could take the box.
            Mine.ServerWeapons.Ammo.Set(10, 20);
            dummy.ServerWeapons.Ammo.Set(10, 20);
            var box = NetworkAmmoBox.All[0];
            Teleport(box.transform.position + new Vector3(0.2f, 0.05f, 0f));
            MoveDummy(box.transform.position + new Vector3(-0.2f, 0.05f, 0f));
            yield return Wait(0.2f);
            Assert.IsTrue(box.Taken.Value);
            int total = Mine.ServerWeapons.Ammo.Reserve + dummy.ServerWeapons.Ammo.Reserve;
            Assert.AreEqual(40 + box.Amount, total, "Exactly one player got the rounds");
            Assert.AreEqual(Mine.ServerWeapons.Ammo.Reserve, Mine.Reserve.Value, "Synced to its owner");
            yield return Wait(0.3f);
            Assert.AreEqual(40 + box.Amount, Mine.ServerWeapons.Ammo.Reserve + dummy.ServerWeapons.Ammo.Reserve, "No second pickup later");

            ui.StartPvPvE(); // restart
            yield return null;
            Assert.AreEqual(2, NetworkAmmoBox.All.Count, "No duplicate boxes");
            Assert.IsTrue(NetworkAmmoBox.All.All(b => !b.Taken.Value), "Boxes back");
            Assert.AreEqual(gun.Definition.startReserve, Mine.ServerWeapons.Ammo.Reserve, "Ammo reset");
        }

        [UnityTest]
        public IEnumerator End_ClearedOnce_NoDamageAfter_AllDeadWinsSameFrame_RestartResetsWithoutDuplicates()
        {
            yield return HostWithDummy();
            int finished = 0;
            Round.Finished += _ => finished++;
            foreach (var z in Zombies)
                z.Brain.ApplyDamage(1000f, z.transform.position, Vector3.forward);
            yield return null;
            yield return null;
            Assert.AreEqual(RoundPhase.Ended, Round.Phase);
            Assert.AreEqual(RoundResult.Cleared, Round.Result);
            Assert.AreEqual(1, finished, "Decided once");
            yield return Wait(0.3f);
            Assert.AreEqual(1, Round.ServerResultsDecided);
            StringAssert.Contains("ゾンビ残り 0 / 3", Round.SummaryText);
            StringAssert.Contains("ホスト：生存", Round.SummaryText);
            Assert.AreEqual(DamageResult.Ignored, Mine.ServerTakeDamage(10f, Vector3.zero), "No damage after the round");
            Assert.IsTrue(ui.PvPvEPanel.activeSelf, "Result shown");

            // Restart: everything fresh, no duplicates.
            int round = Round.RoundNumber.Value;
            ui.StartPvPvE();
            yield return null;
            Assert.AreEqual(RoundPhase.Running, Round.Phase);
            Assert.AreEqual(round + 1, Round.RoundNumber.Value);
            Assert.AreEqual(3, NetworkZombie.All.Count, "Old zombies removed, 3 new");
            Assert.IsTrue(NetworkZombie.All.All(z => !z.Brain.IsDead));
            Assert.AreEqual(100f, Mine.Health.Value);
            Assert.AreEqual(1, NetworkAvatar.LocalBindings);

            // Same frame: last zombies and every player die -> "all dead" wins.
            foreach (var z in Zombies)
                z.Brain.ApplyDamage(1000f, z.transform.position, Vector3.forward);
            Mine.ServerTakeDamage(1000f, Vector3.zero);
            dummy.ServerTakeDamage(1000f, Vector3.zero);
            yield return null;
            yield return null;
            Assert.AreEqual(RoundResult.AllDead, Round.Result);
            Assert.AreEqual(2, finished);
            Assert.IsFalse(ui.RespawnButton.gameObject.activeSelf, "No free respawn button in PvPvE");

            // Back to the normal online check: zombies and boxes removed.
            ui.StopPvPvE();
            yield return null;
            Assert.AreEqual(RoundPhase.Off, Round.Phase);
            Assert.AreEqual(0, NetworkZombie.All.Count);
            Assert.AreEqual(0, NetworkAmmoBox.All.Count);
        }

        [UnityTest]
        public IEnumerator LeavingClearsEverything_RehostStartsClean()
        {
            yield return HostWithDummy();
            session.Leave();
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.AreEqual(0, NetworkZombie.All.Count);
            Assert.AreEqual(0, NetworkAmmoBox.All.Count);
            Assert.IsNull(PvPvERound.Instance);
            Assert.IsTrue(ui.Lobby.activeSelf);
            Assert.IsFalse(player.GetComponent<PlayerHealth>().IsDead);
        }
    }
}
