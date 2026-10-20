using System.Collections;
using System.Collections.Generic;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>Touch layout editing (menu → settings → layout) and sprint slides.</summary>
    public class LayoutAndSlideTests : FiringRangeTestBase
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var go in spawned)
                if (go != null)
                    Object.Destroy(go);
            spawned.Clear();
        }

        GameMenu menu;
        TouchLayoutEditor editor;
        TouchLayoutApplier applier;

        IEnumerator LoadTouch()
        {
            yield return LoadScene(InputMode.Touch);
            menu = Object.FindAnyObjectByType<GameMenu>();
            editor = menu.GetComponent<TouchLayoutEditor>();
            applier = Object.FindAnyObjectByType<TouchLayoutApplier>();
            yield return null;
        }

        Vector2 AnchorOf(string id) => applier.Widget(id).anchorMin;

        // ------------------------------------------------------------------ layout

        [UnityTest]
        public IEnumerator Layout_ApplySaves_AndIsRestoredOnNextLoad()
        {
            yield return LoadTouch();
            Vector2 before = AnchorOf(TouchLayout.Fire);
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.IsOpen);
            Assert.IsTrue(router.IsBlocked, "Game input stops while editing");
            Assert.IsTrue(editor.MoveBy(TouchLayout.Fire, new Vector2(15f, -10f)), editor.Message); // toward the corner: clear of other buttons
            Assert.IsTrue(editor.Resize(TouchLayout.Fire, -1));
            yield return null;
            Vector2 moved = AnchorOf(TouchLayout.Fire);
            Assert.AreNotEqual(before, moved, "Preview moves the real widget");
            float size = applier.Widget(TouchLayout.Fire).sizeDelta.x;
            Assert.AreEqual(TouchLayout.BaseDiameter(TouchLayout.Fire) * 0.9f, size, 0.5f);
            editor.ApplyAndClose();
            Assert.IsFalse(editor.IsOpen);
            Assert.IsTrue(PlayerPrefs.HasKey(TouchLayout.PrefsKey), "Applied layout is stored on the device");
            menu.Close();
            yield return null;
            Assert.IsFalse(router.IsBlocked);

            // Next launch (scene load): restored.
            yield return LoadTouch();
            yield return null;
            Assert.AreEqual(moved.x, AnchorOf(TouchLayout.Fire).x, 0.001f);
            Assert.AreEqual(moved.y, AnchorOf(TouchLayout.Fire).y, 0.001f);
            Assert.AreEqual(size, applier.Widget(TouchLayout.Fire).sizeDelta.x, 0.5f);
        }

        [UnityTest]
        public IEnumerator Layout_CancelRestores_ResetShowsDefaults()
        {
            yield return LoadTouch();
            Vector2 saved = AnchorOf(TouchLayout.Jump);
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.MoveBy(TouchLayout.Jump, new Vector2(-150f, 0f)));
            Assert.AreNotEqual(saved, AnchorOf(TouchLayout.Jump));
            editor.CancelAndClose();
            yield return null;
            Assert.AreEqual(saved, AnchorOf(TouchLayout.Jump), "Cancel puts the saved layout back");
            Assert.IsFalse(PlayerPrefs.HasKey(TouchLayout.PrefsKey), "Cancel stores nothing");

            // Change and apply, then reset to defaults and apply.
            menu.OpenLayoutEditor();
            yield return null;
            editor.MoveBy(TouchLayout.Jump, new Vector2(-150f, 0f));
            editor.ApplyAndClose();
            menu.OpenLayoutEditor();
            yield return null;
            editor.ResetToDefaults();
            editor.ApplyAndClose();
            yield return null;
            Assert.AreEqual(saved.x, AnchorOf(TouchLayout.Jump).x, 0.001f, "Reset returns to the initial layout");
            Assert.AreEqual(saved.y, AnchorOf(TouchLayout.Jump).y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Layout_RefusesOverlap_AndKeepsInsideSafeAreaAndBelowMenu()
        {
            yield return LoadTouch();
            menu.OpenLayoutEditor();
            yield return null;
            var working = editor.Working;
            // Drag FIRE straight onto ADS: refused, nothing changes.
            Vector2 safe = applier.SafeSize;
            float scale = Object.FindAnyObjectByType<Canvas>().rootCanvas.scaleFactor;
            Vector2 toAds = Vector2.Scale(working.Get(TouchLayout.Ads).center - working.Get(TouchLayout.Fire).center, safe) * scale;
            Vector2 fireBefore = AnchorOf(TouchLayout.Fire);
            Assert.IsFalse(editor.MoveBy(TouchLayout.Fire, toAds), "Overlapping buttons are refused");
            Assert.IsNotEmpty(editor.Message);
            Assert.AreEqual(fireBefore, AnchorOf(TouchLayout.Fire));

            // Far off screen to the upper right: clamped inside the Safe Area and below the menu band.
            Assert.IsTrue(editor.MoveBy(TouchLayout.Weapon, new Vector2(5000f, 5000f)));
            yield return null;
            var rect = applier.Widget(TouchLayout.Weapon);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var safeCorners = new Vector3[4];
            applier.SafeArea.GetWorldCorners(safeCorners);
            Assert.LessOrEqual(corners[2].x, safeCorners[2].x + 0.5f, "Right edge inside the Safe Area");
            float topLimit = Mathf.Lerp(safeCorners[0].y, safeCorners[2].y, TouchLayout.TopLimit);
            Assert.LessOrEqual(corners[2].y, topLimit + 0.5f, "Top edge below the menu band");

            // Menu button stays usable while editing is closed again.
            editor.CancelAndClose();
            menu.Close();
            yield return null;
            Assert.IsTrue(menu.MenuButton.gameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator Layout_EditingBlocksTouchesOnGameButtons()
        {
            yield return LoadTouch();
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var fire = GameObject.Find("FireButton").GetComponent<TouchButton>();
            Vector2 firePoint = ScreenCenter((RectTransform)fire.transform);
            int fired = 0;
            gun.Fired += () => fired++;
            menu.OpenLayoutEditor();
            yield return null;
            BeginTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return WaitRealtime(0.3f);
            EndTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.IsFalse(fire.IsHeld, "The editor overlay takes the touch, not the FIRE button");
            Assert.AreEqual(0, fired);
            editor.CancelAndClose();
            menu.Close();
            yield return null;

            // Back in the game the button works again.
            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.2f);
            Assert.IsTrue(fire.IsHeld);
            Assert.Greater(fired, 0);
            EndTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        // ------------------------------------------------------------------ slide

        IEnumerator SprintFor(float seconds)
        {
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(seconds);
        }

        void StopSprint()
        {
            Release(keyboard.wKey, queueEventOnly: true);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
        }

        [UnityTest]
        public IEnumerator Slide_OnlyFromSprint_SlidesDecelerates_EndsCrouched_NoRetrigger_Cooldown()
        {
            yield return LoadScene(InputMode.Desktop);
            Teleport(new Vector3(1f, 0.05f, -4f));
            yield return Wait(0.3f);

            // Walking + C = crouch, not slide.
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(0.3f);
            yield return Tap(keyboard.cKey);
            Release(keyboard.wKey, queueEventOnly: true);
            Assert.AreEqual(Posture.Crouching, posture.Current, "No slide without sprinting");
            yield return Tap(keyboard.cKey);
            yield return Wait(0.4f);
            Teleport(new Vector3(1f, 0.05f, -4f));
            yield return Wait(0.2f);

            // Sprint + C = slide.
            yield return SprintFor(0.5f);
            Press(keyboard.cKey, queueEventOnly: true); // held down on purpose
            yield return null;
            yield return null;
            StopSprint();
            Assert.IsTrue(posture.IsSliding, "Sprint + C slides");
            Vector3 start = player.transform.position;
            yield return Wait(0.15f);
            float early = motor.PlanarSpeed;
            Assert.Less(player.GetComponent<CharacterController>().height, 1.15f, "Lower capsule while sliding");
            Assert.Less(CameraPivot().localPosition.y, 1.0f, "Lower camera while sliding");
            yield return Wait(0.4f);
            float late = motor.PlanarSpeed;
            Assert.Less(late, early, "Decelerates");
            Assert.Greater(player.transform.position.z - start.z, 1.5f, "Keeps sliding in the start direction without input");
            yield return Wait(0.5f);
            Assert.IsFalse(posture.IsSliding);
            Assert.AreEqual(Posture.Crouching, posture.Current, "Ends crouched");
            Assert.IsFalse(posture.SlideReady, "Cooldown runs right after a slide");
            yield return Wait(0.5f);
            Release(keyboard.cKey, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(Posture.Crouching, posture.Current, "Holding C does not slide again or stand up");

            // After the cooldown a new slide is possible again.
            yield return Wait(1.0f);
            Assert.IsTrue(posture.SlideReady);
        }

        Transform CameraPivot() => player.transform.Find("CameraPivot");

        [UnityTest]
        public IEnumerator Slide_JumpIgnored_FiringAllowed_StopsAtWall()
        {
            yield return LoadScene(InputMode.Desktop);
            // Run at the right wall (x = 8) from x = 2.
            Teleport(new Vector3(2f, 0.05f, 2f), 90f);
            yield return Wait(0.3f);
            int fired = 0;
            gun.Fired += () => fired++;
            yield return SprintFor(0.35f);
            yield return Tap(keyboard.cKey);
            StopSprint();
            Assert.IsTrue(posture.IsSliding);
            float y = player.transform.position.y;
            yield return Tap(keyboard.spaceKey);
            yield return Wait(0.1f);
            Assert.IsTrue(posture.IsSliding, "Jump does not cancel the slide");
            Assert.AreEqual(y, player.transform.position.y, 0.05f, "No jump during a slide");
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.2f);
            Release(mouse.leftButton, queueEventOnly: true);
            Assert.Greater(fired, 0, "Firing is allowed while sliding");
            yield return Wait(1.0f);
            Assert.IsFalse(posture.IsSliding);
            Assert.Less(player.transform.position.x, 8f - 0.3f, "Stopped by the wall, not through it");
            Assert.AreEqual(Posture.Crouching, posture.Current);
        }

        [UnityTest]
        public IEnumerator Slide_UnderLowCeiling_EndsProne_OffLedge_EndsInAir()
        {
            yield return LoadScene(InputMode.Desktop);
            // A 1.15 m ceiling: the slide capsule (1.0 m + skin) fits under it, crouching (1.2 m + skin) does not.
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(ceiling);
            ceiling.transform.position = new Vector3(1f, 1.15f + 0.1f, 3f); // covers z 0..6
            ceiling.transform.localScale = new Vector3(3f, 0.2f, 6f);
            Physics.SyncTransforms();
            // Start far enough back that the capsule is already low when it reaches the ceiling edge
            // (sliding straight into a low gap right after starting stops at its edge, like a wall).
            Teleport(new Vector3(1f, 0.05f, -3.5f));
            yield return Wait(0.3f);
            yield return SprintFor(0.3f);
            yield return Tap(keyboard.cKey);
            StopSprint();
            Assert.IsTrue(posture.IsSliding);
            yield return Wait(1.2f);
            Assert.IsFalse(posture.IsSliding);
            Assert.Greater(player.transform.position.z, 0.5f, "Slid in under the ceiling");
            Assert.AreEqual(Posture.Prone, posture.Current, "No room to crouch: ends prone");
            Assert.Less(CameraPivot().position.y, 1.15f);

            // Off the jump platform edge (top 0.8 m): the slide ends as soon as it leaves the ground.
            Teleport(new Vector3(5.5f, 0.9f, 5.9f), 180f);
            yield return Tap(keyboard.zKey); // prone -> stand
            yield return Wait(0.6f);
            Assert.AreEqual(Posture.Standing, posture.Current);
            yield return Wait(posture.SlideReady ? 0f : 1f);
            yield return SprintFor(0.18f);
            yield return Tap(keyboard.cKey);
            StopSprint();
            bool slid = posture.IsSliding;
            float endTime = Time.time + 1f;
            bool airborneWhileSliding = false;
            while (Time.time < endTime)
            {
                if (posture.IsSliding && !motor.IsGrounded && player.transform.position.y < 0.6f)
                    airborneWhileSliding = true;
                yield return null;
            }
            Assert.IsTrue(slid, "Slide started on the platform");
            Assert.IsFalse(airborneWhileSliding, "Leaving the ground ends the slide");
            Assert.Less(player.transform.position.y, 0.3f, "Fell to the floor normally");
        }

        [UnityTest]
        public IEnumerator Slide_StartedRightBeforeLowGap_EntersIt_CameraEasesDownBelowCeiling()
        {
            yield return LoadScene(InputMode.Desktop);
            // Gap 1.15 m high (bottom of the ceiling), from z = 0 to 2: too low to stand or crouch, enough to slide.
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(ceiling);
            ceiling.transform.position = new Vector3(1f, 1.25f, 1f);
            ceiling.transform.localScale = new Vector3(3f, 0.2f, 2f);
            Physics.SyncTransforms();
            const float ceilingBottom = 1.15f;

            // Fixed 60 fps frames so "right before the edge" is reproducible.
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                // Sprint at it and press C about one frame before the standing capsule (radius 0.35) touches the edge.
                Teleport(new Vector3(1f, 0.05f, -4f));
                yield return Wait(0.3f);
                Press(keyboard.leftShiftKey, queueEventOnly: true);
                Press(keyboard.wKey, queueEventOnly: true);
                for (float end = Time.time + 2f; Time.time < end && player.transform.position.z < -0.52f; )
                    yield return null;
                Assert.IsTrue(motor.IsSprinting, "Sprinting toward the gap");
                yield return Tap(keyboard.cKey);
                Release(keyboard.wKey, queueEventOnly: true);
                Release(keyboard.leftShiftKey, queueEventOnly: true);
                Assert.IsTrue(posture.IsSliding, "Slide started in front of the gap");
                Assert.Less(player.GetComponent<CharacterController>().height, 1.15f, "Movement capsule is low at once");
                Assert.AreEqual(0.05f, player.transform.position.y, 0.06f, "Feet stay on the floor");

                var cam = CameraPivot();
                float lastEye = cam.position.y;
                float biggestDrop = 0f;
                bool cameraUnderCeiling = false;
                while (posture.IsSliding)
                {
                    yield return null;
                    float eye = cam.position.y;
                    biggestDrop = Mathf.Max(biggestDrop, lastEye - eye);
                    lastEye = eye;
                    if (cam.position.z > 0f && cam.position.z < 2f)
                    {
                        cameraUnderCeiling = true;
                        Assert.Less(eye, ceilingBottom - 0.02f, "Camera never pokes through the ceiling");
                    }
                }
                Assert.IsTrue(cameraUnderCeiling);
                Assert.Greater(player.transform.position.z, 0.6f, "The slide enters the gap instead of stopping at its edge");
                Assert.Less(biggestDrop, 0.2f, "Camera eases down (no single-frame jump)");
                // No room to crouch under the gap: ends prone (room check before growing is kept).
                if (player.transform.position.z < 2f)
                    Assert.AreEqual(Posture.Prone, posture.Current);
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }
        }

        [UnityTest]
        public IEnumerator Touch_PostureButtonWhileSprinting_SlidesAtOnce_HoldDoesNotGoProne()
        {
            yield return LoadTouch();
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            Teleport(new Vector3(1f, 0.05f, -4f));
            var stick = Object.FindAnyObjectByType<VirtualJoystick>();
            var button = GameObject.Find("PostureButton").GetComponent<TouchButton>();
            Vector2 origin = ScreenCenter((RectTransform)stick.transform) * 0.6f;
            Vector2 p = ScreenCenter((RectTransform)button.transform);
            float unit = 110f * Screen.height / 1080f;

            BeginTouch(1, origin, queueEventOnly: true, screen: touchscreen);
            yield return null;
            MoveTouch(1, origin + new Vector2(0f, unit), queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            Assert.IsTrue(motor.IsSprinting);

            BeginTouch(2, p, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return null;
            Assert.IsTrue(posture.IsSliding, "Pressing the posture button while sprinting slides immediately");
            EndTouch(1, origin, queueEventOnly: true, screen: touchscreen);
            yield return Wait(1.2f); // keep the posture finger down past the long-press time and the slide
            Assert.AreEqual(Posture.Crouching, posture.Current, "Holding does not turn into prone");
            EndTouch(2, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            Assert.AreEqual(Posture.Crouching, posture.Current, "Releasing does not add a tap");

            // Not sprinting: tap = crouch toggle as before.
            BeginTouch(3, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            EndTouch(3, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            Assert.AreEqual(Posture.Standing, posture.Current);
        }
    }
}
