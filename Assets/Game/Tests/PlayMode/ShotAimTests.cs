using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 38: where every gun's shots really go, hip and aimed (the hit is the game's; the drawn muzzle is looks only):
    /// each projectile starts at the camera (the crosshair's eye) and flies within the gun's current spread of the
    /// camera's aim, so it ends at the crosshair at near, middle and far range; a wall right in front stops it (no
    /// shot through); the shotgun's eight pellets spread around the aim (never all on the centre); automatic fire keeps
    /// every shot on the aim of its moment while the recoil lifts it; the flash (the tracer's start) is on the drawn
    /// muzzle of the hip image. The rifle, SMG and handgun are checked the same way (no change for them).
    /// </summary>
    public class ShotAimTests : FiringRangeTestBase
    {
        static readonly string[] MainGuns = { "proto_rifle", "proto_smg", "proto_shotgun", "proto_lmg", "proto_epic_smg" };
        readonly List<GameObject> walls = new List<GameObject>();

        public override void TearDown()
        {
            foreach (var w in walls)
                if (w != null)
                    Object.Destroy(w);
            walls.Clear();
            DevGunSelection.ClearSelection();
            base.TearDown();
        }

        Transform Aim => Camera.main.transform;

        struct Shot
        {
            public Vector3 Origin, Direction, Forward, End;
            public float Spread;
        }

        /// <summary>Records each projectile as it is fired: its start, its direction, the camera's aim and the gun's spread then.</summary>
        List<Shot> Record()
        {
            var shots = new List<Shot>();
            projectiles.Spawned += (id, origin) =>
            {
                if (!projectiles.TryPredictEnd(id, out var end, out var direction))
                    return;
                shots.Add(new Shot { Origin = origin, Direction = direction, Forward = Aim.forward, End = end, Spread = gun.CurrentSpreadDegrees });
            };
            return shots;
        }

        GameObject Wall(float distance)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = $"TestWall_{distance}";
            w.transform.SetPositionAndRotation(Aim.position + Aim.forward * (distance + 0.05f), Aim.rotation);
            w.transform.localScale = new Vector3(Mathf.Max(2f, distance), Mathf.Max(2f, distance), 0.1f);
            walls.Add(w);
            Physics.SyncTransforms();
            return w;
        }

        IEnumerator Take(string weaponId)
        {
            var sel = player.GetComponent<DevGunSelection>();
            var def = sel.MainGunChoices.FirstOrDefault(d => d.weaponId == weaponId);
            Assert.IsNotNull(def, weaponId);
            Assert.IsTrue(sel.TryEquipMain(def, out string error), error);
            yield return Wait(0.5f);
            Assert.AreEqual(weaponId, gun.Definition.weaponId);
        }

        IEnumerator Aimed(bool on)
        {
            if (on)
                Press(mouse.rightButton, queueEventOnly: true);
            else
                Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(gun.Definition.adsTime + 0.3f);
        }

        static void AssertOnAim(Shot s, string what)
        {
            Assert.That(Vector3.Distance(s.Origin, Camera.main.transform.position), Is.LessThan(0.01f), $"{what}: starts at the camera");
            float angle = Vector3.Angle(s.Forward, s.Direction);
            Assert.That(angle, Is.LessThanOrEqualTo(s.Spread + 0.05f), $"{what}: {angle:0.00}° off the aim (spread {s.Spread:0.00}°)");
        }

        [UnityTest]
        public IEnumerator EveryGun_HipAndAimed_ShotsLeaveTheCameraWithinTheSpreadOfTheCrosshair()
        {
            yield return LoadScene(InputMode.Desktop);
            var shots = Record();
            var lines = new List<string>();
            foreach (var id in MainGuns)
            {
                yield return Take(id);
                foreach (bool ads in new[] { false, true })
                {
                    yield return Aimed(ads);
                    shots.Clear();
                    yield return Tap(mouse.leftButton);
                    yield return null;
                    Assert.AreEqual(gun.Definition.projectilesPerShot, shots.Count, $"{id} ads={ads}: one pull");
                    foreach (var s in shots)
                        AssertOnAim(s, $"{id} ads={ads}");
                    if (ads)
                        Assert.Less(shots[0].Spread, gun.Definition.hipSpreadDegrees + 0.001f, $"{id}: aimed is not wider than the hip");
                    lines.Add($"{id} ads={ads}: spread {shots[0].Spread:0.00}°, max off-aim {shots.Max(s => Vector3.Angle(s.Forward, s.Direction)):0.00}°");
                    yield return Wait(Mathf.Max(0.4f, gun.Definition.fireInterval + 0.1f));
                }
                yield return Aimed(false);
            }
            TestContext.WriteLine(string.Join("\n", lines));
        }

        [UnityTest]
        public IEnumerator NearMiddleFar_TheShotEndsOnTheWallAtTheCrosshair_EvenRightInFrontOfTheGun()
        {
            yield return LoadScene(InputMode.Desktop);
            Teleport(new Vector3(1.2f, 0.05f, 0f)); // a lane clear of the range's targets and crates for 15 m
            yield return null;
            var shots = Record();
            foreach (var id in new[] { "proto_shotgun", "proto_lmg", "proto_epic_smg", "proto_rifle" })
            {
                yield return Take(id);
                foreach (float distance in new[] { 0.3f, 5f, 15f })
                {
                    var wall = Wall(distance);
                    shots.Clear();
                    yield return Tap(mouse.leftButton);
                    yield return null;
                    Assert.Greater(shots.Count, 0);
                    // The wall's front face: the shot must end on it (stopped there, never behind it).
                    Vector3 normal = -wall.transform.forward;
                    Vector3 face = wall.transform.position + normal * (wall.transform.localScale.z * 0.5f);
                    foreach (var s in shots)
                    {
                        bool hits = Physics.Raycast(s.Origin, s.Direction, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore);
                        float offFace = Vector3.Dot(s.End - face, normal);
                        float range = Vector3.Distance(s.Origin, s.End);
                        TestContext.WriteLine($"{id} at {distance} m: ends {range:0.000} m away, {offFace:0.000} m off the face, first collider {(hits ? hit.collider.name + " at " + hit.distance.ToString("0.000") : "none")}");
                        Assert.That(Mathf.Abs(offFace), Is.LessThan(0.01f), $"{id} at {distance} m: stopped on the wall's face (no shot through)");
                        float lateral = Vector3.Cross(s.End - s.Origin, s.Forward).magnitude;
                        Assert.LessOrEqual(lateral, range * Mathf.Tan((s.Spread + 0.05f) * Mathf.Deg2Rad) + 0.01f, $"{id} at {distance} m: at the crosshair within the spread");
                    }
                    Object.Destroy(wall);
                    yield return Wait(Mathf.Max(0.4f, gun.Definition.fireInterval + 0.1f));
                }
            }
        }

        [UnityTest]
        public IEnumerator Shotgun_EightPellets_SpreadAroundTheAim_NotAllOnTheCentre()
        {
            yield return LoadScene(InputMode.Desktop);
            var shots = Record();
            yield return Take("proto_shotgun");
            foreach (bool ads in new[] { false, true })
            {
                yield return Aimed(ads);
                var offAim = new List<float>();
                for (int pull = 0; pull < 3; pull++)
                {
                    shots.Clear();
                    yield return Tap(mouse.leftButton);
                    yield return null;
                    Assert.AreEqual(8, shots.Count, "eight pellets for one round");
                    foreach (var s in shots)
                        AssertOnAim(s, $"shotgun ads={ads}");
                    var axis = shots.Aggregate(Vector3.zero, (a, s) => a + s.Direction).normalized;
                    Assert.Less(Vector3.Angle(shots[0].Forward, axis), shots[0].Spread, "the pattern is centred on the aim");
                    float widest = shots.SelectMany(a => shots.Select(b => Vector3.Angle(a.Direction, b.Direction))).Max();
                    Assert.Greater(widest, 0.2f, "the pellets are spread, not all on the centre");
                    offAim.AddRange(shots.Select(s => Vector3.Angle(s.Forward, s.Direction)));
                    yield return Wait(gun.Definition.fireInterval + 0.15f);
                }
                TestContext.WriteLine($"shotgun ads={ads}: spread {gun.CurrentSpreadDegrees:0.00}°, pellets off-aim min {offAim.Min():0.00}° mean {offAim.Average():0.00}° max {offAim.Max():0.00}°");
                yield return Aimed(false);
            }
        }

        [UnityTest]
        public IEnumerator AutomaticFire_EveryShotOnTheAimOfItsMoment_WhileTheRecoilLifts()
        {
            yield return LoadScene(InputMode.Desktop);
            var shots = Record();
            foreach (var id in new[] { "proto_lmg", "proto_epic_smg" })
            {
                yield return Take(id);
                float pitchBefore = Aim.eulerAngles.x;
                shots.Clear();
                Press(mouse.leftButton, queueEventOnly: true);
                yield return Wait(0.6f);
                Release(mouse.leftButton, queueEventOnly: true);
                yield return null;
                Assert.GreaterOrEqual(shots.Count, 4, $"{id}: automatic");
                foreach (var s in shots)
                    AssertOnAim(s, id);
                float lift = Vector3.Angle(shots[0].Forward, shots[shots.Count - 1].Forward);
                Assert.Greater(lift, 0.1f, $"{id}: the recoil moves the aim during the burst (and the shots follow it)");
                TestContext.WriteLine($"{id}: {shots.Count} shots in 0.6 s, aim moved {lift:0.00}° by the recoil, pitch before {pitchBefore:0.0}");
                yield return Wait(1f);
            }
        }

        [UnityTest]
        public IEnumerator Flash_AtTheDrawnMuzzle_BehindTheGun_EveryGun()
        {
            yield return LoadScene(InputMode.Desktop);
            var view = Object.FindAnyObjectByType<WeaponViewModel>();
            foreach (var id in MainGuns)
            {
                yield return Take(id);
                yield return null;
                var p = gun.Definition.viewProfile;
                Assert.IsNotNull(p.hipSprite, id);
                Assert.IsTrue(view.TryGetMuzzleScreenPoint(out var flash), $"{id}: the hip muzzle is on screen");
                // The hip sprite's pivot is the muzzle pixel of its placement file: the flash sits on it.
                var hip = view.Presenter.Hip.transform;
                var cam = Camera.allCameras.First(c => c != Camera.main && (c.cullingMask & (1 << hip.gameObject.layer)) != 0);
                Vector2 pivot = cam.WorldToScreenPoint(hip.TransformPoint(Vector3.zero));
                // At the flash's own depth (just behind the gun): off the centre a depth step moves it on screen.
                float depth = view.Presenter.HipFlash.transform.localPosition.z;
                Vector2 expected = cam.WorldToScreenPoint(hip.TransformPoint(new Vector3(p.hipMuzzleFlashOffset.x, p.hipMuzzleFlashOffset.y, depth)));
                Assert.That(Vector2.Distance(flash, expected), Is.LessThan(1.5f), $"{id}: flash {flash} at the drawn muzzle {pivot} + its offset");
                var hipFlash = view.Presenter.HipFlash;
                bool levelBarrel = id == "proto_shotgun" || id == "proto_lmg" || id == "proto_epic_smg";
                Assert.Less(hipFlash.sortingOrder, view.Presenter.Hip.sortingOrder, $"{id}: drawn behind the gun (no flash painted over it)");
                Assert.Greater(depth, 0f, $"{id}: just behind the gun image");
                if (levelBarrel)
                {
                    Assert.That(Vector2.Distance(flash, pivot), Is.InRange(3f, 40f), $"{id}: centred just beyond the muzzle, still at it");
                    Assert.Less(flash.x, pivot.x, $"{id}: beyond the muzzle along the barrel (left)");
                }
                else
                {
                    Assert.That(Vector2.Distance(flash, pivot), Is.LessThan(1.5f), $"{id}: on the muzzle as before");
                }
            }
        }
    }
}
