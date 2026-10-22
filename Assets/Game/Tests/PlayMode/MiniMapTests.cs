using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.Map;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Floor map of the ruins (development prototype): the baked map matches the building, the shown floor follows the
    /// player (once per stair walk), heading-up position / rotation; enemies show only when seen (view + clear line) or
    /// heard (footsteps, shortened by walls / floors), at the detected position, and go after a short time, at death and
    /// on retry; the on / off setting is saved and only hides the display.
    /// </summary>
    public class MiniMapTests : FiringRangeTestBase
    {
        const string Ruins = "Proto_Ruins";
        const float Upper = 5f, CorridorZ = 15.15f, RingHalf = 12.6f;

        Zombie[] zombies;
        MapEnemy[] enemies;
        EnemyDetector detector;
        MiniMapView view;
        FloorMapData map;
        bool hadMinimapKey;
        int savedMinimap;

        public override void Setup()
        {
            base.Setup();
            hadMinimapKey = PlayerPrefs.HasKey(MapSettings.MinimapVisibleKey);
            savedMinimap = PlayerPrefs.GetInt(MapSettings.MinimapVisibleKey, 1);
            MapSettings.ClearSaved();
        }

        public override void TearDown()
        {
            MapSettings.ClearSaved();
            if (hadMinimapKey)
                PlayerPrefs.SetInt(MapSettings.MinimapVisibleKey, savedMinimap);
            base.TearDown();
        }

        IEnumerator LoadRuins()
        {
            yield return LoadScene(InputMode.Desktop, Ruins);
            zombies = Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None).OrderBy(z => z.name).ToArray();
            foreach (var z in zombies)
                z.Active = false;
            enemies = zombies.Select(z => z.GetComponent<MapEnemy>()).ToArray();
            detector = Object.FindAnyObjectByType<EnemyDetector>();
            view = Object.FindAnyObjectByType<MiniMapView>();
            map = detector.Map;
            yield return null;
            // Everyone out of the way, standing (no footsteps): each test brings in the zombies it needs.
            for (int i = 0; i < zombies.Length; i++)
                Park(zombies[i], new Vector3(-16f + i * 1.5f, Upper, 15.15f));
            yield return Wait(0.3f);
        }

        static void Park(Zombie z, Vector3 position)
        {
            var agent = z.GetComponent<NavMeshAgent>();
            agent.Warp(position);
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        static void Walk(Zombie z, Vector3 from, Vector3 to)
        {
            var agent = z.GetComponent<NavMeshAgent>();
            agent.Warp(from);
            agent.isStopped = false;
            agent.SetDestination(to);
        }

        void Look(Vector3 feet, float yaw, float pitch = 0f)
        {
            Teleport(feet, yaw);
            motor.SetYawPitch(yaw, pitch);
        }

        // ------------------------------------------------------------------ map

        [UnityTest]
        public IEnumerator Map_MatchesTheBuilding_ShownFloorFollowsThePlayer_OncePerStairWalk()
        {
            yield return LoadRuins();
            Assert.AreEqual(2, map.floors.Length);
            void Cell(int floor, float x, float z, MapCell expected, string what) =>
                Assert.AreEqual(expected, map.CellAt(floor, new Vector3(x, 0f, z)), $"{what} ({x}, {z}) floor {floor + 1}");
            Cell(0, 0f, -CorridorZ, MapCell.Floor, "south corridor");
            Cell(1, 0f, -CorridorZ, MapCell.Floor, "upper south corridor");
            Cell(0, 5f, 5f, MapCell.Floor, "atrium floor");
            Cell(1, 5f, 5f, MapCell.Void, "atrium seen from the gallery (a drop, not floor)");
            Cell(1, 10.3f, 0f, MapCell.Floor, "gallery");
            Cell(1, 8.15f, 2f, MapCell.Wall, "parapet");
            Cell(0, RingHalf, 3f, MapCell.Wall, "ring wall");
            Cell(1, RingHalf, 3f, MapCell.Wall, "ring wall upstairs");
            Cell(0, RingHalf, 0f, MapCell.Floor, "main door of the ring wall");
            Cell(0, 21.3f, 0f, MapCell.Wall, "outer wall");
            Cell(0, 19.35f, -2f, MapCell.Stairs, "east stair");
            Cell(1, 19.35f, -2f, MapCell.Stairs, "east stair (both floors)");
            Cell(0, -19.35f, 2f, MapCell.Stairs, "west stair");
            Cell(0, 0f, 0f, MapCell.Wall, "altar (cover)");

            // Floor cells and walls agree with the building: every floor cell has a floor under it and room above.
            int checkedCells = 0;
            for (int f = 0; f < 2; f++)
                for (int y = 2; y < map.height; y += 7)
                    for (int x = 2; x < map.width; x += 7)
                    {
                        if (map.Cell(f, x, y) != MapCell.Floor)
                            continue;
                        var p = new Vector3(map.origin.x + (x + 0.5f) * map.cellSize, map.floors[f].baseY, map.origin.y + (y + 0.5f) * map.cellSize);
                        Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.45f, Vector3.down, 1.05f, 1, QueryTriggerInteraction.Ignore), $"floor under {p}");
                        Assert.IsFalse(Physics.CheckSphere(p + Vector3.up * 1.2f, 0.1f, 1, QueryTriggerInteraction.Ignore), $"room above {p}");
                        checkedCells++;
                    }
            Assert.Greater(checkedCells, 100);

            // Position and heading-up rotation.
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            yield return Wait(0.1f);
            Assert.AreEqual(0, view.DisplayedFloor);
            Assert.AreEqual(90f, view.MapRotation, 0.5f);
            var rotor = view.Frame.Find("Background/Viewport/Rotor");
            var image = rotor.Find("FloorImage").GetComponent<RawImage>();
            Assert.AreEqual(90f, rotor.localEulerAngles.z, 0.5f, "the map turns with the player");
            Vector2 centreCell = -image.rectTransform.anchoredPosition / (map.cellSize * view.UnitsPerMetre);
            Assert.AreEqual(map.ToCells(player.transform.position).x, centreCell.x, 0.05f, "the player is at the centre");
            Assert.AreEqual(map.ToCells(player.transform.position).y, centreCell.y, 0.05f);
            Vector2 ahead = view.WorldToFrame(player.transform.position + player.transform.forward * 5f);
            Assert.AreEqual(0f, ahead.x, 0.5f, "what is ahead is straight up on the map");
            Assert.AreEqual(5f * view.UnitsPerMetre, ahead.y, 0.5f);
            Vector2 north = view.WorldToFrame(player.transform.position + Vector3.forward * 5f);
            Assert.Less(north.x, -40f, "facing east, north is on the left");
            Assert.AreSame(image.texture, image.texture);

            // Upper floor, then hysteresis on the east stair (ramp y = (z + 7) tan 30).
            Look(new Vector3(0f, Upper + 0.05f, -CorridorZ), 0f);
            yield return Wait(0.1f);
            Assert.AreEqual(1, view.DisplayedFloor);
            Assert.AreEqual("2F", view.Frame.Find("FloorLabel").GetComponent<Text>().text);
            float Ramp(float y) => -7f + y / Mathf.Tan(30f * Mathf.Deg2Rad);
            Look(new Vector3(19.35f, 2.4f, Ramp(2.3f)), 180f);
            yield return Wait(0.1f);
            Assert.AreEqual(1, view.DisplayedFloor, "half way down: still the upper floor");
            Look(new Vector3(19.35f, 1.5f, Ramp(1.4f)), 180f);
            yield return Wait(0.1f);
            Assert.AreEqual(0, view.DisplayedFloor, "low on the stair: ground floor");
            Look(new Vector3(19.35f, 3.1f, Ramp(3f)), 0f);
            yield return Wait(0.1f);
            Assert.AreEqual(0, view.DisplayedFloor, "half way up: still the ground floor");

            // Walking up and down the stair switches exactly once each way.
            foreach (var (start, yaw, endFloor) in new[] { (new Vector3(19.35f, 0.05f, -8.6f), 0f, 1), (new Vector3(19.35f, Upper + 0.05f, 3.2f), 180f, 0) })
            {
                Look(start, yaw);
                yield return Wait(0.2f);
                int last = view.DisplayedFloor, changes = 0;
                Press(keyboard.wKey, queueEventOnly: true);
                float end = Time.time + 7f;
                while (Time.time < end && (endFloor == 1 ? player.transform.position.y < Upper - 0.05f : player.transform.position.y > 0.05f))
                {
                    yield return null;
                    if (view.DisplayedFloor != last)
                    {
                        changes++;
                        last = view.DisplayedFloor;
                    }
                }
                Release(keyboard.wKey, queueEventOnly: true);
                yield return Wait(0.2f);
                Assert.AreEqual(endFloor, view.DisplayedFloor);
                Assert.AreEqual(1, changes, "one switch for one walk over the stair");
            }
        }

        // ------------------------------------------------------------------ sight

        [UnityTest]
        public IEnumerator Sight_InViewAndClearOnly_NotThroughWallsOrFloors_MarkerStaysWhereLastSeen()
        {
            yield return LoadRuins();
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f); // south corridor, looking east
            var (open, behindWall, above, behind) = (zombies[0], zombies[1], zombies[2], zombies[3]);
            Park(open, new Vector3(8f, 0f, -CorridorZ));
            Park(behindWall, new Vector3(8f, 0f, -10.5f));      // ring aisle, behind the ring wall
            Park(above, new Vector3(8f, Upper, -CorridorZ));     // upper corridor, above the slab
            Park(behind, new Vector3(-8f, 0f, -CorridorZ));     // in the open, behind the player
            yield return Wait(0.4f);

            Assert.IsTrue(detector.CanSee(enemies[0]), "in view and in the open");
            Assert.IsFalse(detector.CanSee(enemies[1]), "behind the ring wall");
            Assert.IsFalse(detector.CanSee(enemies[2]), "above the floor slab");
            Assert.IsFalse(detector.CanSee(enemies[3]), "behind the player (outside the view)");
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var seen));
            Assert.AreEqual(ContactKind.Seen, seen.Kind);
            for (int i = 1; i < 4; i++)
                Assert.IsFalse(detector.TryGetContact(enemies[i], out _), $"{zombies[i].name}: not on the map (standing, unseen)");
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[0], out string shape, out Vector2 at));
            Assert.AreEqual("seen", shape);
            Assert.AreEqual(view.WorldToFrame(open.transform.position).y, at.y, 1f);
            Assert.AreEqual(1, view.ShownMarkers);

            // Hidden: the marker stays where it was last seen (not where the zombie is now), then goes.
            Vector3 lastSeen = open.transform.position;
            Park(open, new Vector3(8f, 0f, -10.5f));
            yield return Wait(0.5f);
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var held));
            Assert.Less(Vector3.Distance(lastSeen, held.Position), 0.05f, "kept at the last seen position");
            Assert.IsTrue(view.TryGetMarker(enemies[0], out _, out Vector2 heldAt));
            Assert.AreEqual(view.WorldToFrame(lastSeen).x, heldAt.x, 1f);
            Assert.AreEqual(view.WorldToFrame(lastSeen).y, heldAt.y, 1f);
            yield return Wait(detector.DisplayTime(ContactKind.Seen) + 0.2f);
            Assert.IsFalse(detector.TryGetContact(enemies[0], out _), "gone a short time after it was last seen");
            Assert.AreEqual(0, view.ShownMarkers);

            // From the gallery down into the atrium: seen on the floor below (an arrow down, not a same-floor marker).
            // (Close below the gallery, the 1.3 m parapet hides the zombie: it must be farther out in the atrium.)
            Look(new Vector3(2f, Upper + 0.05f, 8.7f), 180f, -22f);
            Park(behind, new Vector3(2f, 0f, -4f));
            yield return Wait(0.4f);
            Assert.IsTrue(detector.TryGetContact(enemies[3], out var below));
            Assert.AreEqual(ContactKind.Seen, below.Kind);
            Assert.AreEqual(0, below.Floor);
            Assert.AreEqual(1, view.DisplayedFloor);
            Assert.IsTrue(view.TryGetMarker(enemies[3], out shape, out _));
            Assert.That(shape, Does.EndWith("seen_down"), "an arrow down (on the edge when beyond the map's range)");
        }

        // ------------------------------------------------------------------ footsteps

        [UnityTest]
        public IEnumerator Footsteps_HeardWithinRange_WallsShortenIt_StandingIsSilent_MarkerStaysThenGoes()
        {
            yield return LoadRuins();
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            var walker = zombies[0];
            var heard = new List<MapContact>();
            detector.Detected += c => { if (c.Kind == ContactKind.Heard) heard.Add(c); };

            // Behind one wall, close (about 5 m): heard.
            Walk(walker, new Vector3(4f, 0f, -10.5f), new Vector3(9f, 0f, -10.5f));
            yield return Wait(1.6f);
            Assert.IsFalse(detector.CanSee(enemies[0]), "hidden by the ring wall");
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var c), "footsteps heard through one wall nearby");
            Assert.AreEqual(ContactKind.Heard, c.Kind);
            Assert.AreEqual(-10.5f, c.Position.z, 0.3f, "at the step position");

            // Stops: no more footsteps; the marker stays at the last step, does not follow, then goes.
            Park(walker, walker.transform.position);
            yield return Wait(0.3f);
            detector.TryGetContact(enemies[0], out var last);
            int steps = heard.Count;
            Park(walker, new Vector3(9f, 0f, -10.5f));
            yield return Wait(0.6f);
            Assert.AreEqual(steps, heard.Count, "a standing zombie makes no footsteps");
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var held));
            Assert.AreEqual(last.Position, held.Position, "kept at the last detected step");
            Assert.IsTrue(view.TryGetMarker(enemies[0], out string shape, out _));
            Assert.AreEqual("heard", shape);
            yield return Wait(detector.DisplayTime(ContactKind.Heard));
            Assert.IsFalse(detector.TryGetContact(enemies[0], out _), "gone after the display time");

            // 10 m in the open (behind the player, not seen): heard. About 10 m behind one wall: not heard.
            heard.Clear();
            Walk(walker, new Vector3(-9f, 0f, -CorridorZ), new Vector3(-12f, 0f, -CorridorZ));
            yield return Wait(1.2f);
            Assert.Greater(heard.Count, 0, "10 m in the open is within hearing range");
            Park(walker, new Vector3(-12f, 0f, -CorridorZ));
            yield return Wait(detector.DisplayTime(ContactKind.Heard) + 0.2f);
            heard.Clear();
            Walk(walker, new Vector3(-9f, 0f, -10.5f), new Vector3(-12f, 0f, -10.5f));
            yield return Wait(1.2f);
            Assert.AreEqual(0, heard.Count, "about 10 m behind a wall: muffled below hearing range");
            Park(walker, new Vector3(-12f, 0f, -10.5f));

            // Upstairs, above the player: heard through the floor, shown as an arrow up.
            heard.Clear();
            Walk(walker, new Vector3(-2f, Upper, -CorridorZ), new Vector3(2f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Assert.Greater(heard.Count, 0, "heard through the floor slab");
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var up));
            Assert.AreEqual(1, up.Floor);
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[0], out shape, out _));
            Assert.That(shape, Is.EqualTo("heard_up").Or.EqualTo("self_heard_up"), "an arrow up (beside the player's arrow when straight above)");
        }

        [UnityTest]
        public IEnumerator Markers_BeyondTheRange_OnTheEdge_TowardTheLastDetectedPosition_AboveAndBelow_BesideTheArrow()
        {
            yield return LoadRuins();
            const float edge = 80f;
            // Seen 18 m ahead (beyond the 11 m range): a pointer on the edge, straight up (ahead).
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Park(zombies[0], new Vector3(18f, 0f, -CorridorZ));
            yield return Wait(0.3f);
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[0], out string shape, out Vector2 at));
            Assert.AreEqual("edge_seen", shape);
            Assert.AreEqual(0f, at.x, 1f);
            Assert.AreEqual(edge, at.y, 1f, "held on the edge, toward the enemy");
            var pointer = view.Frame.GetComponentsInChildren<Image>().First(i => i.enabled && i.name.StartsWith("Marker_"));
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, pointer.rectTransform.localEulerAngles.z), 1f, "pointing outward (up)");

            // Hidden and moved elsewhere: the pointer keeps the direction of the last detected position. Facing north,
            // that position (east) is to the right; the zombie's current place (front-right, in range) is not used.
            Vector3 lastSeen = zombies[0].transform.position;
            Park(zombies[0], new Vector3(8f, 0f, -10.5f));
            Look(new Vector3(0f, 0.05f, -CorridorZ), 0f);
            yield return Wait(0.3f);
            Assert.IsTrue(detector.TryGetContact(enemies[0], out var held));
            Assert.Less(Vector3.Distance(lastSeen, held.Position), 0.05f);
            Assert.IsTrue(view.TryGetMarker(enemies[0], out shape, out at));
            Assert.AreEqual("edge_seen", shape);
            Assert.AreEqual(edge, at.x, 1f, "to the right: toward where it was last detected");
            Assert.AreEqual(0f, at.y, 2f);
            yield return Wait(detector.DisplayTime(ContactKind.Seen));
            Assert.IsFalse(view.TryGetMarker(enemies[0], out _, out _), "gone after the display time");
            Assert.AreEqual(0, view.ShownMarkers);

            // Straight above (heard through the floor): a boxed arrow up beside the player's arrow, not under it.
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Walk(zombies[1], new Vector3(-0.9f, Upper, -CorridorZ), new Vector3(0.9f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Park(zombies[1], zombies[1].transform.position);
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[1], out shape, out at));
            Assert.AreEqual("self_heard_up", shape);
            Assert.Greater(at.magnitude, 20f, "beside the player's arrow, not on it");
            Assert.AreEqual(30f, at.x, 0.5f);

            // Straight below, seen from upstairs is impossible through the slab: heard walking below.
            Park(zombies[1], new Vector3(-16f, Upper, 15f));
            Look(new Vector3(0f, Upper + 0.05f, -CorridorZ), 90f);
            Walk(zombies[2], new Vector3(-0.9f, 0f, -CorridorZ), new Vector3(0.9f, 0f, -CorridorZ));
            yield return Wait(1.2f);
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[2], out shape, out at));
            Assert.AreEqual("self_heard_down", shape);
            Park(zombies[2], new Vector3(-14f, Upper, 15f));

            // Another floor and beyond the range: the edge pointer with an arrow down beside it.
            Look(new Vector3(2f, Upper + 0.05f, 8.7f), 180f, -19f);
            Park(zombies[3], new Vector3(2f, 0f, -6f));
            yield return Wait(0.4f);
            Assert.IsTrue(view.TryGetMarker(enemies[3], out shape, out at));
            Assert.AreEqual("edge_seen_down", shape);
            Assert.AreEqual(edge, at.y, 1f);
        }

        // ------------------------------------------------------------------ death, retry, setting

        [UnityTest]
        public IEnumerator Markers_GoAtDeath_AndOnRetry()
        {
            yield return LoadRuins();
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Park(zombies[0], new Vector3(8f, 0f, -CorridorZ));
            yield return Wait(0.3f);
            Assert.IsTrue(detector.TryGetContact(enemies[0], out _));
            zombies[0].ApplyDamage(10000f, zombies[0].transform.position, Vector3.forward);
            Assert.IsFalse(detector.TryGetContact(enemies[0], out _), "removed at once when it dies");
            yield return null;
            Assert.AreEqual(0, view.ShownMarkers);

            Park(zombies[1], new Vector3(8f, 0f, -CorridorZ - 1f));
            yield return Wait(0.3f);
            Assert.Greater(detector.Contacts.Count, 0);
            Object.FindAnyObjectByType<TrainingSession>().Retry();
            yield return null;
            yield return null;
            var fresh = Object.FindAnyObjectByType<EnemyDetector>();
            var freshView = Object.FindAnyObjectByType<MiniMapView>();
            Assert.AreNotSame(detector, fresh);
            Assert.AreEqual(0, freshView.ShownMarkers, "no marker carried over the retry");
            Assert.AreEqual(5, MapEnemy.All.Count, "only the new round's zombies");
            Assert.IsTrue(MapEnemy.All.All(e => e != null && !e.IsDead));
        }

        [UnityTest]
        public IEnumerator Setting_DefaultOn_OffHidesOnlyTheDisplay_SavedAcrossReload_OtherSettingsUntouched()
        {
            ControlSettings.TouchControlTransparency = 40;
            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Hold);
            yield return LoadRuins();
            Assert.IsFalse(PlayerPrefs.HasKey(MapSettings.MinimapVisibleKey));
            Assert.IsTrue(view.IsShown, "on by default");
            string layout = PlayerPrefs.GetString(TouchLayout.PrefsKey, "");

            var menu = Object.FindAnyObjectByType<GameMenu>();
            menu.OpenControlSettings();
            yield return null;
            var window = menu.transform.Find("MenuLayer/ControlSettingsPanel/Window");
            StringAssert.Contains("ON", window.Find("MinimapLabel").GetComponent<Text>().text);
            window.Find("MinimapOff").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(MapSettings.MinimapVisible);
            Assert.AreEqual(0, PlayerPrefs.GetInt(MapSettings.MinimapVisibleKey, 1), "saved");
            StringAssert.Contains("OFF", window.Find("MinimapLabel").GetComponent<Text>().text);
            menu.CloseControlSettings();
            menu.Close();
            yield return null;
            Assert.IsFalse(view.IsShown, "hidden");
            Assert.AreEqual(40, ControlSettings.TouchControlTransparency, "transparency kept");
            Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Touch), "ADS mode kept");
            Assert.AreEqual(layout, PlayerPrefs.GetString(TouchLayout.PrefsKey, ""), "touch layout kept");

            // Detection and the zombies carry on as before (display only).
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Park(zombies[0], new Vector3(8f, 0f, -CorridorZ));
            yield return Wait(0.3f);
            Assert.IsTrue(detector.TryGetContact(enemies[0], out _), "detection still runs");
            zombies[0].Active = true;
            zombies[0].GetComponent<NavMeshAgent>().isStopped = false;
            yield return Wait(0.6f);
            Assert.AreEqual(ZombieState.Chase, zombies[0].State, "the zombie chases as usual");

            // A new load (as after a restart) reads the saved choice.
            yield return LoadRuins();
            Assert.IsFalse(view.IsShown, "off is restored");
            MapSettings.MinimapVisible = true;
            yield return null;
            Assert.IsTrue(view.IsShown);
            yield return LoadRuins();
            Assert.IsTrue(view.IsShown, "on is restored");
        }

        [UnityTest]
        public IEnumerator Frame_IsPlacedAsTheLayoutSays()
        {
            yield return LoadRuins();
            var frame = view.Frame;
            Assert.AreEqual(new Vector2(0f, 1f), frame.anchorMin);
            Assert.AreEqual(new Vector2(MiniMapView.Margin.x, -MiniMapView.Margin.y), frame.anchoredPosition);
            Assert.AreEqual(new Vector2(MiniMapView.Size, MiniMapView.Size), frame.sizeDelta);
            Assert.IsNotNull(frame.parent.GetComponent<SafeAreaFitter>(), "inside the Safe Area (notch)");
            foreach (var g in frame.GetComponentsInChildren<Graphic>())
                Assert.IsFalse(g.raycastTarget, $"{g.name}: display only, never takes a touch");
        }
    }
}
