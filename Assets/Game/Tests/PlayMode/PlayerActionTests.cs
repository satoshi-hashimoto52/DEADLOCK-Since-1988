using System.Collections;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>Posture, jump/posture interplay, sprint hysteresis, weapon switching and melee in the prototype scene.</summary>
    public class PlayerActionTests : FiringRangeTestBase
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

        float FeetY => player.transform.position.y;
        Transform CameraPivot => player.transform.Find("CameraPivot");

        IEnumerator WaitForPosture() => Wait(0.5f);

        IEnumerator WalkDistance(float seconds, System.Action<float> result)
        {
            Vector3 start = player.transform.position;
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(seconds);
            Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            Vector3 d = player.transform.position - start;
            result(new Vector2(d.x, d.z).magnitude);
            yield return Wait(0.3f);
        }

        // ------------------------------------------------------------------ posture

        [UnityTest]
        public IEnumerator Posture_Keys_ChangeCapsuleEyeAndSpeed()
        {
            yield return LoadScene(InputMode.Desktop);
            var controller = player.GetComponent<CharacterController>();
            yield return Wait(0.3f);
            float groundY = FeetY;
            Assert.AreEqual(Posture.Standing, posture.Current);
            Assert.AreEqual(1.8f, controller.height, 0.01f);

            float stand = 0f, crouch = 0f, prone = 0f;
            yield return WalkDistance(0.6f, d => stand = d);
            Teleport(new Vector3(0f, 0.05f, 0f));

            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Crouching, posture.Current);
            Assert.AreEqual(1.2f, controller.height, 0.01f);
            Assert.AreEqual(0.6f, controller.center.y, 0.01f, "Feet must stay put: centre = height / 2");
            Assert.AreEqual(1.05f, CameraPivot.localPosition.y, 0.01f);
            Assert.AreEqual(groundY, FeetY, 0.03f, "No hop or sink when crouching");
            yield return WalkDistance(0.6f, d => crouch = d);
            Teleport(new Vector3(0f, 0.05f, 0f));

            yield return Tap(keyboard.zKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current);
            Assert.AreEqual(0.75f, controller.height, 0.01f);
            Assert.AreEqual(0.45f, CameraPivot.localPosition.y, 0.01f);
            Assert.AreEqual(groundY, FeetY, 0.03f);
            yield return WalkDistance(0.6f, d => prone = d);

            Assert.Less(crouch, stand * 0.75f, "Crouch is slower than walking");
            Assert.Less(prone, crouch * 0.75f, "Prone is slower than crouch");
            Assert.Greater(prone, 0.05f, "Prone still moves");

            yield return Tap(keyboard.cKey); // prone -> crouch
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Crouching, posture.Current);
            yield return Tap(keyboard.zKey); // crouch -> prone
            yield return WaitForPosture();
            yield return Tap(keyboard.zKey); // prone -> stand
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Standing, posture.Current);
            Assert.AreEqual(1.8f, controller.height, 0.01f);
            Assert.AreEqual(groundY, FeetY, 0.03f, "No hop when standing up");
        }

        [UnityTest]
        public IEnumerator Posture_LowCeiling_RefusesToStandOrCrouchUp()
        {
            yield return LoadScene(InputMode.Desktop);
            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();

            // Crouch tunnel: 1.35 m clearance.
            Teleport(new Vector3(6.6f, 0.05f, 12f));
            yield return Wait(0.3f);
            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Crouching, posture.Current, "No room to stand under the crouch tunnel");
            Assert.Less(Time.time - posture.LastBlockedTime, 1f);
            float y = FeetY;
            yield return Tap(keyboard.spaceKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Crouching, posture.Current, "Jump = stand request, refused under the ceiling");
            Assert.AreEqual(y, FeetY, 0.03f, "Must not jump");
            Assert.Less(CameraPivot.position.y, 1.35f, "Camera stays below the ceiling");

            yield return Tap(keyboard.zKey); // shrinking is always allowed
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current);

            // Prone tunnel: 0.85 m clearance.
            Teleport(new Vector3(6.6f, 0.05f, 18f));
            yield return Wait(0.3f);
            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current, "No room to crouch under the prone tunnel");
            yield return Tap(keyboard.zKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current, "No room to stand under the prone tunnel");
            Assert.Less(CameraPivot.position.y, 0.85f);

            // Out in the open again: standing up works.
            Teleport(new Vector3(6.6f, 0.05f, 15f));
            yield return Wait(0.3f);
            yield return Tap(keyboard.zKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Standing, posture.Current);
        }

        /// <summary>Regression: the posture course has to be enterable by walking / crawling in from outside.</summary>
        [UnityTest]
        public IEnumerator Posture_CanWalkIntoCrouchTunnel_AndCrawlIntoProneTunnel()
        {
            yield return LoadScene(InputMode.Desktop);
            // Crouch tunnel spans z 10..14 (clearance 1.35 m): walk in crouched from z 8.6.
            Teleport(new Vector3(6.6f, 0.05f, 8.6f));
            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(1.5f);
            Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            Assert.Greater(player.transform.position.z, 10.6f, "Crouched player walks into the crouch tunnel");

            // Prone tunnel spans z 16..20 (clearance 0.85 m): crawl in prone from z 14.8.
            Teleport(new Vector3(6.6f, 0.05f, 14.8f));
            yield return Tap(keyboard.zKey);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current);
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(2.5f);
            Release(keyboard.wKey, queueEventOnly: true);
            yield return null;
            Assert.Greater(player.transform.position.z, 16.6f, "Prone player crawls into the prone tunnel");
        }

        [UnityTest]
        public IEnumerator Jump_FromCrouch_OnlyStandsUp_ThenJumpsAndRejumps()
        {
            yield return LoadScene(InputMode.Desktop);
            yield return Wait(0.3f);
            float groundY = FeetY;
            yield return Tap(keyboard.cKey);
            yield return WaitForPosture();

            float peak = FeetY;
            Press(keyboard.spaceKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.spaceKey, queueEventOnly: true);
            for (float end = Time.time + 0.6f; Time.time < end; )
            {
                peak = Mathf.Max(peak, FeetY);
                yield return null;
            }
            Assert.AreEqual(Posture.Standing, posture.Current);
            Assert.Less(peak - groundY, 0.05f, "The same press must not also jump");

            // Holding Space gives exactly one jump; mid-air posture input is refused.
            Press(keyboard.spaceKey, queueEventOnly: true);
            yield return Wait(0.15f);
            Assert.Greater(FeetY - groundY, 0.2f, "Standing: Space jumps");
            yield return Tap(keyboard.cKey);
            Assert.AreEqual(Posture.Standing, posture.Current, "No crouching in the air");
            yield return Wait(1.2f); // landed while still holding Space
            Assert.AreEqual(groundY, FeetY, 0.05f, "Holding Space must not jump again");
            Release(keyboard.spaceKey, queueEventOnly: true);
            yield return null;

            peak = groundY;
            Press(keyboard.spaceKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.spaceKey, queueEventOnly: true);
            for (float end = Time.time + 0.8f; Time.time < end; )
            {
                peak = Mathf.Max(peak, FeetY);
                yield return null;
            }
            Assert.Greater(peak - groundY, 0.6f, "Can jump again after landing");
        }

        [UnityTest]
        public IEnumerator Jump_HeadBump_FallsBackWithoutSticking()
        {
            yield return LoadScene(InputMode.Desktop);
            // Jump platform top is 0.8 m; JumpCeiling bottom is 3.0 m (2.2 m clearance).
            Teleport(new Vector3(5.5f, 0.9f, 5f));
            yield return Wait(0.5f);
            float standY = FeetY;
            Assert.AreEqual(0.8f, standY, 0.1f);

            float peak = standY;
            Press(keyboard.spaceKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.spaceKey, queueEventOnly: true);
            float start = Time.time;
            float landedAt = -1f;
            while (Time.time - start < 1.5f)
            {
                peak = Mathf.Max(peak, FeetY);
                if (landedAt < 0f && Time.time - start > 0.2f && motor.IsGrounded)
                    landedAt = Time.time - start;
                yield return null;
            }
            Assert.LessOrEqual(peak + 1.8f, 3.0f + 0.06f, "Head stops at the ceiling");
            Assert.Greater(landedAt, 0f, "Lands again (no sticking to the ceiling)");
            Assert.Less(landedAt, 0.9f, "Falls back promptly after the bump");
            Assert.AreEqual(standY, FeetY, 0.05f);
        }

        // ------------------------------------------------------------------ touch posture / sprint

        [UnityTest]
        public IEnumerator Touch_PostureButton_TapCrouch_HoldProne_Exclusive_WithStickHeld()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var button = GameObject.Find("PostureButton").GetComponent<TouchButton>();
            var stick = Object.FindAnyObjectByType<VirtualJoystick>();
            Vector2 p = ScreenCenter((RectTransform)button.transform);
            Vector2 stickPoint = ScreenCenter((RectTransform)stick.transform) * 0.6f;

            // Finger 1 keeps walking forward the whole time.
            BeginTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            MoveTouch(1, stickPoint + new Vector2(0f, Screen.height * 0.06f), queueEventOnly: true, screen: touchscreen);
            yield return null;
            Vector3 start = player.transform.position;

            // Tap: crouch.
            BeginTouch(2, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            EndTouch(2, p, queueEventOnly: true, screen: touchscreen);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Crouching, posture.Current);
            Assert.IsTrue(stick.IsActive, "Stick finger still tracked");

            // Hold: prone fires while held; the release must not add a tap.
            float yaw = motor.Yaw;
            BeginTouch(3, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.6f);
            Assert.AreEqual(Posture.Prone, posture.Current, "Long press goes prone while still held");
            MoveTouch(3, p + new Vector2(-120f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(3, p + new Vector2(-120f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Prone, posture.Current, "Long press must not also trigger the tap action");
            Assert.AreEqual(yaw, motor.Yaw, 0.001f, "Dragging from the posture button must not turn the view");

            // Hold again: prone -> stand.
            BeginTouch(4, p, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.6f);
            EndTouch(4, p, queueEventOnly: true, screen: touchscreen);
            yield return WaitForPosture();
            Assert.AreEqual(Posture.Standing, posture.Current);

            Assert.Greater(Vector3.Distance(start, player.transform.position), 0.3f, "Kept moving with the stick meanwhile");
            EndTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Touch_Sprint_StartsDeep_ReleasesWithHysteresis_ForwardAndStandingOnly()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var stick = Object.FindAnyObjectByType<VirtualJoystick>();
            Vector2 origin = ScreenCenter((RectTransform)stick.transform) * 0.6f;
            float unit = 110f * Screen.height / 1080f; // stick radius in screen pixels

            IEnumerator StickTo(Vector2 value)
            {
                MoveTouch(1, origin + value * unit, queueEventOnly: true, screen: touchscreen);
                yield return null;
                yield return null;
            }

            BeginTouch(1, origin, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return StickTo(new Vector2(0f, 0.85f));
            Assert.IsFalse(controls.SprintLatched, "0.85 forward is below the start threshold");
            yield return StickTo(new Vector2(0f, 0.95f));
            Assert.IsTrue(controls.SprintLatched);
            yield return Wait(0.1f);
            Assert.IsTrue(motor.IsSprinting);
            yield return StickTo(new Vector2(0f, 0.8f));
            Assert.IsTrue(controls.SprintLatched, "Small drop near the threshold keeps sprinting (hysteresis)");
            yield return StickTo(new Vector2(0f, 0.7f));
            Assert.IsFalse(controls.SprintLatched, "Below the release threshold sprint stops");
            yield return StickTo(new Vector2(1f, 0f));
            Assert.IsFalse(controls.SprintLatched, "Sideways does not sprint");
            yield return StickTo(new Vector2(0f, -1f));
            Assert.IsFalse(controls.SprintLatched, "Backwards does not sprint");

            // Crouched: full forward neither sprints nor stands up.
            posture.Request(Posture.Crouching);
            yield return WaitForPosture();
            yield return StickTo(new Vector2(0f, 1f));
            yield return Wait(0.3f);
            Assert.IsFalse(motor.IsSprinting);
            Assert.AreEqual(Posture.Crouching, posture.Current);
            EndTouch(1, origin, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Touch_Backgrounding_ReleasesHeldFireAndStick()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var fireButton = GameObject.Find("FireButton").GetComponent<TouchButton>();
            var stick = Object.FindAnyObjectByType<VirtualJoystick>();
            Vector2 firePoint = ScreenCenter((RectTransform)fireButton.transform);
            Vector2 stickPoint = ScreenCenter((RectTransform)stick.transform) * 0.6f;
            int fired = 0;
            gun.Fired += () => fired++;

            BeginTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            MoveTouch(1, stickPoint + new Vector2(0f, Screen.height * 0.06f), queueEventOnly: true, screen: touchscreen);
            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.25f);
            Assert.IsTrue(fireButton.IsHeld);
            Assert.IsTrue(stick.IsActive);
            Assert.Greater(fired, 0);

            // App goes to the background while both fingers are down; their release never arrives.
            router.SendMessage("OnApplicationPause", true);
            yield return null;
            router.SendMessage("OnApplicationPause", false);
            yield return null;
            Assert.IsFalse(fireButton.IsHeld, "FIRE must not stay held after backgrounding");
            Assert.IsFalse(stick.IsActive, "Stick must not stay held after backgrounding");
            int firedAfterResume = fired;
            Vector3 position = player.transform.position;
            yield return Wait(0.4f);
            Assert.AreEqual(firedAfterResume, fired, "No firing after returning");
            Assert.Less(Vector3.Distance(position, player.transform.position), 0.1f, "No movement after returning");

            // The stale fingers ending later changes nothing; a fresh tap works.
            EndTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            EndTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            BeginTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.2f);
            Assert.IsTrue(fireButton.IsHeld, "A new touch on FIRE works after returning");
            Assert.Greater(fired, firedAfterResume);
            EndTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        // ------------------------------------------------------------------ weapons

        IEnumerator SwitchTo(UnityEngine.InputSystem.Controls.ButtonControl key)
        {
            yield return Tap(key);
            yield return Wait(weapons.SwitchTime + 0.1f);
        }

        ShootingTarget SpawnTarget(Vector3 position)
        {
            var go = new GameObject("TestTarget");
            spawned.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.95f, 0f);
            box.size = new Vector3(0.8f, 1.9f, 0.3f);
            var target = go.AddComponent<ShootingTarget>();
            Physics.SyncTransforms();
            return target;
        }

        [UnityTest]
        public IEnumerator Switch_BlocksAttacks_MeleeHitsOnceWithinReach_NoAmmoUse()
        {
            yield return LoadScene(InputMode.Desktop);
            var target = GameObject.Find("MeleeTarget_Open").GetComponent<ShootingTarget>();
            int loaded = gun.Ammo.Loaded, reserve = gun.Ammo.Reserve;
            Teleport(new Vector3(-1.6f, 0.05f, 2.0f));
            yield return Wait(0.3f);

            // Input during the switch does nothing.
            yield return Tap(keyboard.digit2Key);
            Assert.IsTrue(weapons.IsSwitching);
            yield return Tap(mouse.leftButton);
            Assert.AreEqual(0, melee.SwingCount, "No swing while switching");
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "No shot while switching");
            yield return Wait(weapons.SwitchTime);
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            Assert.IsTrue(melee.IsEquipped);
            Assert.IsFalse(gun.IsEquipped);

            int hits = 0;
            melee.Hit += (c, r) => hits++;
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(1, melee.SwingCount);
            Assert.AreEqual(target.MaxHealth - melee.Definition.damage, target.Health, 0.01f, "Exactly one hit per swing");
            Assert.AreEqual(1, hits);

            // Mashing during a swing does not add swings.
            for (int i = 0; i < 6; i++)
                yield return Tap(mouse.leftButton);
            Assert.AreEqual(2, melee.SwingCount, "Presses during a swing are ignored");
            yield return Wait(melee.Definition.TotalTime + 0.1f);

            // Out of reach.
            float health = target.Health;
            Teleport(new Vector3(-1.6f, 0.05f, 0.4f));
            yield return Wait(0.2f);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(health, target.Health, 0.01f, "Target beyond reach is not hit");

            Assert.AreEqual(loaded, gun.Ammo.Loaded);
            Assert.AreEqual(reserve, gun.Ammo.Reserve, "Melee uses no ammo");
        }

        [UnityTest]
        public IEnumerator Melee_WallBlocks_OneTargetPerSwing_WorksProne()
        {
            yield return LoadScene(InputMode.Desktop);
            yield return SwitchTo(keyboard.digit2Key);

            // Thin wall between the player and MeleeTarget_BehindWall.
            var hidden = GameObject.Find("MeleeTarget_BehindWall").GetComponent<ShootingTarget>();
            Collider hitCollider = null;
            melee.Hit += (c, r) => hitCollider = c;
            Teleport(new Vector3(-6.2f, 0.05f, 2.6f));
            yield return Wait(0.3f);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(hidden.MaxHealth, hidden.Health, "No hit through the wall");
            Assert.IsNotNull(hitCollider);
            Assert.AreEqual("MeleeBlockWall", hitCollider.name);

            // Two targets in a line within reach: only the first is damaged.
            var first = SpawnTarget(new Vector3(0f, 0f, -1.9f));
            var second = SpawnTarget(new Vector3(0f, 0f, -2.3f));
            Teleport(new Vector3(0f, 0.05f, -0.9f), 180f);
            yield return Wait(0.3f);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.Less(first.Health, first.MaxHealth);
            Assert.AreEqual(second.MaxHealth, second.Health, "One target per swing");

            // Prone swing still reaches.
            yield return Tap(keyboard.zKey);
            yield return WaitForPosture();
            float before = first.Health;
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.Less(first.Health, before, "Melee works prone");

            // Swinging at nothing reports nothing (and never hits the player itself).
            hitCollider = null;
            Teleport(new Vector3(0f, 0.05f, 30f), 90f);
            yield return Wait(0.3f);
            yield return Tap(mouse.leftButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.IsNull(hitCollider);
        }

        [UnityTest]
        public IEnumerator Switch_DuringReload_CancelsWithoutAddingRounds_AndNoFireOnDraw()
        {
            yield return LoadScene(InputMode.Desktop);
            int fired = 0;
            gun.Fired += () => fired++;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.45f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            int loaded = gun.Ammo.Loaded, reserve = gun.Ammo.Reserve;
            Assert.Less(loaded, gun.Definition.magazineSize);

            yield return Tap(keyboard.rKey);
            Assert.IsTrue(gun.IsReloading);
            yield return Wait(gun.Definition.reloadTime * 0.5f);
            yield return Tap(keyboard.digit2Key);
            Assert.IsFalse(gun.IsReloading, "Switching away cancels the reload");
            Assert.AreEqual(loaded, gun.Ammo.Loaded);
            Assert.AreEqual(reserve, gun.Ammo.Reserve, "No rounds moved by a cancelled reload");
            yield return Wait(weapons.SwitchTime + gun.Definition.reloadTime);
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "The cancelled reload never completes later");

            // Back to the gun while holding the trigger through the switch: no shot until the trigger is pressed again.
            int firedBefore = fired;
            yield return Tap(keyboard.digit1Key);
            Assert.IsTrue(weapons.IsSwitching);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.IsFalse(gun.IsReloading, "Reload is not resumed automatically");
            Assert.AreEqual(firedBefore, fired, "Holding fire through the draw must not shoot");
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(firedBefore + 1, fired, "A fresh press fires");
            Assert.AreEqual(loaded - 1, gun.Ammo.Loaded);
            Assert.AreEqual(reserve, gun.Ammo.Reserve);

            // Posture changes do not touch ammo or a reload in progress.
            yield return Tap(keyboard.rKey);
            yield return Tap(keyboard.cKey);
            yield return Wait(gun.Definition.reloadTime + 0.2f);
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded);
            Assert.AreEqual(loaded - 1 + reserve, gun.Ammo.Loaded + gun.Ammo.Reserve, "Reload while crouching completes once");
        }

        [UnityTest]
        public IEnumerator Touch_WeaponButton_FireSwingsMelee_AdsAndReloadDisabled()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var weaponButton = GameObject.Find("WeaponButton").GetComponent<TouchButton>();
            var adsButton = GameObject.Find("AdsButton").GetComponent<TouchButton>();
            var reloadButton = GameObject.Find("ReloadButton").GetComponent<TouchButton>();
            var fireButton = GameObject.Find("FireButton").GetComponent<TouchButton>();
            int id = 1;
            IEnumerator TapButton(TouchButton b)
            {
                Vector2 p = ScreenCenter((RectTransform)b.transform);
                BeginTouch(id, p, queueEventOnly: true, screen: touchscreen);
                yield return null;
                EndTouch(id, p, queueEventOnly: true, screen: touchscreen);
                yield return null;
                id++;
            }

            yield return TapButton(weaponButton);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            Assert.IsFalse(adsButton.Interactable);
            Assert.IsFalse(reloadButton.Interactable);
            yield return TapButton(adsButton);
            yield return Wait(0.2f);
            Assert.IsFalse(controls.AdsToggled);
            Assert.IsFalse(gun.AdsRequested);

            int loaded = gun.Ammo.Loaded;
            yield return TapButton(fireButton);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(1, melee.SwingCount, "FIRE swings the knife");
            Assert.AreEqual(loaded, gun.Ammo.Loaded);

            yield return TapButton(weaponButton);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.IsTrue(adsButton.Interactable);
            Assert.IsTrue(reloadButton.Interactable);
        }
    }
}
