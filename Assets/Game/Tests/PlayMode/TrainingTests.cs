using System.Collections;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>Training round: zombies, player HP, clear / fail, retry, menu pause.</summary>
    public class TrainingTests : FiringRangeTestBase
    {
        TrainingSession session;
        PlayerHealth health;
        Zombie[] zombies;
        GameMenu menu;

        IEnumerator LoadTraining()
        {
            yield return LoadScene(InputMode.Desktop, GameScenes.Training);
            session = Object.FindAnyObjectByType<TrainingSession>();
            health = player.GetComponent<PlayerHealth>();
            zombies = Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).OrderBy(z => z.name).ToArray();
            menu = Object.FindAnyObjectByType<GameMenu>();
        }

        Zombie Nearest(Vector3 position) =>
            zombies.Where(z => !z.IsDead).OrderBy(z => Vector3.Distance(z.transform.position, position)).First();

        /// <summary>
        /// Fires real projectiles from the eye at a zombie's body until it dies. The player first stands 4 m in front
        /// of the zombie (toward the arena centre) so cover does not block the line of fire.
        /// </summary>
        IEnumerator ShootDead(Zombie zombie)
        {
            var gunDef = gun.Definition;
            Vector3 toCentre = -zombie.transform.position;
            toCentre.y = 0f;
            Vector3 stand = zombie.transform.position + toCentre.normalized * 4f;
            Teleport(new Vector3(stand.x, 0.05f, stand.z), Quaternion.LookRotation(-toCentre).eulerAngles.y);
            yield return null;
            for (int i = 0; i < 20 && !zombie.IsDead; i++)
            {
                Vector3 eye = Camera.main.transform.position;
                Vector3 target = zombie.transform.position + Vector3.up * 1.0f;
                projectiles.Fire(eye, target - eye, gunDef.projectileSpeed, gunDef.maxRange, gunDef.damage, player);
                yield return Wait(0.15f);
            }
        }

        [UnityTest]
        public IEnumerator Zombies_Chase_AttackOncePerSwing_KillPlayer_Fails()
        {
            yield return LoadTraining();
            Assert.AreEqual(3, zombies.Length);
            Assert.AreEqual(TrainingResult.Playing, session.Result);
            Assert.AreEqual(100f, health.Health);

            // Chase: every zombie gets closer to the player.
            float[] before = zombies.Select(z => Vector3.Distance(z.transform.position, player.transform.position)).ToArray();
            yield return Wait(1.5f);
            for (int i = 0; i < zombies.Length; i++)
                Assert.Less(Vector3.Distance(zombies[i].transform.position, player.transform.position), before[i] - 1f, $"{zombies[i].name} chases");

            // Stand right next to one: it attacks; each landed attack costs exactly its damage.
            var z0 = Nearest(new Vector3(0f, 0f, 12f));
            Teleport(z0.transform.position + new Vector3(0f, 0.05f, -1.1f));
            float hp = health.Health;
            int hits = 0;
            health.Damaged += (amount, from) => { hits++; Assert.AreEqual(10f, amount, 0.01f); };
            bool sawAttack = false;
            for (float end = Time.time + 2.5f; Time.time < end && hits == 0; )
            {
                sawAttack |= z0.State == ZombieState.Attack;
                yield return null;
            }
            Assert.IsTrue(sawAttack, "The zombie attacks when in reach");
            Assert.AreEqual(1, hits, "One hit per attack");
            Assert.AreEqual(hp - 10f, health.Health, 0.01f);

            // Keep standing there until the player dies.
            for (float end = Time.time + 20f; Time.time < end && !health.IsDead; )
                yield return null;
            yield return null; // the result is resolved once per frame, after all deaths of that frame
            Assert.IsTrue(health.IsDead);
            Assert.AreEqual(0f, health.Health);
            Assert.AreEqual(TrainingResult.Failed, session.Result);
            Assert.IsTrue(router.IsBlocked, "Dead player has no input");
            Assert.IsTrue(Object.FindAnyObjectByType<TrainingHud>().ResultPanel.activeSelf);

            // Zombies stop attacking a dead player.
            yield return Wait(0.5f);
            Assert.IsTrue(zombies.All(z => z.State == ZombieState.Idle || z.IsDead));
            Assert.AreEqual(0f, health.Health);
        }

        [UnityTest]
        public IEnumerator GunAndKnife_KillAllZombies_Clears()
        {
            yield return LoadTraining();

            // Gun: real projectiles.
            var first = zombies[1];
            yield return ShootDead(first);
            Assert.IsTrue(first.IsDead, "Rifle rounds kill a zombie");
            Assert.IsFalse(first.GetComponent<Collider>().enabled, "Dead zombies stop taking hits");
            Assert.AreEqual(2, session.Remaining);

            // Knife: step up to the next zombie and swing twice (50 + 50).
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            var second = Nearest(player.transform.position);
            Vector3 toPlayer = (player.transform.position - second.transform.position);
            toPlayer.y = 0f;
            Vector3 stand = second.transform.position + toPlayer.normalized * 1.2f;
            Teleport(new Vector3(stand.x, 0.05f, stand.z), Quaternion.LookRotation(-toPlayer).eulerAngles.y);
            for (int i = 0; i < 3 && !second.IsDead; i++)
            {
                // Keep facing it (it may have shuffled).
                Vector3 d = second.transform.position - player.transform.position;
                motor.SetYawPitch(Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y, 0f);
                yield return Tap(mouse.leftButton);
                yield return Wait(melee.Definition.TotalTime + 0.05f);
            }
            Assert.IsTrue(second.IsDead, "Two knife hits kill a zombie");

            // Last one with the gun again.
            yield return Tap(keyboard.digit1Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            var third = Nearest(player.transform.position);
            yield return ShootDead(third);
            Assert.IsTrue(zombies.All(z => z.IsDead));
            Assert.AreEqual(TrainingResult.Cleared, session.Result);
            Assert.IsTrue(router.IsBlocked, "Result screen stops input");
            Assert.IsTrue(Object.FindAnyObjectByType<TrainingHud>().ResultPanel.activeSelf);
            Assert.IsFalse(health.IsDead);
        }

        [UnityTest]
        public IEnumerator Retry_ResetsZombiesPlayerAndTransientStates()
        {
            yield return LoadTraining();
            yield return ShootDead(zombies[0]);
            float hpBefore = health.Health;

            // Use ammo with the real trigger and take an ammo box.
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.35f);
            Release(mouse.leftButton, queueEventOnly: true);
            Assert.Less(gun.Ammo.Loaded, gun.Definition.magazineSize);
            var box = GameObject.Find("AmmoPickup_A").GetComponent<AmmoPickup>();
            Teleport(box.transform.position + Vector3.up * 0.05f);
            yield return Wait(0.2f);
            Assert.IsTrue(box.IsConsumed);

            // Leave the layout editor open (menu → settings → layout) and retry from scratch.
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(menu.IsEditingLayout);
            Assert.AreEqual(0f, Time.timeScale);
            session.Retry();
            yield return null;
            yield return null;
            yield return LoadTraining();

            Assert.AreEqual(1f, Time.timeScale, "No paused clock after retry");
            Assert.AreEqual(TrainingResult.Playing, session.Result);
            Assert.AreEqual(3, session.Remaining);
            Assert.IsTrue(zombies.All(z => !z.IsDead && z.Health == z.MaxHealth));
            Assert.AreEqual(health.MaxHealth, health.Health);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(menu.IsEditingLayout);
            Assert.IsFalse(router.IsBlocked);
            Assert.AreEqual(Posture.Standing, posture.Current);
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded);
            Assert.AreEqual(gun.Definition.startReserve, gun.Ammo.Reserve);
            Assert.IsTrue(Object.FindObjectsByType<AmmoPickup>(FindObjectsSortMode.None).All(p => !p.IsConsumed), "Ammo boxes are back");
            Assert.AreEqual(0, session.Defeated);
            Assert.AreEqual(default(PlayerInputFrame).FireHeld, router.Current.FireHeld);
            Assert.AreEqual(Vector2.zero, router.Current.Move);
            Assert.GreaterOrEqual(health.Health, hpBefore);
        }

        [UnityTest]
        public IEnumerator Menu_PausesOfflineTraining_AndBlocksInput()
        {
            yield return LoadTraining();
            yield return Wait(0.3f);
            menu.MenuButton.onClick.Invoke();
            yield return null;
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(router.IsBlocked);

            Vector3 zombieAt = zombies[1].transform.position;
            Vector3 playerAt = player.transform.position;
            int loaded = gun.Ammo.Loaded;
            Press(keyboard.wKey, queueEventOnly: true);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return WaitRealtime(0.5f);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(zombieAt, zombies[1].transform.position, "Zombies are frozen while the menu is open");
            Assert.AreEqual(playerAt.x, player.transform.position.x, 0.001f);
            Assert.AreEqual(playerAt.z, player.transform.position.z, 0.001f);
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "No shot while the menu is open");

            // Tab closes it again.
            yield return Tap(keyboard.tabKey);
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(router.IsBlocked);
            yield return Wait(0.5f);
            Assert.AreNotEqual(zombieAt, zombies[1].transform.position, "Zombies move again after resuming");
        }

        [UnityTest]
        public IEnumerator SimultaneousPlayerAndLastZombieDeath_FailsOnce()
        {
            yield return LoadTraining();
            Assert.AreEqual(1.5f, zombies[0].AttackInterval, 0.001f);

            int finished = 0;
            session.Finished += _ => finished++;
            zombies[1].ApplyDamage(zombies[1].Health, player.transform.position, Vector3.forward);
            zombies[2].ApplyDamage(zombies[2].Health, player.transform.position, Vector3.forward);
            zombies[0].ApplyDamage(zombies[0].Health, player.transform.position, Vector3.forward);
            health.TakeDamage(health.Health, zombies[0].transform.position);
            yield return null;

            Assert.AreEqual(TrainingResult.Failed, session.Result, "Player death takes precedence for a simultaneous terminal frame");
            Assert.AreEqual(1, finished, "The terminal result is emitted once");
            Assert.IsTrue(Object.FindAnyObjectByType<TrainingHud>().ResultPanel.activeSelf);
        }

        /// <summary>Waits until <paramref name="zombie"/> starts an attack (player standing in reach).</summary>
        IEnumerator WaitForWindup(Zombie zombie)
        {
            Teleport(zombie.transform.position + new Vector3(0f, 0.05f, -1.1f));
            for (float end = Time.time + 3f; Time.time < end && !zombie.InAttackWindup; )
                yield return null;
            Assert.IsTrue(zombie.InAttackWindup, "Attack started");
        }

        [UnityTest]
        public IEnumerator Attack_DodgedByLeavingDuringWindup_AndIntervalFromAttackStart()
        {
            yield return LoadTraining();
            // Only one zombie in play.
            zombies[0].ApplyDamage(1000f, Vector3.zero, Vector3.forward);
            zombies[2].ApplyDamage(1000f, Vector3.zero, Vector3.forward);
            var z = zombies[1];
            int hits = 0;
            health.Damaged += (a, f) => hits++;

            yield return WaitForWindup(z);
            float firstStart = Time.time;
            // Step out of reach during the telegraph: the hit-time distance check misses.
            Teleport(z.transform.position + new Vector3(0f, 0.05f, -3.5f));
            yield return Wait(0.5f);
            Assert.AreEqual(0, hits, "Leaving during the windup avoids the hit");

            // Come back right away: the next attack does not start before 1.5 s after the previous start.
            Teleport(z.transform.position + new Vector3(0f, 0.05f, -1.1f));
            float secondStart = -1f;
            for (float end = Time.time + 3f; Time.time < end && secondStart < 0f; )
            {
                yield return null;
                if (z.InAttackWindup)
                    secondStart = Time.time;
            }
            Assert.Greater(secondStart, 0f);
            Assert.GreaterOrEqual(secondStart - firstStart, z.AttackInterval - 0.02f, "Interval runs from attack start to attack start");
            Assert.Less(secondStart - firstStart, z.AttackInterval + 0.2f);
            for (float end = Time.time + 1f; Time.time < end && hits == 0; )
                yield return null;
            Assert.AreEqual(1, hits, "Staying in reach is hit once");
        }

        [UnityTest]
        public IEnumerator Attack_CoverAtHitTime_Blocks_DeadZombieDoesNotHit()
        {
            yield return LoadTraining();
            zombies[0].ApplyDamage(1000f, Vector3.zero, Vector3.forward);
            zombies[2].ApplyDamage(1000f, Vector3.zero, Vector3.forward);
            var z = zombies[1];
            int hits = 0;
            health.Damaged += (a, f) => hits++;

            // A wall appears between them during the windup: the hit-time line-of-sight check blocks it.
            yield return WaitForWindup(z);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = (z.transform.position + player.transform.position) * 0.5f + Vector3.up * 1.5f;
            wall.transform.localScale = new Vector3(3f, 3f, 0.1f);
            Physics.SyncTransforms();
            yield return Wait(0.75f); // past the hit window
            Assert.AreEqual(0, hits, "Cover at the moment of the hit blocks it");
            Object.Destroy(wall);
            yield return null;

            // Killed during its windup: no hit afterwards.
            yield return WaitForWindup(z);
            z.ApplyDamage(1000f, z.transform.position, Vector3.forward);
            yield return Wait(0.8f);
            Assert.AreEqual(0, hits, "A dead zombie does not land its attack");
            Assert.AreEqual(TrainingResult.Cleared, session.Result);
        }

        [UnityTest]
        public IEnumerator Menu_HeldKeysAndButtons_DoNotCarryOverAfterClosing()
        {
            yield return LoadTraining();
            yield return Wait(0.2f);
            Vector3 at = player.transform.position;
            int fired = 0;
            gun.Fired += () => fired++;

            yield return Tap(keyboard.tabKey);
            Assert.IsTrue(menu.IsOpen);
            Press(keyboard.wKey, queueEventOnly: true);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return WaitRealtime(0.1f);
            yield return Tap(keyboard.tabKey); // close while still holding W and the trigger
            Assert.IsFalse(menu.IsOpen);
            yield return Wait(0.4f);
            Assert.AreEqual(at.z, player.transform.position.z, 0.02f, "Held W from the menu does not move");
            Assert.AreEqual(0, fired, "Held trigger from the menu does not fire");

            // Released and pressed again: works normally.
            Release(keyboard.wKey, queueEventOnly: true);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Press(keyboard.wKey, queueEventOnly: true);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(mouse.leftButton, queueEventOnly: true);
            Assert.Greater(player.transform.position.z, at.z + 0.3f);
            Assert.Greater(fired, 0);
        }

        [UnityTest]
        public IEnumerator DamageFlash_IsShortAndFaint_RapidHitsDoNotRestrobe()
        {
            yield return LoadTraining();
            var hud = Object.FindAnyObjectByType<HudController>();
            health.TakeDamage(1f, Vector3.zero);
            yield return null;
            float first = hud.DamageFlashLevel;
            Assert.Greater(first, 0f);
            Assert.LessOrEqual(first, 0.2f, "Faint");
            yield return Wait(0.08f);
            float before = hud.DamageFlashLevel;
            health.TakeDamage(1f, Vector3.zero);
            yield return null;
            Assert.LessOrEqual(hud.DamageFlashLevel, before + 0.001f, "A quick second hit does not brighten it again");
            yield return Wait(0.25f);
            Assert.AreEqual(0f, hud.DamageFlashLevel, 0.001f, "Short");
        }
    }
}
