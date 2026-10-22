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
    /// Knife attack poses (provisional): the image follows the weapon's phase (rest -> pose 1 -> pose 2 -> pose 3 -> rest),
    /// the hit lands while the slash is shown, combat values and swing counts stay as they were, nothing is left over when a
    /// swing is cut short, the hand / arm skin follows every pose, and on iPhone 16 Pro / 16:9 / 4:3 screens the blade and
    /// fist stay readable while the sleeve's cut edge stays off screen.
    /// </summary>
    public class MeleePoseTests : FiringRangeTestBase
    {
        static readonly float Half = Mathf.Tan(25f * Mathf.Deg2Rad);

        MeleeViewModel view;
        SpriteRenderer knife;
        MeleeViewProfile profile;
        Camera worldCam, vmCam;

        // What the screen showed in one frame (read after that frame's LateUpdate) and the phase it was drawn for.
        struct Shown
        {
            public MeleePhase phase;
            public float progress;
            public int pose;
            public Sprite sprite;
            public Vector3 localPosition;
            public float roll;
            public Vector3[] viewport;   // the probe points of the image (see Probes), viewport space
        }

        // Image pixels (from the top-left) per pose: blade tip, the glove's bounding box corners, and the sleeve's cut
        // edges (opaque runs on the canvas border), measured on the displayed images.
        sealed class Probe
        {
            public Vector2 tip;
            public Vector2[] fist;
            public (Vector2 a, Vector2 b)[] cuts;
        }

        static readonly Dictionary<int, Probe> Probes = new Dictionary<int, Probe>
        {
            [-1] = new Probe { tip = new Vector2(242, 206), fist = new Vector2[0], cuts = new[] { (new Vector2(806, 991), new Vector2(1585, 991)), (new Vector2(1585, 956), new Vector2(1585, 991)) } },
            [0] = new Probe { tip = new Vector2(507, 92), fist = Box(220, 604, 471, 922), cuts = new[] { (new Vector2(631, 1567), new Vector2(1002, 1567)), (new Vector2(1002, 1338), new Vector2(1002, 1567)) } },
            [1] = new Probe { tip = new Vector2(98, 476), fist = Box(536, 545, 842, 764), cuts = new[] { (new Vector2(1282, 1399), new Vector2(1679, 1399)), (new Vector2(1679, 1068), new Vector2(1679, 1399)) } },
            [2] = new Probe { tip = new Vector2(66, 581), fist = Box(522, 369, 779, 586), cuts = new[] { (new Vector2(1102, 1115), new Vector2(1408, 1115)), (new Vector2(1408, 863), new Vector2(1408, 1115)) } },
        };

        static Vector2[] Box(float x0, float y0, float x1, float y1) =>
            new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x0, y1), new Vector2(x1, y1) };

        public override void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (worldCam != null)
                worldCam.ResetAspect();
            if (vmCam != null)
                vmCam.ResetAspect();
            base.TearDown();
        }

        IEnumerator LoadKnife()
        {
            yield return LoadScene(InputMode.Desktop);
            Teleport(new Vector3(-1.6f, 0.05f, 2.0f)); // MeleeTarget_Open within reach, straight ahead
            yield return Wait(0.3f);
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            view = player.GetComponentInChildren<MeleeViewModel>(true);
            knife = view.transform.Find("Knife").GetComponent<SpriteRenderer>();
            profile = melee.Definition.viewProfile;
            worldCam = Camera.main;
            vmCam = knife.GetComponentInParent<Camera>();
            Assert.AreEqual(3, profile.poseSprites.Length, "three attack poses imported");
            Assert.Greater(profile.poseTimeline.Length, 0, "pose timeline imported");
        }

        Shown Read(MeleePhase phase, float progress)
        {
            var s = new Shown
            {
                phase = phase,
                progress = progress,
                pose = view.ShownPose,
                sprite = knife.sprite,
                localPosition = knife.transform.localPosition,
                roll = Mathf.DeltaAngle(0f, knife.transform.localEulerAngles.z),
            };
            var probe = Probes[s.pose];
            var points = new List<Vector2> { probe.tip };
            points.AddRange(probe.fist);
            foreach (var (a, b) in probe.cuts)
                for (int i = 0; i <= 8; i++)
                    points.Add(Vector2.Lerp(a, b, i / 8f));
            s.viewport = points.Select(p => vmCam.WorldToViewportPoint(knife.transform.TransformPoint(Local(s.sprite, p)))).ToArray();
            return s;
        }

        static Vector3 Local(Sprite sprite, Vector2 pixel) =>
            new Vector3((pixel.x - sprite.pivot.x) / sprite.pixelsPerUnit, (sprite.rect.height - pixel.y - sprite.pivot.y) / sprite.pixelsPerUnit, 0f);

        /// <summary>
        /// One swing sampled every frame (fixed small steps). A test step resumes after Update, before LateUpdate, so the
        /// view read at step i+1 is what frame i drew for the phase read at step i.
        /// </summary>
        IEnumerator Swing(List<Shown> shown, float step = 1f / 240f, System.Action<MeleePhase, float> during = null)
        {
            Time.captureDeltaTime = step;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Release(mouse.leftButton, queueEventOnly: true);
            MeleePhase phase = melee.Phase;
            float progress = melee.PhaseProgress;
            int idleFrames = 0;
            while (idleFrames < 4)
            {
                during?.Invoke(phase, progress);
                yield return null;
                shown.Add(Read(phase, progress));
                phase = melee.Phase;
                progress = melee.PhaseProgress;
                idleFrames = phase == MeleePhase.Idle ? idleFrames + 1 : 0;
            }
            Time.captureDeltaTime = 0f;
        }

        static List<int> Order(List<Shown> shown)
        {
            var order = new List<int>();
            foreach (var s in shown)
                if (order.Count == 0 || order[^1] != s.pose)
                    order.Add(s.pose);
            return order;
        }

        [UnityTest]
        public IEnumerator Swing_ShowsRestPose1Pose2Pose3Rest_HitWhileSlashing_OneHit_CombatValuesUnchanged()
        {
            yield return LoadKnife();
            var target = GameObject.Find("MeleeTarget_Open").GetComponent<ShootingTarget>();
            string before = JsonUtility.ToJson(melee.Definition);
            var hits = new List<(MeleePhase phase, float progress)>();
            melee.Hit += (c, r) => hits.Add((melee.Phase, melee.PhaseProgress));

            var shown = new List<Shown>();
            yield return Swing(shown);

            CollectionAssert.AreEqual(new[] { -1, 0, 1, 2, -1 }, Order(shown), "rest -> pose 1 -> pose 2 -> pose 3 -> rest");
            foreach (var s in shown)
            {
                int expected = MeleeViewModel.EvaluatePoses(profile, s.phase, s.progress, out _, out _);
                Assert.AreEqual(expected, s.pose, $"image chosen from the phase ({s.phase} {s.progress:0.000})");
                Assert.AreSame(s.pose < 0 ? profile.sprite : profile.poseSprites[s.pose], s.sprite, "sprite of the shown pose");
            }
            Assert.IsTrue(shown.Where(s => s.phase == MeleePhase.Active).All(s => s.pose >= 1), "the active (hit) window shows the slash poses only");
            Assert.IsTrue(shown.Where(s => s.phase == MeleePhase.Windup).All(s => s.pose <= 0), "no slash pose before the hit window");

            Assert.AreEqual(1, melee.SwingCount);
            Assert.AreEqual(1, hits.Count, "one hit per swing");
            Assert.AreEqual(MeleePhase.Active, hits[0].phase, "hit in the active window (unchanged timing)");
            Assert.AreEqual(1, MeleeViewModel.EvaluatePoses(profile, hits[0].phase, hits[0].progress, out _, out _), "pose 2 (centre pass) is on screen when the hit lands");
            Assert.AreEqual(target.MaxHealth - melee.Definition.damage, target.Health, 0.01f, "damage unchanged");
            Assert.AreEqual(before, JsonUtility.ToJson(melee.Definition), "damage / range / timing unchanged");
            Assert.AreEqual(Vector3.one, knife.transform.localScale, "images keep their size");
        }

        [UnityTest]
        public IEnumerator Mash_Switch_Interrupt_NoPoseLeftOver_SwingCountsKept()
        {
            yield return LoadKnife();
            int hits = 0;
            melee.Hit += (c, r) => hits++;
            yield return Wait(0.1f);
            Vector3 rest = knife.transform.localPosition;

            // Mashing during a swing: still one swing, back on the rest image at rest afterwards.
            var shown = new List<Shown>();
            int frame = 0;
            yield return Swing(shown, 1f / 240f, (phase, k) =>
            {
                // Presses only before the recovery, so none is still pending when the swing ends.
                if (phase == MeleePhase.Windup || phase == MeleePhase.Active)
                {
                    if (frame % 4 == 0)
                        Press(mouse.leftButton, queueEventOnly: true);
                    else if (frame % 4 == 2)
                        Release(mouse.leftButton, queueEventOnly: true);
                    frame++;
                }
                else if (mouse.leftButton.isPressed)
                    Release(mouse.leftButton, queueEventOnly: true);
                if (phase == MeleePhase.Active)
                    Assert.IsFalse(weapons.Request(WeaponSlot.Gun), "no weapon switch during a swing (existing rule)");
            });
            Assert.AreEqual(1, melee.SwingCount, "presses during a swing are ignored");
            Assert.AreEqual(1, hits);
            Assert.AreEqual(-1, view.ShownPose);
            Assert.AreSame(profile.sprite, knife.sprite);
            Assert.AreEqual(rest.x, knife.transform.localPosition.x, 1e-4f, "back at rest");
            Assert.AreEqual(rest.y, knife.transform.localPosition.y, 1e-4f, "back at rest");

            // Swings in a row: each one shows the full sequence again.
            for (int i = 0; i < 2; i++)
            {
                var again = new List<Shown>();
                yield return Swing(again);
                CollectionAssert.AreEqual(new[] { -1, 0, 1, 2, -1 }, Order(again), $"swing {i + 2}");
            }
            Assert.AreEqual(3, melee.SwingCount);
            Assert.LessOrEqual(hits, 3, "never more than one hit per swing");

            // Cut short mid-slash (respawn equips a slot at once): the weapon goes idle and the view drops the pose.
            Time.captureDeltaTime = 1f / 240f;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Release(mouse.leftButton, queueEventOnly: true);
            while (view.ShownPose != 1)
                yield return null;
            weapons.ForceSlot(WeaponSlot.Gun);
            yield return null;
            yield return null;
            Assert.AreEqual(MeleePhase.Idle, melee.Phase);
            Assert.IsFalse(knife.enabled, "knife put away");
            weapons.ForceSlot(WeaponSlot.Melee);
            yield return null;
            yield return null;
            Assert.IsTrue(knife.enabled);
            Assert.AreEqual(-1, view.ShownPose, "rest image after the interrupted swing");
            Assert.AreSame(profile.sprite, knife.sprite);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, knife.transform.localEulerAngles.z), 1e-3f, "no roll left over");

            // Normal switch away and back after a swing.
            Time.captureDeltaTime = 0f;
            Assert.IsTrue(weapons.Request(WeaponSlot.Gun));
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.IsTrue(weapons.Request(WeaponSlot.Melee));
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(-1, view.ShownPose);
            var afterSwitch = new List<Shown>();
            yield return Swing(afterSwitch);
            CollectionAssert.AreEqual(new[] { -1, 0, 1, 2, -1 }, Order(afterSwitch), "full sequence after switching back");
            Assert.AreEqual(5, melee.SwingCount, "the interrupted swing and the one after the switch both counted once");
        }

        [UnityTest]
        public IEnumerator Poses_FollowTheHandSkin_EachWithItsOwnRegionMap_BackToStandard()
        {
            yield return LoadKnife();
            SkinSelection.PlayerSkinId = "test_navy";
            yield return null;
            var idleColours = SpriteSkin.CurrentColours(knife);
            Assert.IsNotNull(idleColours);
            Assert.AreEqual(1f, idleColours[(int)SkinRegion.GloveBody].z, "hand skin on the rest image");
            Assert.AreEqual(0f, idleColours[(int)SkinRegion.WeaponBody].z, "the knife keeps its drawn colours");

            var block = new MaterialPropertyBlock();
            var seen = new HashSet<int>();
            bool changedMidSwing = false;
            var shown = new List<Shown>();
            yield return Swing(shown, 1f / 240f, (phase, k) =>
            {
                int pose = view.ShownPose;
                seen.Add(pose);
                Assert.AreEqual("Deadlock/Skinned Sprite", knife.sharedMaterial.shader.name, $"pose {pose}: skin shader");
                knife.GetPropertyBlock(block);
                Texture expected = pose < 0 ? profile.skinRegions : profile.poseSkinRegions[pose];
                Assert.IsNotNull(expected, $"pose {pose} has a region map");
                Assert.AreSame(expected, block.GetTexture(SpriteSkin.RegionsId), $"pose {pose}: its own region map");
                var colours = SpriteSkin.CurrentColours(knife);
                if (!changedMidSwing)
                    CollectionAssert.AreEqual(idleColours, colours, $"pose {pose}: same colours as the rest image");
                if (pose == 1 && !changedMidSwing)
                {
                    SkinSelection.PlayerSkinId = SkinCatalog.StandardId; // changed while slashing
                    changedMidSwing = true;
                }
            });
            CollectionAssert.IsSupersetOf(seen, new[] { -1, 0, 1, 2 });
            Assert.IsTrue(changedMidSwing);
            foreach (var c in SpriteSkin.CurrentColours(knife))
                Assert.AreEqual(0f, c.z, "standard again after the change made mid-swing");
            knife.GetPropertyBlock(block);
            Assert.AreSame(profile.skinRegions, block.GetTexture(SpriteSkin.RegionsId), "rest image's region map after the swing");

            // Standard during a whole swing.
            var standard = new List<Shown>();
            yield return Swing(standard, 1f / 240f, (phase, k) =>
            {
                foreach (var c in SpriteSkin.CurrentColours(knife))
                    Assert.AreEqual(0f, c.z, $"pose {view.ShownPose}: standard look");
            });
        }

        [UnityTest]
        public IEnumerator Layout_IPhone16Pro_16x9_4x3_BladeAndFistReadable_SleeveCutOffScreen_SameSpaceAsPreview()
        {
            yield return LoadKnife();
            Assert.AreEqual(50f, vmCam.fieldOfView, 1e-3f, "view-model camera: vertical FOV 50 (screen half height tan 25 at z = 1, as the preview)");
            foreach (var (name, aspect) in new[] { ("iPhone 16 Pro", 2622f / 1206f), ("16:9", 16f / 9f), ("4:3", 4f / 3f) })
            {
                worldCam.aspect = aspect;
                vmCam.aspect = aspect;
                Time.captureDeltaTime = 1f / 240f;
                yield return null;
                yield return null;
                Vector3 rest = knife.transform.localPosition;
                var shown = new List<Shown>();
                yield return Swing(shown);
                CollectionAssert.AreEqual(new[] { -1, 0, 1, 2, -1 }, Order(shown), name);

                bool tipPastCentre = false;
                foreach (var s in shown)
                {
                    // Same space as the preview: grip at (PlaceX(x), y, 1) on a camera with half height tan 25.
                    MeleeViewModel.EvaluatePoses(profile, s.phase, s.progress, out Vector2 grip, out float roll);
                    Assert.AreEqual(MeleeViewModel.PlaceX(profile, grip.x, aspect), s.localPosition.x, 1e-4f, $"{name}: grip x ({s.phase} {s.progress:0.000})");
                    Assert.AreEqual(grip.y, s.localPosition.y, 1e-4f, $"{name}: grip y");
                    Assert.AreEqual(1f, s.localPosition.z, 1e-6f);
                    Assert.AreEqual(roll, s.roll, 1e-3f, $"{name}: roll");

                    var probe = Probes[s.pose];
                    int cutStart = 1 + probe.fist.Length;
                    for (int i = cutStart; i < s.viewport.Length; i++)
                    {
                        Vector3 v = s.viewport[i];
                        Assert.IsTrue(v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f, $"{name}: sleeve cut edge on screen (pose {s.pose}, {s.phase} {s.progress:0.000}, viewport {v})");
                    }
                    if (s.pose == 0)
                        for (int i = 0; i < cutStart; i++)
                            Assert.That(s.viewport[i].x, Is.InRange(0f, 1f), $"{name}: pose 1 blade / fist inside the left / right edges ({s.progress:0.000})");
                    if (s.pose == 1 && s.viewport[0].x < 0.5f)
                        tipPastCentre = true;
                    if (s.pose == 2 && s.phase == MeleePhase.Active)
                        for (int i = 0; i < cutStart; i++)
                        {
                            Vector3 v = s.viewport[i];
                            Assert.IsTrue(v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f, $"{name}: pose 3 blade tip / fist on screen in the hit window ({s.progress:0.000}, viewport {v})");
                        }
                }
                Assert.IsTrue(tipPastCentre, $"{name}: the blade crosses the screen centre in pose 2");
                Assert.AreEqual(rest.x, knife.transform.localPosition.x, 1e-4f, $"{name}: back at rest");
                Assert.AreEqual(rest.y, knife.transform.localPosition.y, 1e-4f, $"{name}: back at rest");
                Assert.AreEqual(MeleeViewModel.PlaceX(profile, profile.gripPosition.x, aspect), rest.x, 1e-4f, $"{name}: rest place");
            }
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(3, melee.SwingCount);
        }
    }
}
