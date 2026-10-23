using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Ruins test field (prototype): a closed two-storey building about twice the firing range's walkable area; both
    /// stair tunnels can be walked up and down in every posture without leaving the structure; the gallery parapet stops
    /// falls; floors and walls stop shots and zombie attacks; zombies follow the player up and down the stairs; the start
    /// is safe; defeating every zombie does not end the round (the escape point does, RuinsEscapeTests); retry starts
    /// over; the menu leads in and out.
    /// </summary>
    public class RuinsTests : FiringRangeTestBase
    {
        const string Ruins = "Proto_Ruins";
        const float Upper = 5f;
        static readonly int Solid = 1; // Default layer: the building (player, zombies, pickups are on other layers)
        // Plan (RuinsSceneBuilder): outer wall faces, stair tunnels (interior x 17.6..21.1, ramps from z -7 / +7).
        const float HalfX = 21.1f, HalfZ = 17.3f, TunnelInner = 17.6f, RampStart = 7f, CorridorZ = 15.05f;

        Zombie[] zombies;

        IEnumerator LoadRuins(bool zombiesActive)
        {
            yield return LoadScene(InputMode.Desktop, Ruins);
            zombies = Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).OrderBy(z => z.name).ToArray();
            foreach (var z in zombies)
                z.Active = zombiesActive;
            yield return null;
        }

        IEnumerator Hold(float seconds, params UnityEngine.InputSystem.Controls.ButtonControl[] keys)
        {
            foreach (var k in keys)
                Press(k, queueEventOnly: true);
            yield return Wait(seconds);
            foreach (var k in keys)
                Release(k, queueEventOnly: true);
            yield return null;
        }

        static void Park(Zombie z, Vector3 position, bool stopped)
        {
            var agent = z.GetComponent<NavMeshAgent>();
            agent.Warp(position);
            if (stopped)
                agent.isStopped = true;
        }

        /// <summary>The standing capsule is inside the building's open space (not in a wall, floor or ceiling).</summary>
        void AssertNotInsideSolid(string why)
        {
            Vector3 p = player.transform.position;
            float h = player.GetComponent<CharacterController>().height;
            bool overlap = Physics.CheckCapsule(p + Vector3.up * 0.32f, p + Vector3.up * Mathf.Max(0.33f, h - 0.32f), 0.28f, Solid, QueryTriggerInteraction.Ignore);
            Assert.IsFalse(overlap, $"{why}: player capsule inside geometry at {p}");
            Assert.That(p.x, Is.InRange(-HalfX, HalfX), why);
            Assert.That(p.z, Is.InRange(-HalfZ, HalfZ), why);
            Assert.That(p.y, Is.InRange(-0.1f, Upper + 0.6f), why);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Walkable cells (0.5 m grid) where a standing player fits on a floor at height y.</summary>
        static float WalkableArea(float x0, float x1, float z0, float z1, float y, out int openToSky)
        {
            float area = 0f;
            openToSky = 0;
            for (float x = x0 + 0.25f; x < x1; x += 0.5f)
                for (float z = z0 + 0.25f; z < z1; z += 0.5f)
                {
                    var from = new Vector3(x, y + 1.2f, z);
                    if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 1.5f, Solid, QueryTriggerInteraction.Ignore))
                        continue;
                    if (Mathf.Abs(hit.point.y - y) > 0.06f || hit.normal.y < 0.9f)
                        continue;
                    Vector3 f = hit.point;
                    if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.45f, 0.35f, Solid, QueryTriggerInteraction.Ignore))
                        continue;
                    area += 0.25f;
                    if (!Physics.Raycast(f + Vector3.up * 1f, Vector3.up, 30f, Solid, QueryTriggerInteraction.Ignore))
                        openToSky++;
                }
            return area;
        }

        /// <summary>Walkable cells on a stair ramp (x0..x1, rising from bottomZ toward dir), slope area.</summary>
        static float RampArea(float x0, float x1, float bottomZ, float dir)
        {
            float run = Upper / Mathf.Tan(30f * Mathf.Deg2Rad), area = 0f;
            for (float x = x0 + 0.25f; x < x1; x += 0.5f)
                for (float d = 0.25f; d < run; d += 0.5f)
                {
                    float z = bottomZ + dir * d, y = d * Mathf.Tan(30f * Mathf.Deg2Rad);
                    if (!Physics.Raycast(new Vector3(x, y + 1.2f, z), Vector3.down, out RaycastHit hit, 1.6f, Solid, QueryTriggerInteraction.Ignore))
                        continue;
                    Vector3 f = hit.point;
                    if (!Physics.CheckCapsule(f + Vector3.up * 0.45f, f + Vector3.up * 1.45f, 0.35f, Solid, QueryTriggerInteraction.Ignore))
                        area += 0.25f / Mathf.Cos(30f * Mathf.Deg2Rad);
                }
            return area;
        }

        [UnityTest]
        public IEnumerator Layout_Closed_TwoFloors_WalkableAboutTwiceTheFiringRange()
        {
            yield return LoadRuins(false);
            float ground = WalkableArea(-HalfX, HalfX, -HalfZ, HalfZ, 0f, out int skyGround);
            float upper = WalkableArea(-HalfX, HalfX, -HalfZ, HalfZ, Upper, out int skyUpper);
            float stairs = RampArea(TunnelInner, HalfX, -RampStart, 1f) + RampArea(-HalfX, -TunnelInner, RampStart, -1f);
            Assert.AreEqual(0, skyGround + skyUpper, "every floor cell is under the roof (no sky, the atrium included)");
            Assert.Greater(upper, 700f, "an upper floor with a gallery around the atrium");
            Assert.Greater(stairs, 45f, "two walkable stairs (about 2.5 m of usable width each, 10 m long)");

            yield return LoadScene(InputMode.Desktop, SceneName);
            float range = WalkableArea(-8f, 8f, -6f, 54f, 0f, out _);
            float total = ground + upper + stairs;
            float ratio = total / range;
            Debug.Log($"[DEADLOCK][ruins] walkable (standing player, 0.5 m grid): ground {ground:0} m2, upper {upper:0} m2, stairs {stairs:0} m2, total {total:0} m2; firing range {range:0} m2; ratio {ratio:0.00}");
            Assert.That(ratio, Is.InRange(2.0f, 2.8f), "about twice the firing range or a little more (passages widened after the device check)");
        }

        [UnityTest]
        public IEnumerator Stairs_BothTunnels_UpAndDown_StandingCrouchingProne_JumpUnderTheCeiling()
        {
            yield return LoadRuins(false);
            foreach (var (name, bottom, yaw, rampZ) in new[] { ("east", new Vector3(19.35f, 0.05f, -8.6f), 0f, -RampStart), ("west", new Vector3(-19.35f, 0.05f, 8.6f), 180f, RampStart) })
            {
                // Standing: up to the upper floor and on through the exit, then back down.
                Teleport(bottom, yaw);
                yield return Wait(0.2f);
                Press(keyboard.wKey, queueEventOnly: true);
                float end = Time.time + 6f;
                while (Time.time < end && player.transform.position.y < Upper - 0.05f)
                {
                    AssertNotInsideSolid($"{name} stair, walking up");
                    yield return null;
                }
                yield return Wait(0.6f);
                Release(keyboard.wKey, queueEventOnly: true);
                yield return Wait(0.2f);
                Assert.AreEqual(Upper, player.transform.position.y, 0.08f, $"{name}: walked up to the upper floor");
                Assert.IsTrue(motor.IsGrounded, $"{name}: standing on the upper floor");

                Teleport(player.transform.position, yaw + 180f);
                yield return Wait(0.1f);
                Press(keyboard.wKey, queueEventOnly: true);
                end = Time.time + 6f;
                while (Time.time < end && player.transform.position.y > 0.05f)
                {
                    AssertNotInsideSolid($"{name} stair, walking down");
                    yield return null;
                }
                Release(keyboard.wKey, queueEventOnly: true);
                yield return Wait(0.3f);
                Assert.Less(player.transform.position.y, 0.1f, $"{name}: walked back down to the ground floor");

                // Crouched: the whole way up (a ramp, not steps: the crouched step height is 0.1 m).
                Teleport(bottom, yaw);
                yield return Wait(0.2f);
                yield return Tap(keyboard.cKey);
                yield return Wait(0.3f);
                Assert.AreEqual(Posture.Crouching, posture.Current);
                Press(keyboard.wKey, queueEventOnly: true);
                end = Time.time + 9f;
                while (Time.time < end && player.transform.position.y < Upper - 0.05f)
                {
                    AssertNotInsideSolid($"{name} stair, crouched");
                    yield return null;
                }
                Release(keyboard.wKey, queueEventOnly: true);
                Assert.Greater(player.transform.position.y, Upper - 0.1f, $"{name}: crouched up the stairs");
                yield return Tap(keyboard.cKey);
                yield return Wait(0.3f);

                // Prone: crawls up the ramp (slowly), never sinks into it.
                Teleport(bottom, yaw);
                yield return Wait(0.2f);
                yield return Tap(keyboard.zKey);
                yield return Wait(0.5f);
                Assert.AreEqual(Posture.Prone, posture.Current);
                float startY = player.transform.position.y;
                Press(keyboard.wKey, queueEventOnly: true);
                end = Time.time + 4f;
                while (Time.time < end)
                {
                    AssertNotInsideSolid($"{name} stair, prone");
                    yield return null;
                }
                Release(keyboard.wKey, queueEventOnly: true);
                Assert.Greater(player.transform.position.y - startY, 1f, $"{name}: crawled up the ramp");
                yield return Tap(keyboard.zKey);
                yield return Wait(0.5f);
                Assert.AreEqual(Posture.Standing, posture.Current, "room to stand up inside the tunnel");

                // Jump on the stairs: the head stays under the tunnel ceiling, lands back on the ramp.
                float maxHead = float.MinValue, ceilingAt = float.MaxValue;
                yield return Tap(keyboard.spaceKey);
                end = Time.time + 1.2f;
                while (Time.time < end)
                {
                    Vector3 p = player.transform.position;
                    float along = Mathf.Max(0f, Mathf.Abs(p.z - rampZ)); // from the foot of the ramp
                    ceilingAt = Mathf.Min(ceilingAt, along * Mathf.Tan(30f * Mathf.Deg2Rad) + 3.6f);
                    maxHead = Mathf.Max(maxHead, p.y + 1.8f - along * Mathf.Tan(30f * Mathf.Deg2Rad));
                    AssertNotInsideSolid($"{name} stair, jumping");
                    yield return null;
                }
                Assert.Less(maxHead, 3.6f + 0.05f, $"{name}: the head stays under the tunnel ceiling");
                Assert.IsTrue(motor.IsGrounded, $"{name}: back on the ramp after the jump");
            }
        }

        [UnityTest]
        public IEnumerator Gallery_Parapet_StopsFalls_SprintJumpSlideProne()
        {
            yield return LoadRuins(false);
            // Sprint + repeated jumps straight at the parapet over the atrium.
            Teleport(new Vector3(2f, Upper + 0.05f, 11f), 180f);
            yield return Wait(0.2f);
            Press(keyboard.wKey, queueEventOnly: true);
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            for (int i = 0; i < 6; i++)
            {
                yield return Tap(keyboard.spaceKey);
                yield return Wait(0.45f);
                Assert.Greater(player.transform.position.y, Upper - 0.1f, "still on the gallery");
                Assert.Greater(player.transform.position.z, 8.3f, "not past the parapet");
            }
            Release(keyboard.wKey, queueEventOnly: true);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return Wait(0.5f);
            Assert.AreEqual(Upper, player.transform.position.y, 0.08f);

            // Slide into the parapet.
            Teleport(new Vector3(-2f, Upper + 0.05f, 11.9f), 180f);
            yield return Wait(0.2f);
            Press(keyboard.wKey, queueEventOnly: true);
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            yield return Wait(0.45f);
            yield return Tap(keyboard.cKey);
            yield return Wait(1.2f);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return Wait(0.3f);
            Assert.Greater(player.transform.position.y, Upper - 0.1f, "slide stops at the parapet");
            Assert.Greater(player.transform.position.z, 8.3f);
            AssertNotInsideSolid("after the slide");

            // Prone at the edge.
            yield return Tap(keyboard.zKey);
            yield return Wait(0.5f);
            yield return Hold(1.5f, keyboard.wKey);
            Assert.Greater(player.transform.position.y, Upper - 0.1f, "prone at the edge stays on the gallery");
            Assert.Greater(player.transform.position.z, 8.3f);
        }

        // ------------------------------------------------------------------ combat across floors

        IEnumerator FireAt(Vector3 point, float seconds)
        {
            Vector3 eye = Camera.main.transform.position;
            Vector3 d = point - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; // positive = up
            motor.SetYawPitch(yaw, pitch);
            yield return null;
            Assert.Greater(Vector3.Dot(Camera.main.transform.forward, d.normalized), 0.999f, "aimed at the target");
            yield return Hold(seconds, mouse.leftButton);
            yield return Wait(0.6f);
        }

        [UnityTest]
        public IEnumerator Shots_DownFromTheGallery_Hit_FloorAndWallsBlock()
        {
            yield return LoadRuins(false);
            var a = zombies[0];
            var b = zombies[1];
            var c = zombies[2];
            // Hall, far half: from the parapet the 1.3 m wall hides the near half (it is cover both ways).
            Park(a, new Vector3(2f, 0f, -7f), true);
            Park(b, new Vector3(-10f, Upper, -CorridorZ), true); // upper south corridor, right above the ground corridor
            Park(c, new Vector3(0.5f, 0f, CorridorZ), true);     // north corridor, behind the ring wall (off the door line)
            yield return Wait(0.3f);

            Teleport(new Vector3(2f, Upper + 0.05f, 8.7f), 180f);
            yield return Wait(0.3f);
            Press(mouse.rightButton, queueEventOnly: true); // aimed (ADS): a narrow line just over the parapet
            yield return Wait(0.4f);
            yield return FireAt(a.transform.position + Vector3.up * 1.5f, 0.5f);
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Assert.Less(a.Health, a.MaxHealth, "a shot down from the gallery into the hall hits");

            Teleport(new Vector3(-10f, 0.05f, -CorridorZ + 0.75f), 0f);
            yield return Wait(0.3f);
            yield return FireAt(b.transform.position + Vector3.up * 1f, 0.5f);
            Assert.AreEqual(b.MaxHealth, b.Health, "the upper floor slab stops shots from below");

            Teleport(new Vector3(-6f, 0.05f, 10f), 0f);
            yield return Wait(0.3f);
            yield return FireAt(c.transform.position + Vector3.up * 1f, 0.5f);
            Assert.AreEqual(c.MaxHealth, c.Health, "the ring wall stops shots");
        }

        [UnityTest]
        public IEnumerator Zombies_FollowUpAndDownTheStairs_Attack_NotThroughFloorsOrAcrossTheAtrium()
        {
            yield return LoadRuins(false);
            var health = player.GetComponent<PlayerHealth>();
            var z = zombies[0];
            foreach (var other in zombies.Skip(1))
                other.gameObject.SetActive(false);

            // Up: zombie on the ground in the east wing, player upstairs beside the stair exit.
            Park(z, new Vector3(15f, 0f, -10f), false);
            Teleport(new Vector3(14f, Upper + 0.05f, -2f), 180f);
            yield return Wait(0.2f);
            z.Active = true;
            bool onStairs = false;
            float end = Time.time + 25f;
            while (Time.time < end && health.Health >= health.MaxHealth)
            {
                var p = z.transform.position;
                if (p.y > 0.6f && p.y < Upper - 0.6f && p.x > TunnelInner)
                    onStairs = true;
                yield return null;
            }
            Assert.IsTrue(onStairs, "went up the east stair tunnel");
            Assert.Greater(z.transform.position.y, Upper - 0.5f, "reached the upper floor");
            Assert.Less(health.Health, health.MaxHealth, "attacked the player upstairs");

            // Down: zombie on the gallery, player on the ground beside the west stair.
            z.Active = false;
            yield return null;
            Park(z, new Vector3(-3f, Upper, 10f), false);
            Teleport(new Vector3(-15f, 0.05f, 10f), 0f);
            float before = health.Health;
            yield return Wait(0.2f);
            z.Active = true;
            onStairs = false;
            end = Time.time + 30f;
            while (Time.time < end && health.Health >= before)
            {
                var p = z.transform.position;
                if (p.y > 0.6f && p.y < Upper - 0.6f && p.x < -TunnelInner)
                    onStairs = true;
                yield return null;
            }
            Assert.IsTrue(onStairs, "came down the west stair tunnel");
            Assert.Less(z.transform.position.y, 0.5f, "reached the ground floor");
            Assert.Less(health.Health, before, "attacked the player downstairs");
            z.Active = false;
            yield return Wait(2f); // let a swing in progress finish

            // Not through the floor: zombie upstairs right above the player (horizontally in reach), held in place.
            float hp = health.Health;
            Park(z, new Vector3(-10f, Upper, -CorridorZ), true);
            Teleport(new Vector3(-10f, 0.05f, -CorridorZ + 0.75f), 0f);
            z.Active = true;
            yield return null;
            z.GetComponent<NavMeshAgent>().isStopped = true;
            end = Time.time + 4f;
            while (Time.time < end)
            {
                z.GetComponent<NavMeshAgent>().isStopped = true;
                Assert.AreNotEqual(ZombieState.Attack, z.State, "no attack through the upper floor");
                yield return null;
            }
            Assert.AreEqual(hp, health.Health, "no damage through the floor");

            // Not across the atrium: zombie in the hall below the gallery edge, player at the parapet above.
            z.Active = false;
            yield return null;
            Park(z, new Vector3(2f, 0f, 7.6f), true);
            Teleport(new Vector3(2f, Upper + 0.05f, 8.75f), 180f);
            z.Active = true;
            end = Time.time + 4f;
            while (Time.time < end)
            {
                z.GetComponent<NavMeshAgent>().isStopped = true;
                Assert.AreNotEqual(ZombieState.Attack, z.State, "no attack from the hall up to the gallery");
                yield return null;
            }
            Assert.AreEqual(hp, health.Health, "no damage across the atrium");
        }

        // ------------------------------------------------------------------ round, retry, menu

        [UnityTest]
        public IEnumerator Start_IsSafe_AllDefeatedDoesNotEnd_RetryResetsPlayerAndZombies()
        {
            yield return LoadRuins(true);
            var health = player.GetComponent<PlayerHealth>();
            var session = Object.FindAnyObjectByType<TrainingSession>();
            Assert.AreEqual(5, session.Total);
            // Agents are saved off and switched on in Zombie.Start, after the scene's NavMesh is registered.
            Assert.IsTrue(zombies.All(z => z.GetComponent<NavMeshAgent>().enabled && z.GetComponent<NavMeshAgent>().isOnNavMesh), "every zombie on the NavMesh");
            Vector3 spawn = player.transform.position;
            // Nobody reaches the start within the first seconds.
            yield return Wait(8f);
            Assert.AreEqual(health.MaxHealth, health.Health, "not attacked right after the start");

            // Damage the player, kill every zombie: the round goes on (the ruins end by the escape point or death).
            health.TakeDamage(30f, player.transform.position + Vector3.forward);
            foreach (var z in zombies)
                z.ApplyDamage(z.MaxHealth, z.transform.position, Vector3.forward);
            yield return null;
            yield return null;
            Assert.AreEqual(TrainingResult.Playing, session.Result);
            Assert.AreEqual(0, session.Remaining);
            Assert.IsFalse(Object.FindAnyObjectByType<TrainingHud>().ResultPanel.activeSelf, "no result for defeating every zombie");

            // Retry: a fresh round.
            session.Retry();
            yield return null;
            yield return null;
            yield return LoadRuins(true);
            session = Object.FindAnyObjectByType<TrainingSession>();
            health = player.GetComponent<PlayerHealth>();
            Assert.AreEqual(TrainingResult.Playing, session.Result);
            Assert.AreEqual(5, session.Remaining);
            Assert.IsTrue(zombies.All(z => !z.IsDead && z.Health == z.MaxHealth), "all zombies back");
            Assert.AreEqual(health.MaxHealth, health.Health, "player back to full health");
            Assert.Less(Vector3.Distance(spawn, player.transform.position), 0.5f, "player back at the start");
        }

        [UnityTest]
        public IEnumerator Menu_TrainingToRuins_AndBack_SettingsKept()
        {
            PlayerPrefs.SetInt(ControlSettings.TouchTransparencyKey, 37);
            PlayerPrefs.Save();
            yield return LoadScene(InputMode.Desktop, GameScenes.Training);
            var menu = Object.FindAnyObjectByType<GameMenu>();
            menu.Open();
            yield return null;
            var ruins = GameObject.Find("RuinsButton");
            Assert.IsNotNull(ruins, "training menu offers the ruins");
            Assert.IsNotNull(GameObject.Find("SceneButton"), "and the firing range");
            ruins.GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            yield return LoadScene(InputMode.Desktop, Ruins);
            Assert.AreEqual(Ruins, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            menu = Object.FindAnyObjectByType<GameMenu>();
            menu.Open();
            yield return null;
            Assert.IsNull(GameObject.Find("RuinsButton"), "no button to the scene already open");
            Assert.IsNotNull(GameObject.Find("RetryButton"), "retry from the menu");
            Assert.IsNotNull(GameObject.Find("TrainingButton"));
            GameObject.Find("SceneButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            Assert.AreEqual(GameScenes.FiringRange, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "back out to the firing range");
            Assert.AreEqual(37, PlayerPrefs.GetInt(ControlSettings.TouchTransparencyKey), "control settings kept");
        }
    }
}
