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
            savedMapLayout = PlayerPrefs.GetString(MapSettings.MinimapLayoutKey, null);
            MapSettings.ClearSaved();
        }

        public override void TearDown()
        {
            MapSettings.ClearSaved();
            if (hadMinimapKey)
                PlayerPrefs.SetInt(MapSettings.MinimapVisibleKey, savedMinimap);
            if (!string.IsNullOrEmpty(savedMapLayout))
                PlayerPrefs.SetString(MapSettings.MinimapLayoutKey, savedMapLayout);
            base.TearDown();
        }

        string savedMapLayout;

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
            void Stairs(int floor, float x, float z, string what) =>
                Assert.IsTrue(FloorMapData.IsStairs(map.CellAt(floor, new Vector3(x, 0f, z))), $"{what} ({x}, {z}) floor {floor + 1}");
            Stairs(0, 19.35f, -2f, "east stair");
            Stairs(1, 19.35f, -2f, "east stair (both floors)");
            Stairs(0, -19.35f, 2f, "west stair");
            // Boundary lines: drawn across the end that leads to the other floor, never across the way on / off this
            // floor (east ramp: bottom z -7 on the ground, top z 1.66 at the upper floor; west mirrored).
            Cell(0, 19.35f, 1.5f, MapCell.StairsEnd, "east stair, 1F: boundary at the top (to 2F)");
            Cell(1, 19.35f, -6.85f, MapCell.StairsEnd, "east stair, 2F: boundary at the bottom (to 1F)");
            Cell(0, -19.35f, -1.5f, MapCell.StairsEnd, "west stair, 1F: boundary at the top");
            Cell(1, -19.35f, 6.85f, MapCell.StairsEnd, "west stair, 2F: boundary at the bottom");
            foreach (var (floor, z, what) in new[] { (0, -6.9f, "east stair, 1F: way on from the ground"), (1, 1.55f, "east stair, 2F: way on from the upper floor") })
                for (float x = 17.8f; x < 21f; x += 0.25f)
                {
                    var c = map.CellAt(floor, new Vector3(x, 0f, z));
                    Assert.AreNotEqual(MapCell.StairsEnd, c, $"{what}: no boundary line ({x})");
                    Assert.AreNotEqual(MapCell.StairsStep, c, $"{what}: no step line ({x})");
                }
            int steps = 0;
            for (float z = -7f; z < 1.6f; z += 0.25f)
                if (map.CellAt(0, new Vector3(19.35f, 0f, z)) == MapCell.StairsStep)
                    steps++;
            Assert.Greater(steps, 5, "step lines along the ramp");
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

            // Upstairs, above the player: heard through the floor as a footstep cue only (no position on the map).
            heard.Clear();
            var cues = new List<FootstepCue>();
            detector.CueHeard += cues.Add;
            Walk(walker, new Vector3(-2f, Upper, -CorridorZ), new Vector3(2f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Assert.Greater(cues.Count, 0, "heard through the floor slab");
            Assert.AreEqual(0, heard.Count, "not a heard marker");
            Assert.IsTrue(cues.All(c => c.Vertical == 1), "above");
            Assert.IsFalse(detector.TryGetContact(enemies[0], out _));
            yield return null;
            Assert.IsFalse(view.TryGetMarker(enemies[0], out _, out _), "no marker for an unseen enemy on another floor");
            Assert.Greater(view.ShownCues, 0);
        }

        [UnityTest]
        public IEnumerator Markers_BeyondTheRange_OnTheEdge_TowardTheLastDetectedPosition_AboveAndBelow_BesideTheArrow()
        {
            yield return LoadRuins();
            Vector2 edgeHalf = MiniMapView.EdgeHalf;
            float edge = edgeHalf.y;
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
            Assert.AreEqual(edgeHalf.x, at.x, 1f, "to the right: toward where it was last detected (the map is wider than tall)");
            Assert.AreEqual(0f, at.y, 2f);
            yield return Wait(detector.DisplayTime(ContactKind.Seen));
            Assert.IsFalse(view.TryGetMarker(enemies[0], out _, out _), "gone after the display time");
            Assert.AreEqual(0, view.ShownMarkers);

            // Straight above (heard through the floor): no marker at all, only an "above" footstep cue beside the label.
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Walk(zombies[1], new Vector3(-0.9f, Upper, -CorridorZ), new Vector3(0.9f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Park(zombies[1], zombies[1].transform.position);
            yield return null;
            Assert.IsFalse(view.TryGetMarker(enemies[1], out _, out _), "straight above: no marker");
            CollectionAssert.Contains(view.CueShapesShown(), "step_up_overhead");

            // Straight below (from upstairs): likewise, a "below" cue.
            Park(zombies[1], new Vector3(-16f, Upper, 15f));
            Look(new Vector3(0f, Upper + 0.05f, -CorridorZ), 90f);
            Walk(zombies[2], new Vector3(-0.9f, 0f, -CorridorZ), new Vector3(0.9f, 0f, -CorridorZ));
            yield return Wait(1.2f);
            yield return null;
            Assert.IsFalse(view.TryGetMarker(enemies[2], out _, out _), "straight below: no marker");
            CollectionAssert.Contains(view.CueShapesShown(), "step_down_overhead");
            Park(zombies[2], new Vector3(-14f, Upper, 15f));

            // Another floor and beyond the range: the edge pointer with an arrow down beside it.
            Look(new Vector3(2f, Upper + 0.05f, 8.7f), 180f, -19f);
            Park(zombies[3], new Vector3(2f, 0f, -6f));
            yield return Wait(0.4f);
            Assert.IsTrue(view.TryGetMarker(enemies[3], out shape, out at));
            Assert.AreEqual("edge_seen_down", shape);
            Assert.AreEqual(edge, at.y, 1f);
        }

        // ------------------------------------------------------------------ another floor: footstep cues only

        [UnityTest]
        public IEnumerator OtherFloor_Unseen_NoMarkerAtAnyDistance_FootstepCueOnlyWhileWalking_RoughDirection_GoesAfterTheDisplayTime()
        {
            yield return LoadRuins();
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f); // ground floor, south corridor, facing east
            var walker = zombies[0];
            var cues = new List<FootstepCue>();
            detector.CueHeard += cues.Add;

            // Standing upstairs, straight above: nothing at all.
            Park(walker, new Vector3(0.3f, Upper, -CorridorZ));
            yield return Wait(1f);
            Assert.AreEqual(0, cues.Count, "a standing zombie makes no footsteps");
            Assert.IsFalse(detector.TryGetContact(enemies[0], out _));
            Assert.AreEqual(0, view.ShownMarkers);
            Assert.AreEqual(0, view.ShownCues);

            // Walking upstairs 5 m ahead: a cue (above, about east), never a marker; the cue does not follow the zombie.
            Walk(walker, new Vector3(4f, Upper, -CorridorZ), new Vector3(6f, Upper, -CorridorZ));
            Face(walker, 90f); // already turned the way it walks (east)
            for (float end = Time.time + 1.2f; Time.time < end;)
            {
                yield return null;
                Assert.IsFalse(view.TryGetMarker(enemies[0], out string s, out _), $"no marker for an unseen enemy upstairs ({s})");
            }
            Assert.Greater(cues.Count, 0, "heard through the floor nearby");
            var cue = cues.Last();
            Assert.AreEqual(1, cue.Vertical);
            Assert.AreEqual(1f, cue.Direction.x, 1e-3f, "rounded to one of 8 directions (east)");
            Assert.AreEqual(0f, cue.Direction.y, 1e-3f);
            CollectionAssert.Contains(view.CueShapesShown(), "step_up");
            Assert.AreEqual(1f, cue.Heading.x, 1e-3f, "facing east as it walks east (rounded to 8 ways)");
            AssertAngle(view.FrameAngle(cue.Heading), view.CueAngle(0), "the footprint's toes point where it faced");
            AssertAngle(0f, view.CueAngle(0), "east is ahead of the player: toes up");
            Assert.AreEqual("MarkerFootprint", view.Frame.Find("Background/Viewport/Markers/Cue_0").GetComponent<Image>().sprite.texture.name);
            Vector2 shownAt = view.CueFramePosition(0);
            Assert.AreEqual(MiniMapView.EdgeHalf.y, shownAt.y, 1f, "on the map's edge (no distance), ahead");
            Assert.AreEqual(0f, shownAt.x, 1f);

            // Stops: no new cue; the shown one goes after the display time.
            Park(walker, walker.transform.position);
            yield return Wait(0.3f);
            int count = cues.Count;
            yield return Wait(detector.CueDisplayTime + 0.2f);
            Assert.AreEqual(count, cues.Count, "no footsteps while standing");
            Assert.AreEqual(0, detector.Cues.Count);
            Assert.AreEqual(0, view.ShownCues, "gone after the display time");

            // Out of range: walking upstairs about 14 m away (through the floor): nothing.
            cues.Clear();
            Walk(walker, new Vector3(13f, Upper, -CorridorZ), new Vector3(15f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Assert.AreEqual(0, cues.Count, "beyond the hearing range through the floor");
            Assert.AreEqual(0, view.ShownCues);
            Assert.IsFalse(view.TryGetMarker(enemies[0], out _, out _));
            Park(walker, new Vector3(-16f, Upper, 15f));
        }

        static void AssertAngle(float expected, float actual, string why) =>
            Assert.AreEqual(0f, Mathf.DeltaAngle(expected, actual), 1f, $"{why}: {actual:0.#} (expected {expected:0.#})");

        static void Face(Zombie z, float yaw)
        {
            z.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Markers_RedTriangleTipAndFootprintToes_PointWhereEachEnemyFaced_KeptWhileHidden()
        {
            yield return LoadRuins();
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f); // south corridor, looking east (east = up on the map)
            // Three seen zombies ahead, each facing another way.
            var faces = new[] { 0f, 270f, 135f }; // north, west (toward the player), south-east
            for (int i = 0; i < 3; i++)
            {
                Park(zombies[i], new Vector3(3f + 2f * i, 0f, -CorridorZ + (i - 1) * 0.8f)); // within the map's range
                Face(zombies[i], faces[i]);
            }
            yield return Wait(0.4f);
            var angles = new List<float>();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(view.TryGetMarker(enemies[i], out string shape, out _), $"{zombies[i].name} seen");
                Assert.AreEqual("seen", shape);
                Assert.IsTrue(view.TryGetMarkerAngle(enemies[i], out float angle));
                Vector2 facing = new Vector2(Mathf.Sin(faces[i] * Mathf.Deg2Rad), Mathf.Cos(faces[i] * Mathf.Deg2Rad));
                AssertAngle(view.FrameAngle(facing), angle, $"{zombies[i].name}: the tip points where it faces");
                angles.Add(angle);
            }
            // On this heading-up map (east up): north = left, west = down (toward the player), south-east = up-right.
            AssertAngle(90f, angles[0], "north: tip left");
            AssertAngle(180f, angles[1], "west: tip down, toward the player's arrow");
            AssertAngle(-45f, angles[2], "south-east: tip up-right");
            var marker = view.Frame.GetComponentsInChildren<Image>().First(m => m.enabled && m.name.StartsWith("Marker_"));
            Assert.AreEqual("MarkerFacing", marker.sprite.texture.name, "a triangle (not a dot)");
            Assert.Greater(marker.color.r, 0.9f, "red");
            Assert.Less(marker.color.g, 0.4f);

            // While seen, the tip follows its turn; once hidden, it keeps the last seen facing.
            Face(zombies[0], 90f);
            yield return Wait(0.3f);
            Assert.IsTrue(view.TryGetMarkerAngle(enemies[0], out float turned));
            AssertAngle(0f, turned, "turned east (away from the player): tip up");
            Park(zombies[0], new Vector3(8f, 0f, -10.5f)); // behind the ring wall
            Face(zombies[0], 180f);
            yield return Wait(0.3f);
            Assert.IsTrue(view.TryGetMarkerAngle(enemies[0], out float held));
            AssertAngle(0f, held, "hidden: the last seen facing, not where it turned");

            // Heard behind the wall: a footprint whose toes point the way it walks.
            for (int i = 0; i < 3; i++)
                Park(zombies[i], new Vector3(-16f + i * 1.5f, Upper, 15.15f));
            yield return Wait(detector.DisplayTime(ContactKind.Seen) + 0.2f);
            Walk(zombies[1], new Vector3(4f, 0f, -10.5f), new Vector3(9f, 0f, -10.5f));
            Face(zombies[1], 90f); // already turned the way it walks
            yield return Wait(1.4f);
            Assert.IsTrue(detector.TryGetContact(enemies[1], out var heard));
            Assert.AreEqual(ContactKind.Heard, heard.Kind);
            Assert.Less(Vector2.Angle(Vector2.right, heard.Facing), 25f, "faced east, the way it walked");
            Assert.IsTrue(view.TryGetMarker(enemies[1], out string heardShape, out _));
            Assert.AreEqual("heard", heardShape);
            Assert.IsTrue(view.TryGetMarkerAngle(enemies[1], out float toes));
            AssertAngle(view.FrameAngle(heard.Facing), toes, "toes where it faced at the step");
            var foot = view.Frame.GetComponentsInChildren<Image>().First(m => m.enabled && m.name.StartsWith("Marker_"));
            Assert.AreEqual("MarkerFootprint", foot.sprite.texture.name);
        }

        [UnityTest]
        public IEnumerator FloorChange_AndStairs_NeverFlashAnUnseenEnemysPlace()
        {
            yield return LoadRuins();
            // Heard behind the ring wall on the ground floor: a ring at its place.
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Walk(zombies[0], new Vector3(4f, 0f, -10.5f), new Vector3(9f, 0f, -10.5f));
            yield return Wait(1.2f);
            Park(zombies[0], zombies[0].transform.position);
            yield return null;
            Assert.IsTrue(view.TryGetMarker(enemies[0], out string shape, out _));
            Assert.AreEqual("heard", shape);
            // The player goes upstairs: on the first frame the map shows the upper floor, the ring is gone.
            Look(new Vector3(0f, Upper + 0.05f, -CorridorZ), 90f);
            yield return null;
            Assert.AreEqual(1, view.DisplayedFloor);
            Assert.IsFalse(view.TryGetMarker(enemies[0], out shape, out _), $"not shown on the other floor's map ({shape})");
            Park(zombies[0], new Vector3(-16f, Upper, 15f));
            yield return Wait(detector.DisplayTime(ContactKind.Heard) + 0.2f);

            // Up the east stair tunnel (walked by moving the player) while one zombie walks on the ground floor beside
            // the stair and one on the upper floor ahead: every frame, any marker of an enemy that is not seen is a
            // same-floor marker of the floor shown (never an up / down marker).
            Look(new Vector3(19.35f, 0.05f, -9.2f), 0f);
            Walk(zombies[1], new Vector3(15f, 0f, -5f), new Vector3(15f, 0f, 2f));
            Walk(zombies[2], new Vector3(15f, Upper, 6f), new Vector3(15f, Upper, -2f));
            float run = Upper / Mathf.Tan(30f * Mathf.Deg2Rad);
            int frames = 0;
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                float z = -7f + run * t;
                Look(new Vector3(19.35f, Upper * t + 0.05f, z), 0f);
                yield return null;
                frames++;
                for (int i = 1; i <= 2; i++)
                {
                    if (!view.TryGetMarker(enemies[i], out shape, out _))
                        continue;
                    bool seen = detector.TryGetContact(enemies[i], out var c) && c.Kind == ContactKind.Seen;
                    if (!seen)
                    {
                        Assert.That(shape, Is.EqualTo("heard").Or.EqualTo("edge_heard"), $"frame {frames}: an unseen enemy shows only as a same-floor ring");
                        Assert.AreEqual(view.DisplayedFloor, c.Floor, $"frame {frames}: on the floor shown");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator Cues_GoAtDeathClearAndRetry()
        {
            yield return LoadRuins();
            var session = Object.FindAnyObjectByType<TrainingSession>();
            // Death with a cue showing (a zombie walking upstairs, above the player).
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Walk(zombies[0], new Vector3(-1f, Upper, -CorridorZ), new Vector3(2f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Assert.Greater(view.ShownCues, 0);
            player.GetComponent<Deadlock.Player.PlayerHealth>().TakeDamage(10000f, player.transform.position);
            yield return null;
            yield return null;
            Assert.AreEqual(TrainingResult.Failed, session.Result);
            Assert.AreEqual(0, detector.Cues.Count);
            Assert.AreEqual(0, view.ShownCues, "no cue stays after death");
            Assert.AreEqual(0, view.ShownMarkers);

            // Retry: none carried over. Cleared (every zombie defeated) with a cue showing: none stays either.
            session.Retry();
            yield return null;
            yield return null;
            yield return LoadRuins();
            session = Object.FindAnyObjectByType<TrainingSession>();
            Assert.AreEqual(0, view.ShownCues, "none carried over the retry");
            Look(new Vector3(0f, 0.05f, -CorridorZ), 90f);
            Walk(zombies[0], new Vector3(-1f, Upper, -CorridorZ), new Vector3(2f, Upper, -CorridorZ));
            yield return Wait(1.2f);
            Assert.Greater(view.ShownCues, 0);
            foreach (var z in zombies.Skip(1))
                z.ApplyDamage(z.MaxHealth, z.transform.position, Vector3.forward);
            zombies[0].ApplyDamage(zombies[0].MaxHealth, zombies[0].transform.position, Vector3.forward);
            yield return null;
            yield return null;
            Assert.AreEqual(TrainingResult.Cleared, session.Result, "the ruins end as before: every zombie defeated");
            Assert.AreEqual(0, view.ShownCues, "no cue stays after the result");
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
        public IEnumerator LayoutEditor_MovesAndResizesTheMap_SavedRestored_KeptInside_NotMovedByTheGame()
        {
            yield return LoadRuins();
            MapSettings.MinimapVisible = false; // the preview shows even when the map is switched off
            yield return null;
            Assert.IsFalse(view.IsShown);
            var menu = Object.FindAnyObjectByType<GameMenu>();
            var editor = menu.GetComponent<TouchLayoutEditor>();
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(view.IsShown && view.IsEditing, "shown for editing");
            Assert.IsTrue(view.Frame.Find("EditPreview").gameObject.activeSelf, "sample markers while editing");
            Assert.IsTrue(menu.transform.Find("LayoutEditorLayer/Handles/Handle_minimap").gameObject.activeSelf, "a handle on the map");

            var start = view.Placement;
            Assert.IsTrue(editor.MoveBy(MiniMapView.LayoutId, new Vector2(400f, -250f)));
            Assert.IsTrue(editor.Resize(MiniMapView.LayoutId, +1));
            Assert.IsTrue(editor.Resize(MiniMapView.LayoutId, +1));
            yield return null;
            var moved = view.Placement;
            Assert.Greater(moved.center.x, start.center.x, "moved right");
            Assert.Less(moved.center.y, start.center.y, "moved down");
            Assert.AreEqual(1.2f, moved.scale, 0.001f, "bigger");
            Assert.AreEqual(moved.scale, view.Frame.localScale.x, 0.001f);
            Assert.AreEqual(view.Frame.localScale.x, view.Frame.localScale.y, 0.001f, "aspect kept");
            editor.ApplyAndClose();
            yield return null;
            Assert.IsTrue(view.PlacementSaved);
            Assert.IsTrue(MapSettings.TryLoadPlacement(out var saved));
            Assert.AreEqual(moved.center.x, saved.center.x, 1e-4f);
            Assert.AreEqual(moved.scale, saved.scale, 1e-4f);
            Assert.IsFalse(view.IsShown, "still switched off after editing");
            Assert.IsFalse(MapSettings.MinimapVisible);
            var buttons = TouchLayout.Load();
            var defaults = TouchLayout.Defaults();
            foreach (var item in defaults.items)
                Assert.AreEqual(item.center, buttons.Get(item.id).center, $"{item.id}: buttons not moved by the map");

            // Restored after a reload, even with a control moved onto the map (the game never moves a saved map).
            MapSettings.MinimapVisible = true;
            var layout = TouchLayout.Defaults();
            var weapon = layout.Get(TouchLayout.Weapon);
            weapon.center = saved.center;
            layout.Set(weapon);
            TouchLayout.Save(layout);
            yield return LoadRuins();
            Assert.IsTrue(view.PlacementSaved);
            Assert.AreEqual(saved.center.x, view.Placement.center.x, 1e-4f);
            Assert.AreEqual(saved.center.y, view.Placement.center.y, 1e-4f);
            Assert.AreEqual(saved.scale, view.Frame.localScale.x, 1e-4f);
            TouchLayout.ClearSaved();
            yield return LoadRuins();

            // Never fully off screen: dragged far away, it stays inside the Safe Area.
            menu = Object.FindAnyObjectByType<GameMenu>();
            editor = menu.GetComponent<TouchLayoutEditor>();
            menu.OpenLayoutEditor();
            yield return null;
            editor.MoveBy(MiniMapView.LayoutId, new Vector2(-100000f, 100000f));
            for (int i = 0; i < 10; i++)
                editor.Resize(MiniMapView.LayoutId, +1);
            yield return null;
            Rect inside = MiniMapView.PlacementRect(view.Placement, view.SafeSize);
            Assert.GreaterOrEqual(inside.xMin, -0.01f);
            Assert.LessOrEqual(inside.yMax, view.SafeSize.y + 0.01f);
            Assert.AreEqual(MiniMapView.MaxScale, view.Placement.scale, 1e-4f);
            // Cancel: back to the saved place.
            editor.CancelAndClose();
            yield return null;
            Assert.AreEqual(saved.center.x, view.Placement.center.x, 1e-4f);
            Assert.AreEqual(saved.scale, view.Placement.scale, 1e-4f);

            // Reset + apply: automatic again (nothing saved).
            menu.OpenLayoutEditor();
            yield return null;
            editor.ResetToDefaults();
            editor.ApplyAndClose();
            yield return null;
            Assert.IsFalse(view.PlacementSaved);
            Assert.IsFalse(MapSettings.TryLoadPlacement(out _));
            var auto = MiniMapView.AutoPlacement(view.SafeSize, TouchLayout.Defaults());
            Assert.AreEqual(auto.center.x, view.Placement.center.x, 1e-4f);
        }

        [UnityTest]
        public IEnumerator PerfLog_FollowsTheRealPlayer_MovementShotsAndSwings_AlsoAfterRetry()
        {
            yield return LoadRuins();
            var lines = new List<string>();
            var placesAtLine = new List<Vector3>(); // the tracked player's place when each line was written
            var monitor = FrameRateMonitor.Attach();
            monitor.LogInterval = 1f;
            monitor.Logged += l =>
            {
                lines.Add(l);
                placesAtLine.Add(monitor.TrackedPlayer != null ? monitor.TrackedPlayer.position : Vector3.zero);
            };
            try
            {
                Assert.AreSame(player.transform, monitor.TrackedPlayer, "the player rig (not another object named Player)");
                yield return WaitRealtime(2.3f); // the load window
                Look(new Vector3(-18f, 0.05f, -CorridorZ), 90f);
                lines.Clear();
                placesAtLine.Clear();
                Press(keyboard.wKey, queueEventOnly: true);
                Press(mouse.leftButton, queueEventOnly: true);
                yield return Wait(1.5f);
                Release(mouse.leftButton, queueEventOnly: true);
                Release(keyboard.wKey, queueEventOnly: true);
                yield return Tap(keyboard.digit2Key);
                yield return Wait(weapons.SwitchTime + 0.3f);
                yield return Tap(mouse.leftButton);
                yield return WaitRealtime(2.5f);
                var perf = lines.Where(l => l.StartsWith("[DEADLOCK][perf] ")).ToList();
                Assert.GreaterOrEqual(perf.Count, 2);
                float moved = 0f;
                int shots = 0, swings = 0;
                foreach (var l in perf)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(l, @"moved ([\d.]+) m, shots (\d+), melee (\d+)");
                    Assert.IsTrue(m.Success, l);
                    moved += float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    shots += int.Parse(m.Groups[2].Value);
                    swings += int.Parse(m.Groups[3].Value);
                }
                Assert.Greater(moved, 2f, "walked about 1.5 s");
                Assert.Greater(shots, 0, "shots counted");
                Assert.AreEqual(1, swings, "one knife swing");
                // The place on each line is the real player's place at the moment the line was written.
                int checkedLines = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (!lines[i].StartsWith("[DEADLOCK][perf] "))
                        continue;
                    var at = System.Text.RegularExpressions.Regex.Match(lines[i], @"Proto_Ruins \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)$");
                    Assert.IsTrue(at.Success, lines[i]);
                    Assert.AreEqual(placesAtLine[i].x, float.Parse(at.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0.06f, lines[i]);
                    Assert.AreEqual(placesAtLine[i].z, float.Parse(at.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), 0.06f, lines[i]);
                    checkedLines++;
                }
                Assert.GreaterOrEqual(checkedLines, 2);

                // Retry reloads the scene: the new player is tracked.
                lines.Clear();
                placesAtLine.Clear();
                Object.FindAnyObjectByType<TrainingSession>().Retry();
                yield return null;
                yield return null;
                var fresh = Object.FindAnyObjectByType<Deadlock.Player.PlayerRig>();
                StringAssert.Contains($"tracking Player #{fresh.GetInstanceID()}", string.Join("\n", lines));
                Assert.AreSame(fresh.transform, monitor.TrackedPlayer, "the new round's player");
                var controller = fresh.GetComponent<CharacterController>();
                controller.enabled = false;
                fresh.transform.position = new Vector3(5f, 0.05f, -CorridorZ);
                controller.enabled = true;
                yield return WaitRealtime(3.6f);
                var after = lines.Where(l => l.StartsWith("[DEADLOCK][perf] ")).ToList();
                Assert.Greater(after.Count, 0);
                StringAssert.EndsWith($"Proto_Ruins (5.0, 0.0, {-CorridorZ:0.0})", after[after.Count - 1], "the new player's place");
            }
            finally
            {
                Object.Destroy(monitor.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator SavedPlace_FromTheNarrowerMap_ShownInsideTheSafeArea_SavedValueNotRewritten()
        {
            // A place saved with the earlier 4:3 map right at the edge: the wider map is moved in only as far as needed,
            // for display; the saved value stays as the player left it.
            var saved = new MiniMapPlacement { center = new Vector2(0.95f, 0.9f), scale = 1.3f };
            MapSettings.SavePlacement(saved);
            yield return LoadRuins();
            yield return null;
            Rect r = MiniMapView.PlacementRect(view.Placement, view.SafeSize);
            Assert.LessOrEqual(r.xMax, view.SafeSize.x + 0.01f, "inside on the right");
            Assert.AreEqual(view.SafeSize.x, r.xMax, 0.01f, "moved in just to the edge, no further");
            Assert.LessOrEqual(r.yMax, view.SafeSize.y + 0.01f);
            Assert.AreEqual(1.3f, view.Placement.scale, 1e-4f, "size kept");
            Assert.AreEqual(MiniMapView.BaseSize * 1.3f, r.size, "3:2 at the saved size");
            Assert.IsTrue(MapSettings.TryLoadPlacement(out var still));
            Assert.AreEqual(saved.center.x, still.center.x, 1e-6f, "the saved value is not rewritten");
            Assert.AreEqual(saved.center.y, still.center.y, 1e-6f);
            Assert.AreEqual(saved.scale, still.scale, 1e-6f);
        }

        // The touch layout saved on the test iPhone before the integration build (2026-10-22): FIRE / ADS on the right,
        // the weapon button in the top-left corner.
        const string DeviceLayout = "{\"version\":1,\"items\":[{\"id\":\"move_zone\",\"center\":{\"x\":0.2,\"y\":0.4},\"size\":{\"x\":0.4,\"y\":0.8},\"scale\":1.0},"
            + "{\"id\":\"move_stick\",\"center\":{\"x\":0.1086,\"y\":0.2765},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.0},"
            + "{\"id\":\"fire\",\"center\":{\"x\":0.9166,\"y\":0.2392},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.4},"
            + "{\"id\":\"ads\",\"center\":{\"x\":0.9444,\"y\":0.8835},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.4},"
            + "{\"id\":\"reload\",\"center\":{\"x\":0.7399,\"y\":0.3679},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.0},"
            + "{\"id\":\"jump\",\"center\":{\"x\":0.7350,\"y\":0.0752},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.1},"
            + "{\"id\":\"posture\",\"center\":{\"x\":0.6402,\"y\":0.2361},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.2},"
            + "{\"id\":\"weapon\",\"center\":{\"x\":0.0359,\"y\":0.8548},\"size\":{\"x\":0.0,\"y\":0.0},\"scale\":1.1}]}";
        static readonly MiniMapPlacement DevicePlace = new MiniMapPlacement { center = new Vector2(0.7299997806549072f, 0.8867603540420532f), scale = 1.3000000715255738f };

        /// <summary>Screen rectangle of the text actually drawn (from the text's generated glyphs), in the map's Safe Area units.</summary>
        static Rect DrawnText(Text text, MiniMapView map)
        {
            var verts = text.cachedTextGenerator.verts;
            Assert.Greater(verts.Count, 0, $"{text.name} draws something");
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            var safe = (RectTransform)map.Frame.parent;
            foreach (var v in verts)
            {
                Vector3 world = text.rectTransform.TransformPoint(v.position / text.pixelsPerUnit);
                Vector2 local = (Vector2)safe.InverseTransformPoint(world) + safe.rect.size * safe.pivot;
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static Rect FrameOnScreen(MiniMapView map, RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var safe = (RectTransform)map.Frame.parent;
            Vector2 a = (Vector2)safe.InverseTransformPoint(corners[0]) + safe.rect.size * safe.pivot;
            Vector2 b = (Vector2)safe.InverseTransformPoint(corners[2]) + safe.rect.size * safe.pivot;
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        void AssertClearOfHudAndControls(string why)
        {
            Rect map = FrameOnScreen(view, view.Frame);
            Assert.AreEqual(view.ShownRect.center.x, map.center.x, 0.5f, $"{why}: drawn where it says");
            Assert.GreaterOrEqual(map.xMin, -0.5f, why);
            Assert.GreaterOrEqual(map.yMin, -0.5f, why);
            Assert.LessOrEqual(map.xMax, view.SafeSize.x + 0.5f, why);
            Assert.LessOrEqual(map.yMax, view.SafeSize.y + 0.5f, why);
            var hud = Object.FindAnyObjectByType<HudController>();
            var texts = new List<Text> { hud.AmmoText, hud.StanceText, Object.FindAnyObjectByType<TrainingHud>().StatusText };
            foreach (var t in texts)
                Assert.IsFalse(map.Overlaps(DrawnText(t, view)), $"{why}: map over the text '{t.text}'");
            var menu = Object.FindAnyObjectByType<GameMenu>();
            Assert.IsFalse(map.Overlaps(FrameOnScreen(view, (RectTransform)menu.MenuButton.transform)), $"{why}: map over the menu button");
            var applier = Object.FindAnyObjectByType<TouchLayoutApplier>();
            foreach (string id in TouchLayout.Circles)
            {
                Rect w = FrameOnScreen(view, applier.Widget(id));
                Vector2 c = w.center;
                float r = w.width * 0.5f;
                Vector2 nearest = new Vector2(Mathf.Clamp(c.x, map.xMin, map.xMax), Mathf.Clamp(c.y, map.yMin, map.yMax));
                Assert.GreaterOrEqual(Vector2.Distance(nearest, c), r - 0.5f, $"{why}: map over {id}");
            }
        }

        IEnumerator LoadWithDevicePlace()
        {
            TouchLayout.Save(JsonUtility.FromJson<TouchLayoutData>(DeviceLayout));
            MapSettings.SavePlacement(DevicePlace);
            yield return LoadScene(InputMode.Touch, Ruins);
            view = Object.FindAnyObjectByType<MiniMapView>();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavedPlace_ShownClearOfHudTextAndControls_SavedValueKept_BackToTheSavedPlaceWhenRoomReturns()
        {
            yield return LoadWithDevicePlace();
            Debug.Log($"[DEADLOCK][minimap] safe {view.SafeSize} asked {MiniMapView.PlacementRect(view.Placement, view.SafeSize)} shown {view.ShownRect} failed {view.AvoidanceFailed}");
            Assert.IsFalse(view.AvoidanceFailed, "a clear place exists");
            AssertClearOfHudAndControls("saved place");
            Assert.AreEqual(1.3f, view.Placement.scale, 1e-4f, "size kept");
            Assert.AreEqual(MiniMapView.BaseSize.x * 1.3f, view.ShownRect.width, 0.01f, "not shrunk");
            Assert.AreEqual(MiniMapView.BaseSize.y * 1.3f, view.ShownRect.height, 0.01f, "not shrunk");
            Assert.IsTrue(MapSettings.TryLoadPlacement(out var saved));
            Assert.AreEqual(DevicePlace.center, saved.center, "saved value not rewritten");
            Assert.AreEqual(DevicePlace.scale, saved.scale);

            // Not moved again while nothing changes (no per-frame movement).
            Vector2 before = view.Frame.anchoredPosition;
            yield return Wait(0.5f);
            Assert.AreEqual(before, view.Frame.anchoredPosition);

            // A narrower screen (the Safe Area squeezed from both sides), then back: worked out from the saved place each time.
            var fitters = Object.FindObjectsByType<SafeAreaFitter>(FindObjectsSortMode.None);
            foreach (var f in fitters)
            {
                f.enabled = false;
                var r = (RectTransform)f.transform;
                r.anchorMin = new Vector2(0.12f, 0f);
                r.anchorMax = new Vector2(0.88f, 1f);
            }
            yield return null;
            yield return null;
            AssertClearOfHudAndControls("narrower screen");
            Assert.IsTrue(MapSettings.TryLoadPlacement(out saved));
            Assert.AreEqual(DevicePlace.center, saved.center, "a screen change never saves");
            foreach (var f in fitters)
                f.enabled = true;
            yield return null;
            yield return null;
            Assert.AreEqual(before, view.Frame.anchoredPosition, "back on the first screen: shown where it was");
        }

        [UnityTest]
        public IEnumerator Editing_HandleFollowsTheShownMap_ApplySavesTheAskedPlace_CancelKeepsTheSaved()
        {
            yield return LoadWithDevicePlace();
            var menu = Object.FindAnyObjectByType<GameMenu>();
            var editor = menu.GetComponent<TouchLayoutEditor>();
            menu.OpenLayoutEditor();
            yield return null;
            yield return null;
            var handle = (RectTransform)menu.transform.Find("LayoutEditorLayer/Handles/Handle_minimap");
            Rect Handle() => FrameOnScreen(view, handle);
            Rect Map() => FrameOnScreen(view, view.Frame);
            Assert.AreEqual(Map().center.x, Handle().center.x, 1f, "the handle is on the map as shown");
            Assert.AreEqual(Map().width, Handle().width, 1f);

            // Drag a little: the shown map stays clear of the HUD, the handle stays on it.
            Assert.IsTrue(editor.MoveBy(MiniMapView.LayoutId, new Vector2(-60f, 40f)));
            yield return null;
            AssertClearOfHudAndControls("while editing");
            Assert.AreEqual(Map().center.x, Handle().center.x, 1f);
            Assert.AreEqual(Map().center.y, Handle().center.y, 1f);
            var asked = editor.MapWorking;

            // Cancel: the saved place is kept.
            editor.CancelAndClose();
            yield return null;
            Assert.IsTrue(MapSettings.TryLoadPlacement(out var saved));
            Assert.AreEqual(DevicePlace.center, saved.center, "cancel keeps the saved place");

            // Apply: the asked place (not the shifted one) is saved.
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.MoveBy(MiniMapView.LayoutId, new Vector2(-60f, 40f)));
            asked = editor.MapWorking;
            editor.ApplyAndClose();
            menu.Close();
            yield return null;
            Assert.IsTrue(MapSettings.TryLoadPlacement(out saved));
            Assert.AreEqual(asked.center.x, saved.center.x, 1e-5f, "apply saves the asked place");
            Assert.AreEqual(asked.center.y, saved.center.y, 1e-5f);
            Assert.AreEqual(1.3f, saved.scale, 1e-4f);
            AssertClearOfHudAndControls("after apply");
        }

        [UnityTest]
        public IEnumerator Frame_IsPlacedAsTheLayoutSays()
        {
            yield return LoadRuins();
            var frame = view.Frame;
            Assert.IsFalse(view.PlacementSaved, "nothing saved: automatic first place");
            var auto = MiniMapView.AutoPlacement(view.SafeSize, TouchLayout.Defaults());
            Assert.AreEqual(auto.center.x, view.Placement.center.x, 1e-4f);
            Assert.AreEqual(auto.center.y, view.Placement.center.y, 1e-4f);
            Assert.AreEqual(auto.center * view.SafeSize, frame.anchoredPosition);
            Assert.AreEqual(MiniMapView.BaseSize, frame.sizeDelta);
            Assert.AreEqual(Vector3.one, frame.localScale);
            Assert.IsNotNull(frame.parent.GetComponent<SafeAreaFitter>(), "inside the Safe Area (notch)");
            foreach (var g in frame.GetComponentsInChildren<Graphic>())
                Assert.IsFalse(g.raycastTarget, $"{g.name}: display only, never takes a touch");
        }
    }
}
