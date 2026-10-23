using System.Collections;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Map;
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
    /// Escape from the ruins test field (test-only provisional rule): the escape point is away from the start (another
    /// floor), "脱出する" shows only on it, pressing it = 生還 with time and defeated count; defeating every zombie does
    /// not end the round; death = 死亡, and death on the same frame wins over the escape; after the result nothing moves,
    /// shoots or hurts; retry starts everything over but keeps the test gun choice and the settings; the result panel
    /// leads to the menu; the point shows on the field and the floor map; the button keeps clear of the HUD, the
    /// controls and the map.
    /// </summary>
    public class RuinsEscapeTests : FiringRangeTestBase
    {
        const string Ruins = "Proto_Ruins";
        const string Smg = "proto_smg";

        Zombie[] zombies;
        TrainingSession session;
        TrainingHud hud;
        ExtractionPoint point;
        PlayerHealth health;
        string savedMapLayout;
        bool hadMapVisible;
        int savedMapVisible;

        public override void Setup()
        {
            base.Setup();
            savedMapLayout = PlayerPrefs.GetString(MapSettings.MinimapLayoutKey, null);
            hadMapVisible = PlayerPrefs.HasKey(MapSettings.MinimapVisibleKey);
            savedMapVisible = PlayerPrefs.GetInt(MapSettings.MinimapVisibleKey, 1);
            MapSettings.ClearSaved();
            DevGunSelection.ClearSelection();
        }

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            MapSettings.ClearSaved();
            if (!string.IsNullOrEmpty(savedMapLayout))
                PlayerPrefs.SetString(MapSettings.MinimapLayoutKey, savedMapLayout);
            if (hadMapVisible)
                PlayerPrefs.SetInt(MapSettings.MinimapVisibleKey, savedMapVisible);
            base.TearDown();
        }

        IEnumerator LoadRuins(InputMode mode = InputMode.Desktop, bool zombiesActive = false)
        {
            yield return LoadScene(mode, Ruins);
            zombies = Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).OrderBy(z => z.name).ToArray();
            foreach (var z in zombies)
                z.Active = zombiesActive;
            session = Object.FindAnyObjectByType<TrainingSession>();
            hud = Object.FindAnyObjectByType<TrainingHud>();
            point = Object.FindAnyObjectByType<ExtractionPoint>();
            health = player.GetComponent<PlayerHealth>();
            yield return null;
        }

        void OnThePoint() => Teleport(point.Position + new Vector3(0.3f, 0.05f, 0.2f), 90f);

        static void Park(Zombie z, Vector3 position)
        {
            var agent = z.GetComponent<NavMeshAgent>();
            agent.Warp(position);
            agent.isStopped = true;
        }

        // ------------------------------------------------------------------ point, button, survival

        [UnityTest]
        public IEnumerator Point_AwayFromTheStart_ButtonOnlyOnIt_AllDefeatedDoesNotEnd_EscapeIsSurvival_EverythingStops()
        {
            yield return LoadRuins();
            Assert.IsNotNull(point, "the ruins have an escape point");
            var map = Object.FindAnyObjectByType<EnemyDetector>().Map;
            Vector3 start = player.transform.position;
            Assert.AreNotEqual(map.NearestFloor(start.y + 0.1f), map.NearestFloor(point.Position.y + 0.1f), "on another floor than the start");
            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(start, point.Position, NavMesh.AllAreas, path));
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "reachable on foot");
            float walk = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                walk += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            Assert.Greater(walk, 30f, "the player has to move there (up a stair tunnel and round)");
            Assert.IsFalse(point.PlayerInside);
            Assert.IsFalse(hud.EscapeButton.gameObject.activeSelf, "no button at the start");

            // On the field: a green pad and posts, no collider (shots, sight, footsteps pass as before).
            Assert.IsNotNull(point.transform.Find("Pad").GetComponent<MeshRenderer>());
            Assert.AreEqual(0, point.GetComponentsInChildren<Collider>().Length);

            // Every zombie defeated: the round goes on (not a condition of the escape).
            foreach (var z in zombies)
                z.ApplyDamage(z.MaxHealth, z.transform.position, Vector3.forward);
            yield return null;
            yield return null;
            Assert.AreEqual(TrainingResult.Playing, session.Result, "defeating every zombie does not end the round");
            Assert.AreEqual(5, session.Defeated);
            Assert.IsFalse(session.RequestEscape(), "not accepted off the point");

            // Directly below the point (ground floor): not on it.
            Teleport(new Vector3(point.Position.x, 0.05f, point.Position.z), 0f);
            yield return null;
            Assert.IsFalse(point.PlayerInside, "the floor below does not count");
            Assert.IsFalse(hud.EscapeButton.gameObject.activeSelf);

            OnThePoint();
            yield return null;
            yield return null;
            Assert.IsTrue(point.PlayerInside);
            Assert.IsTrue(hud.EscapeButton.gameObject.activeSelf, "shown on the point");
            Assert.AreEqual("脱出する", hud.EscapeButton.GetComponentInChildren<Text>().text);
            float before = session.ElapsedTime;
            hud.EscapeButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(TrainingResult.Escaped, session.Result);
            Assert.IsTrue(hud.ResultPanel.activeSelf);
            Assert.AreEqual("生還", hud.ResultTitle);
            int s = Mathf.FloorToInt(session.ElapsedTime);
            StringAssert.Contains($"経過時間 {s / 60}:{s % 60:00}", hud.ResultDetail);
            StringAssert.Contains("討伐数 5", hud.ResultDetail);
            Assert.GreaterOrEqual(session.ElapsedTime, before);
            Assert.IsFalse(hud.EscapeButton.gameObject.activeSelf, "the button goes with the result");

            // After the result: the clock stops, no move, no shot.
            float stopped = session.ElapsedTime;
            Vector3 at = player.transform.position;
            int loaded = gun.Ammo.Loaded;
            Press(keyboard.wKey, queueEventOnly: true);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.5f);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(stopped, session.ElapsedTime, "the clock stopped");
            Assert.Less(Vector3.Distance(at, player.transform.position), 0.05f, "no movement");
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "no shot");
        }

        [UnityTest]
        public IEnumerator AfterEscape_ZombiesDoNoDamage()
        {
            yield return LoadRuins(zombiesActive: true);
            OnThePoint();
            // A living zombie right beside the player.
            Park(zombies[0], point.Position + new Vector3(1.2f, 0f, 0f));
            zombies[0].GetComponent<NavMeshAgent>().isStopped = false;
            yield return null;
            yield return null;
            Assert.IsTrue(session.RequestEscape());
            yield return null;
            Assert.AreEqual(TrainingResult.Escaped, session.Result);
            float hp = health.Health;
            yield return Wait(3f);
            Assert.AreEqual(hp, health.Health, "no damage after the result");
            Assert.IsTrue(zombies.All(z => !z.Active), "the zombies stopped");
            Assert.AreNotEqual(ZombieState.Attack, zombies[0].State);
        }

        // ------------------------------------------------------------------ death

        [UnityTest]
        public IEnumerator Death_IsDeath_AndDeathOnTheSameFrameWinsOverTheEscape()
        {
            yield return LoadRuins();
            health.TakeDamage(health.MaxHealth, player.transform.position + Vector3.forward);
            yield return null;
            Assert.AreEqual(TrainingResult.Failed, session.Result);
            Assert.AreEqual("死亡", hud.ResultTitle);
            StringAssert.Contains("討伐数 0", hud.ResultDetail);

            // Both on one frame: the escape is pressed, then a fatal hit arrives before the result is decided.
            yield return LoadRuins();
            OnThePoint();
            yield return null;
            yield return null;
            Assert.IsTrue(session.CanEscape);
            Assert.IsTrue(session.RequestEscape());
            health.TakeDamage(health.MaxHealth, player.transform.position + Vector3.forward);
            yield return null;
            Assert.AreEqual(TrainingResult.Failed, session.Result, "death wins over the escape on the same frame");
            Assert.AreEqual("死亡", hud.ResultTitle);
            Assert.IsFalse(session.RequestEscape(), "no escape after the result");
        }

        // ------------------------------------------------------------------ retry, menu, settings

        [UnityTest]
        public IEnumerator Retry_StartsOver_KeepsTheTestGunAndSettings_ResultLeadsToTheMenu()
        {
            PlayerPrefs.SetInt(ControlSettings.TouchTransparencyKey, 41);
            MapSettings.SavePlacement(new MiniMapPlacement { center = new Vector2(0.3f, 0.8f), scale = 1.2f });
            yield return LoadRuins();
            var selection = player.GetComponent<DevGunSelection>();
            Assert.IsTrue(selection.Select(Smg));
            Vector3 start = player.transform.position;
            zombies[0].ApplyDamage(zombies[0].MaxHealth, zombies[0].transform.position, Vector3.forward);
            zombies[1].ApplyDamage(10f, zombies[1].transform.position, Vector3.forward);
            health.TakeDamage(25f, player.transform.position + Vector3.forward);
            gun.Ammo.Set(3, 10);
            yield return Wait(1f);
            OnThePoint();
            yield return null;
            yield return null;
            hud.EscapeButton.onClick.Invoke();
            yield return null;
            Assert.AreEqual(TrainingResult.Escaped, session.Result);
            StringAssert.Contains("討伐数 1", hud.ResultDetail);

            // The menu from the result panel: the panel steps aside and comes back when the menu closes.
            var menu = Object.FindAnyObjectByType<GameMenu>();
            hud.ResultPanel.transform.Find("Window/MenuButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(hud.ResultPanel.activeSelf);
            menu.Close();
            yield return null;
            Assert.IsTrue(hud.ResultPanel.activeSelf, "back to the result");

            // Retry from the result panel.
            hud.ResultPanel.transform.Find("Window/RetryButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;
            yield return LoadRuins();
            Assert.AreEqual(TrainingResult.Playing, session.Result);
            Assert.Less(session.ElapsedTime, 0.5f, "the clock starts over");
            Assert.AreEqual(0, session.Defeated);
            Assert.IsTrue(zombies.All(z => !z.IsDead && z.Health == z.MaxHealth), "every zombie back");
            Assert.AreEqual(health.MaxHealth, health.Health);
            Assert.Less(Vector3.Distance(start, player.transform.position), 0.5f, "back at the start");
            Assert.IsFalse(point.PlayerInside);
            Assert.IsFalse(hud.EscapeButton.gameObject.activeSelf, "the escape state starts over");
            Assert.IsFalse(hud.ResultPanel.activeSelf);
            Assert.AreEqual(Smg, gun.Definition.weaponId, "the test gun choice is kept");
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded, "fresh ammo");
            Assert.AreEqual(41, PlayerPrefs.GetInt(ControlSettings.TouchTransparencyKey), "control settings kept");
            Assert.IsTrue(MapSettings.TryLoadPlacement(out var placement));
            Assert.AreEqual(0.3f, placement.center.x, 1e-4f, "map settings kept");
            Assert.AreEqual(1.2f, placement.scale, 1e-4f);
        }

        // ------------------------------------------------------------------ map, layout

        [UnityTest]
        public IEnumerator Map_ShowsThePoint_UpOrDownOnAnotherFloor_ButtonClearOfHudControlsAndMap()
        {
            yield return LoadRuins(InputMode.Touch);
            var view = Object.FindAnyObjectByType<MiniMapView>();
            yield return null;
            Assert.AreEqual("exit_edge_up", view.ExitShape, "from the start: on the edge, upstairs");
            Teleport(point.Position + new Vector3(-4f, 0.05f, 0f), 90f);
            yield return Wait(0.2f);
            Assert.AreEqual("exit", view.ExitShape, "same floor, close: at its place");
            Assert.AreEqual(view.WorldToFrame(point.Position).x, view.ExitFramePosition.x, 1f);
            Assert.AreEqual(view.WorldToFrame(point.Position).y, view.ExitFramePosition.y, 1f);
            Teleport(new Vector3(point.Position.x - 4f, 0.05f, point.Position.z), 90f);
            yield return Wait(0.2f);
            Assert.AreEqual("exit_up", view.ExitShape, "below it: an up chevron");
            Teleport(new Vector3(point.Position.x, 0.05f, point.Position.z), 90f);
            yield return Wait(0.2f);
            Assert.AreEqual("exit_self_up", view.ExitShape, "straight below: beside the arrow");

            OnThePoint();
            yield return null;
            yield return null;
            Assert.IsTrue(hud.EscapeButton.gameObject.activeSelf);
            var r = hud.EscapeRect;
            Assert.IsFalse(r.Overlaps(view.ShownRect), "clear of the map");
            foreach (var b in view.AvoidedBoxes)
                Assert.IsFalse(r.Overlaps(b), $"clear of the HUD box {b}");
            foreach (var c in view.AvoidedCircles)
            {
                float nx = Mathf.Clamp(c.x, r.xMin, r.xMax), ny = Mathf.Clamp(c.y, r.yMin, r.yMax);
                Assert.GreaterOrEqual(new Vector2(nx - c.x, ny - c.y).magnitude, c.z, $"clear of the control at {c}");
            }
            Vector2 safe = view.SafeSize;
            Assert.That(r.xMin, Is.GreaterThanOrEqualTo(-0.01f));
            Assert.That(r.yMin, Is.GreaterThanOrEqualTo(-0.01f));
            Assert.That(r.xMax, Is.LessThanOrEqualTo(safe.x + 0.01f));
            Assert.That(r.yMax, Is.LessThanOrEqualTo(safe.y + 0.01f));
        }
    }
}
