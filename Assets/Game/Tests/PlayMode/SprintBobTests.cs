using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Sprint step motion of the local camera (<see cref="CameraBob"/>): only while really running on the ground,
    /// only downward from the eye, no rotation, off against a wall / in the air / while sliding or aiming.
    /// </summary>
    public class SprintBobTests : FiringRangeTestBase
    {
        CameraBob bob;
        Transform pivot;

        IEnumerator Setup()
        {
            yield return LoadScene(InputMode.Desktop);
            bob = Camera.main.GetComponent<CameraBob>();
            Assert.IsNotNull(bob, "the main camera has the sprint step motion");
            pivot = Camera.main.transform.parent;
        }

        void AssertCameraSafe()
        {
            var cam = Camera.main.transform;
            Assert.LessOrEqual(cam.localPosition.y, 1e-5f, "never above the eye (no camera into a low ceiling)");
            Assert.AreEqual(Quaternion.identity, cam.localRotation, "no tilt / pitch from the bob");
            Assert.Less(Vector3.Angle(cam.forward, pivot.forward), 0.01f, "aim direction = view direction");
        }

        [UnityTest]
        public IEnumerator Sprint_BobsDownOnly_WalkDoesNot_StopsWhenStopping()
        {
            yield return Setup();
            Teleport(new Vector3(0f, 0.05f, -5f));
            Press(keyboard.wKey, queueEventOnly: true);
            float end = Time.time + 0.8f;
            while (Time.time < end) { Assert.AreEqual(0f, bob.Blend, "walking: no camera bob"); AssertCameraSafe(); yield return null; }
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            float lowest = 0f, maxBlend = 0f;
            end = Time.time + 1.5f;
            while (Time.time < end)
            {
                AssertCameraSafe();
                lowest = Mathf.Min(lowest, bob.Offset.y);
                maxBlend = Mathf.Max(maxBlend, bob.Blend);
                yield return null;
            }
            Assert.IsTrue(motor.IsSprinting);
            Assert.Greater(maxBlend, 0.8f, "full strength while sprinting");
            Assert.Less(lowest, -0.05f, "clearly visible step drop");
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.wKey, queueEventOnly: true);
            yield return Wait(0.5f);
            Assert.AreEqual(0f, bob.Blend, 0.001f, "fades out after stopping");
            Assert.AreEqual(Vector3.zero, Camera.main.transform.localPosition);
        }

        [UnityTest]
        public IEnumerator SprintIntoAWall_TheBobSettles()
        {
            yield return Setup();
            Teleport(new Vector3(-6.5f, 0.05f, 10f), -90f); // the left wall is 1.15 m ahead
            Press(keyboard.wKey, queueEventOnly: true);
            yield return null; // one key per frame: two queued events of one device in a frame keep only the last
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            yield return Wait(1.2f);
            Assert.Less(motor.PlanarSpeed, 0.2f, "stopped by the wall");
            Assert.Less(bob.Blend, 0.05f, "no running bob while not moving");
            AssertCameraSafe();
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SprintThenJumpSlideOrAim_TheBobFadesQuickly()
        {
            yield return Setup();
            foreach (int kind in new[] { 0, 1, 2 })
            {
                Teleport(new Vector3(0f, 0.05f, -5f));
                yield return Wait(0.3f);
                Press(keyboard.wKey, queueEventOnly: true);
                yield return null;
                Press(keyboard.leftShiftKey, queueEventOnly: true);
                yield return Wait(0.9f);
                Assert.Greater(bob.Blend, 0.5f, $"{kind}: sprinting first");
                if (kind == 0) yield return Tap(keyboard.spaceKey);
                if (kind == 1) yield return Tap(keyboard.cKey);
                if (kind == 2) Press(mouse.rightButton, queueEventOnly: true);
                yield return Wait(0.2f);
                if (kind == 0) Assert.IsFalse(motor.IsGrounded, "in the air");
                if (kind == 1) Assert.IsTrue(posture.IsSliding, "sliding");
                if (kind == 2) Assert.Greater(gun.AdsBlend, 0.5f, "aiming");
                Assert.Less(bob.Blend, 0.05f, $"{kind}: the ground sprint bob does not overlap the jump / slide / aim");
                AssertCameraSafe();
                Release(keyboard.leftShiftKey, queueEventOnly: true);
                yield return null;
                Release(keyboard.wKey, queueEventOnly: true);
                if (kind == 2) Release(mouse.rightButton, queueEventOnly: true);
                yield return Wait(1.2f);
                posture.Request(Deadlock.Player.Posture.Standing);
                yield return Wait(0.6f);
            }
        }

        [UnityTest]
        public IEnumerator SprintWithTheKnife_AlsoBobs()
        {
            yield return Setup();
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.2f);
            Teleport(new Vector3(0f, 0.05f, -5f));
            Press(keyboard.wKey, queueEventOnly: true);
            yield return null; // one key per frame: two queued events of one device in a frame keep only the last
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            float maxBlend = 0f;
            float end = Time.time + 1.2f;
            while (Time.time < end) { maxBlend = Mathf.Max(maxBlend, bob.Blend); AssertCameraSafe(); yield return null; }
            Assert.Greater(maxBlend, 0.8f);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
        }
    }
}
