using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>Movement, shooting, pickups and touch input in the prototype scene.</summary>
    public class FiringRangeSceneTests : FiringRangeTestBase
    {
        [UnityTest]
        public IEnumerator Desktop_MoveSprintJumpLook()
        {
            yield return LoadScene(InputMode.Desktop);

            Vector3 start = player.transform.position;
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(0.6f);
            Release(keyboard.wKey, queueEventOnly: true);
            float walked = Vector3.Distance(start, player.transform.position);
            Assert.Greater(walked, 1.5f, "W should move the player forward");
            Assert.Greater(player.transform.position.z, start.z);
            yield return Wait(0.3f);

            start = player.transform.position;
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(0.6f);
            Assert.IsTrue(motor.IsSprinting);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            float sprinted = Vector3.Distance(start, player.transform.position);
            Assert.Greater(sprinted, walked * 1.25f, "Sprint should be faster than walking");
            yield return Wait(0.3f);

            float groundY = player.transform.position.y;
            float peak = groundY;
            Press(keyboard.spaceKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.spaceKey, queueEventOnly: true);
            float end = Time.time + 0.8f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                yield return null;
            }
            Assert.Greater(peak - groundY, 0.6f, "Space should jump");
            Assert.AreEqual(groundY, player.transform.position.y, 0.05f, "Player should land again");

            float yaw = motor.Yaw;
            float pitch = motor.Pitch;
            Set(mouse.delta, new Vector2(100f, 50f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.Greater(motor.Yaw - yaw, 5f, "Mouse X should turn the view");
            Assert.Greater(motor.Pitch - pitch, 2f, "Mouse Y should pitch the view");
        }

        [UnityTest]
        public IEnumerator Desktop_AdsFireReload()
        {
            yield return LoadScene(InputMode.Desktop);
            WeaponDefinition def = gun.Definition;
            Camera cam = Camera.main;

            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(def.adsTime + 0.2f);
            Assert.AreEqual(1f, gun.AdsBlend, 0.001f);
            Assert.AreEqual(def.adsFov, cam.fieldOfView, 0.1f);
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(def.adsTime + 0.2f);
            Assert.AreEqual(0f, gun.AdsBlend, 0.001f);
            Assert.AreEqual(def.hipFov, cam.fieldOfView, 0.1f);

            int fired = 0;
            gun.Fired += () => fired++;
            int total = gun.Ammo.Loaded + gun.Ammo.Reserve;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.55f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.Greater(fired, 2, "Holding fire should shoot automatically");
            Assert.LessOrEqual(fired, Mathf.CeilToInt(0.6f / def.fireInterval) + 1, "Fire interval must limit the rate");
            Assert.AreEqual(def.magazineSize - fired, gun.Ammo.Loaded);
            Assert.AreEqual(total - fired, gun.Ammo.Loaded + gun.Ammo.Reserve);

            int totalBeforeReload = gun.Ammo.Loaded + gun.Ammo.Reserve;
            Press(keyboard.rKey, queueEventOnly: true);
            yield return null;
            Release(keyboard.rKey, queueEventOnly: true);
            yield return null;
            Assert.IsTrue(gun.IsReloading);

            // Firing is blocked during the reload.
            int loadedDuringReload = gun.Ammo.Loaded;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Release(mouse.leftButton, queueEventOnly: true);
            Assert.AreEqual(loadedDuringReload, gun.Ammo.Loaded);

            yield return Wait(def.reloadTime);
            Assert.IsFalse(gun.IsReloading);
            Assert.AreEqual(def.magazineSize, gun.Ammo.Loaded);
            Assert.AreEqual(totalBeforeReload, gun.Ammo.Loaded + gun.Ammo.Reserve, "Reload must not create rounds");
        }

        [UnityTest]
        public IEnumerator EmptyMagazine_CannotFire_UntilReloaded()
        {
            yield return LoadScene(InputMode.Desktop);
            WeaponDefinition def = gun.Definition;

            int fired = 0;
            gun.Fired += () => fired++;
            Press(mouse.leftButton, queueEventOnly: true);
            float timeout = Time.time + def.magazineSize * def.fireInterval + 2f;
            while (gun.Ammo.Loaded > 0 && Time.time < timeout)
                yield return null;
            Assert.AreEqual(0, gun.Ammo.Loaded);
            int firedAtEmpty = fired;

            // Still holding the trigger: nothing more comes out and nothing goes negative.
            yield return Wait(0.5f);
            Assert.AreEqual(firedAtEmpty, fired);
            Assert.AreEqual(0, gun.Ammo.Loaded);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;

            // A fresh trigger press on an empty magazine starts a reload instead of firing.
            Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(firedAtEmpty, fired);
            Assert.IsTrue(gun.IsReloading);
            Assert.GreaterOrEqual(gun.Ammo.Reserve, 0);
        }

        [UnityTest]
        public IEnumerator AmmoPickup_AddsToReserveExactlyOnce()
        {
            yield return LoadScene(InputMode.Desktop);
            var pickup = GameObject.Find("AmmoPickup_A").GetComponent<AmmoPickup>();
            int loaded = gun.Ammo.Loaded;
            int reserve = gun.Ammo.Reserve;
            Vector3 pickupPosition = pickup.transform.position;

            Teleport(pickupPosition);
            yield return Wait(0.3f);
            Assert.IsTrue(pickup.IsConsumed);
            Assert.IsFalse(pickup.gameObject.activeSelf);
            Assert.AreEqual(reserve + pickup.Amount, gun.Ammo.Reserve);
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "Pickups go to reserve, not the magazine");

            Teleport(new Vector3(0f, 0.05f, 0f));
            yield return Wait(0.2f);
            Teleport(pickupPosition);
            yield return Wait(0.3f);
            Assert.AreEqual(reserve + pickup.Amount, gun.Ammo.Reserve, "A consumed pickup must not add again");
        }

        [UnityTest]
        public IEnumerator SceneGeometry_OccluderWallStopsShots_VisibleTargetTakesDamage()
        {
            yield return LoadScene(InputMode.Desktop);
            var hidden = GameObject.Find("Target_Hidden").GetComponent<ShootingTarget>();
            var near = GameObject.Find("Target_Near").GetComponent<ShootingTarget>();
            Vector3 eye = Camera.main.transform.position;

            ProjectileHit? hit = null;
            projectiles.Hit += h => hit = h;

            projectiles.Fire(eye, hidden.transform.position + Vector3.up - eye, 250f, 200f, 25f, player);
            yield return Wait(0.5f);
            Assert.IsTrue(hit.HasValue);
            Assert.AreEqual("OccluderWall", hit.Value.Collider.name);
            Assert.AreEqual(hidden.MaxHealth, hidden.Health);

            hit = null;
            projectiles.Fire(eye, near.transform.position + Vector3.up - eye, 250f, 200f, 25f, player);
            yield return Wait(0.5f);
            Assert.IsTrue(hit.HasValue);
            Assert.AreSame(near.gameObject, hit.Value.Collider.gameObject);
            Assert.Less(near.Health, near.MaxHealth);
        }

        [UnityTest]
        public IEnumerator Touch_TwoFingersMoveAndLook_ButtonsDoNotLook()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;

            var stick = Object.FindAnyObjectByType<VirtualJoystick>();
            var reloadButton = GameObject.Find("ReloadButton").GetComponent<TouchButton>();

            Vector2 stickPoint = ScreenCenter((RectTransform)stick.transform) * new Vector2(0.6f, 0.6f);
            Vector2 lookPoint = new Vector2(Screen.width * 0.62f, Screen.height * 0.75f);
            float stickTravel = Screen.height * 0.2f;

            Vector3 start = player.transform.position;
            float yaw = motor.Yaw;

            // Finger 1: move stick forward. Finger 2: drag the view at the same time.
            BeginTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            MoveTouch(1, stickPoint + new Vector2(0f, stickTravel), queueEventOnly: true, screen: touchscreen);
            yield return null;
            BeginTouch(2, lookPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            for (int i = 1; i <= 5; i++)
            {
                MoveTouch(2, lookPoint + new Vector2(i * 20f, 0f), queueEventOnly: true, screen: touchscreen);
                yield return null;
            }
            Assert.IsTrue(stick.IsActive, "Stick finger must stay tracked while another finger looks");
            Assert.Greater(stick.Value.y, 0.5f);
            Assert.Greater(motor.Yaw - yaw, 1f, "Second finger should rotate the view");
            yield return Wait(0.4f);
            Assert.Greater(Vector3.Distance(start, player.transform.position), 0.5f, "Stick should move the player");

            EndTouch(2, lookPoint + new Vector2(100f, 0f), queueEventOnly: true, screen: touchscreen);
            EndTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return null;
            Assert.IsFalse(stick.IsActive);
            Assert.AreEqual(Vector2.zero, stick.Value);

            // A finger that starts on a button and slides must not turn the view.
            yaw = motor.Yaw;
            Vector2 buttonPoint = ScreenCenter((RectTransform)reloadButton.transform);
            BeginTouch(3, buttonPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.IsTrue(reloadButton.IsHeld);
            for (int i = 1; i <= 5; i++)
            {
                MoveTouch(3, buttonPoint + new Vector2(-i * 25f, 0f), queueEventOnly: true, screen: touchscreen);
                yield return null;
            }
            Assert.AreEqual(yaw, motor.Yaw, 0.001f, "Dragging from a button must not rotate the view");
            EndTouch(3, buttonPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return null;
            Assert.IsFalse(reloadButton.IsHeld);
        }

        [UnityTest]
        public IEnumerator Touch_AdsButtonToggles_And_DesktopInputIsIgnored()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var adsButton = GameObject.Find("AdsButton").GetComponent<TouchButton>();
            Vector2 point = ScreenCenter((RectTransform)adsButton.transform);

            BeginTouch(1, point, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(1, point, queueEventOnly: true, screen: touchscreen);
            yield return Wait(gun.Definition.adsTime + 0.15f);
            Assert.IsTrue(gun.AdsRequested);
            Assert.AreEqual(1f, gun.AdsBlend, 0.001f);

            BeginTouch(2, point, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.IsTrue(adsButton.IsHeld);
            EndTouch(2, point, queueEventOnly: true, screen: touchscreen);
            yield return Wait(gun.Definition.adsTime + 0.15f);
            Assert.IsFalse(gun.AdsRequested);
            Assert.AreEqual(0f, gun.AdsBlend, 0.001f);

            // Keyboard/mouse must not act while touch mode is active (no double input).
            // Touches after a mouse click are covered by Touch_ButtonsStillWork_WithPhysicalMouse.
            Vector3 position = player.transform.position;
            Press(keyboard.wKey, queueEventOnly: true);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(mouse.leftButton, queueEventOnly: true);
            Assert.AreEqual(position.x, player.transform.position.x, 0.01f);
            Assert.AreEqual(position.z, player.transform.position.z, 0.01f);
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded);
        }

        public enum MouseInterference
        {
            /// <summary>Mouse click, then (a few frames later) touches.</summary>
            ClickThenTouch,
            /// <summary>Mouse button release and a new touch arrive in the same input update.</summary>
            ReleaseInSameUpdateAsTouchBegin,
            /// <summary>A touch begins while the mouse button is held; the mouse is released first.</summary>
            TouchWhileMouseHeld,
            /// <summary>The mouse button is pressed during a held touch and released after the touch ends.</summary>
            MouseClickDuringHeldTouch,
        }

        /// <summary>
        /// Regression: in touch mode a physical mouse (e.g. iPad + mouse, or a desktop player) must neither act
        /// itself nor disturb the touch buttons, whatever the timing between mouse and finger.
        /// </summary>
        [UnityTest]
        public IEnumerator Touch_ButtonsStillWork_WithPhysicalMouse([Values] MouseInterference order)
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var adsButton = GameObject.Find("AdsButton").GetComponent<TouchButton>();
            var fireButton = GameObject.Find("FireButton").GetComponent<TouchButton>();
            var reloadButton = GameObject.Find("ReloadButton").GetComponent<TouchButton>();
            Vector2 adsPoint = ScreenCenter((RectTransform)adsButton.transform);
            Vector2 firePoint = ScreenCenter((RectTransform)fireButton.transform);
            Vector2 reloadPoint = ScreenCenter((RectTransform)reloadButton.transform);
            int fired = 0;
            gun.Fired += () => fired++;
            Vector3 position = player.transform.position;

            // The physical mouse hovers over the FIRE button: the worst place for it to be clicked.
            Set(mouse.position, firePoint, queueEventOnly: true);
            yield return null;

            bool expectAdsOn;
            switch (order)
            {
                case MouseInterference.ClickThenTouch:
                    Press(mouse.leftButton, queueEventOnly: true);
                    yield return Wait(0.2f);
                    Release(mouse.leftButton, queueEventOnly: true);
                    yield return null;
                    yield return null;
                    expectAdsOn = false;
                    break;
                case MouseInterference.ReleaseInSameUpdateAsTouchBegin:
                    Press(mouse.leftButton, queueEventOnly: true);
                    yield return Wait(0.2f);
                    Release(mouse.leftButton, queueEventOnly: true);
                    BeginTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    Assert.IsTrue(adsButton.IsHeld, "Touch beginning with the mouse release must reach the button");
                    EndTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    yield return null;
                    expectAdsOn = true;
                    break;
                case MouseInterference.TouchWhileMouseHeld:
                    Press(mouse.leftButton, queueEventOnly: true);
                    yield return null;
                    BeginTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    Release(mouse.leftButton, queueEventOnly: true);
                    yield return null;
                    Assert.IsTrue(adsButton.IsHeld, "Releasing the mouse must not release the finger's button");
                    EndTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    yield return null;
                    expectAdsOn = true;
                    break;
                default: // MouseClickDuringHeldTouch
                    BeginTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    Press(mouse.leftButton, queueEventOnly: true);
                    yield return null;
                    EndTouch(10, adsPoint, queueEventOnly: true, screen: touchscreen);
                    yield return null;
                    Release(mouse.leftButton, queueEventOnly: true);
                    yield return null;
                    yield return null;
                    expectAdsOn = true;
                    break;
            }

            Assert.IsFalse(adsButton.IsHeld, "Lifting the finger must release the button");
            Assert.IsFalse(fireButton.IsHeld, "The mouse must not hold the FIRE button");
            Assert.AreEqual(expectAdsOn, controls.AdsToggled);
            Assert.AreEqual(0, fired, "The mouse must not shoot in touch mode");
            Assert.AreEqual(position.x, player.transform.position.x, 0.01f);
            Assert.AreEqual(position.z, player.transform.position.z, 0.01f);

            // The next touches all work.
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.IsTrue(adsButton.IsHeld, "Next touch must reach the ADS button");
            EndTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return null;
            Assert.IsFalse(adsButton.IsHeld);
            Assert.AreEqual(!expectAdsOn, controls.AdsToggled, "ADS must toggle on the next tap");

            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.35f);
            Assert.IsTrue(fireButton.IsHeld);
            Assert.Greater(fired, 0, "Touch FIRE must shoot");
            EndTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return null;
            Assert.IsFalse(fireButton.IsHeld);
            int firedAtRelease = fired;
            yield return Wait(0.3f);
            Assert.AreEqual(firedAtRelease, fired, "Lifting the finger must stop firing");

            BeginTouch(3, reloadPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(3, reloadPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.IsTrue(gun.IsReloading, "Touch RELOAD must start a reload");
        }

        /// <summary>Editor F2 path: the mouse drives a simulated touchscreen, which must still reach the touch buttons.</summary>
        [UnityTest]
        public IEnumerator F2_TouchSimulation_MouseClickReachesTouchButtons()
        {
            yield return LoadScene(InputMode.Desktop);
            var controls = Object.FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);
            var adsButton = GameObject.Find("AdsButton");
            Assert.IsNull(adsButton, "Touch controls are hidden in desktop mode");

            Press(keyboard.f2Key, queueEventOnly: true);
            yield return null;
            Release(keyboard.f2Key, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(InputMode.Touch, router.Mode);
            Assert.IsTrue(UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null
                          && UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled);

            Vector2 adsPoint = ScreenCenter((RectTransform)GameObject.Find("AdsButton").transform);
            Set(mouse.position, adsPoint, queueEventOnly: true);
            yield return null;
            Press(mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return null;
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.IsTrue(controls.AdsToggled, "A simulated touch from the mouse must press ADS");
            Assert.AreEqual(gun.Definition.magazineSize, gun.Ammo.Loaded, "The click must not also fire via desktop input");

            Press(keyboard.f2Key, queueEventOnly: true);
            yield return null;
            Release(keyboard.f2Key, queueEventOnly: true);
            yield return null;
            Assert.AreEqual(InputMode.Desktop, router.Mode);
            Assert.IsFalse(UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null
                           && UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled);
        }
    }
}
