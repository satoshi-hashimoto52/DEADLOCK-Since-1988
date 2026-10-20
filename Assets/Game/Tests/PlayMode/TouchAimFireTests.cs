using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Touch aiming with the ADS / FIRE buttons, ADS-linked fire, independent ADS requests and the look-finger rules.
    /// </summary>
    public class TouchAimFireTests : FiringRangeTestBase
    {
        Touchscreen touchscreen;
        TouchControls controls;
        TouchLookArea lookArea;
        Vector2 adsPoint, firePoint, emptyPoint, jumpPoint;
        int shots;

        IEnumerator LoadTouch()
        {
            yield return LoadScene(InputMode.Touch);
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            controls = Object.FindAnyObjectByType<TouchControls>();
            lookArea = Object.FindAnyObjectByType<TouchLookArea>();
            adsPoint = ScreenCenter((RectTransform)GameObject.Find("AdsButton").transform);
            firePoint = ScreenCenter((RectTransform)GameObject.Find("FireButton").transform);
            jumpPoint = ScreenCenter((RectTransform)GameObject.Find("JumpButton").transform);
            emptyPoint = new Vector2(Screen.width * 0.62f, Screen.height * 0.72f); // right side, no button
            shots = 0;
            gun.Fired += () => shots++;
        }

        IEnumerator Drag(int id, Vector2 from, Vector2 by, int steps = 5)
        {
            for (int i = 1; i <= steps; i++)
            {
                MoveTouch(id, from + by * i / steps, queueEventOnly: true, screen: touchscreen);
                yield return null;
            }
        }

        IEnumerator Tap(int id, Vector2 point)
        {
            BeginTouch(id, point, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(id, point, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdsButtonDrag_AimsWhenOn_NotWhenOff_NeverFires()
        {
            yield return LoadTouch();
            Assert.IsTrue(ControlSettings.TouchAdsDragAim, "Default on");
            float yaw = motor.Yaw;
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return Drag(1, adsPoint, new Vector2(-200f, 0f));
            Assert.IsTrue(gun.AdsRequested, "Hold: ADS while the finger is down");
            Assert.AreNotEqual(yaw, motor.Yaw, "Dragging from the ADS button aims");
            EndTouch(1, adsPoint + new Vector2(-200f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested);
            Assert.AreEqual(0, shots, "The ADS button never fires");

            ControlSettings.TouchAdsDragAim = false;
            yaw = motor.Yaw;
            BeginTouch(2, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return Drag(2, adsPoint, new Vector2(-200f, 0f));
            Assert.IsTrue(gun.AdsRequested);
            Assert.AreEqual(yaw, motor.Yaw, 0.001f, "OFF: the ADS button only holds ADS");
            EndTouch(2, adsPoint + new Vector2(-200f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdsToggle_DragAndLeavingTheButton_DoNotToggleAgain_HoldEndsOnCancel()
        {
            yield return LoadTouch();
            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            // Out of the button and back in, and a long hold: still exactly one toggle.
            yield return Drag(1, adsPoint, new Vector2(-300f, 120f));
            yield return Drag(1, adsPoint + new Vector2(-300f, 120f), new Vector2(300f, -120f));
            yield return Wait(0.6f);
            Assert.IsTrue(controls.AdsFromButton);
            EndTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "Toggle stays on after the finger lifts");
            yield return Tap(2, adsPoint);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Next press: off");

            // Hold mode: an OS-cancelled touch ends it.
            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Hold);
            BeginTouch(3, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            CancelTouch(3, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Cancelled touch: ADS off");
            Assert.AreEqual(0, lookArea.LookerCount, "Cancelled touch no longer aims");
        }

        [UnityTest]
        public IEnumerator Fire_Normal_ShootsFromCurrentStance_AdsLinked_WaitsForFullAim_NoLateShot()
        {
            yield return LoadTouch();
            // Normal: hip fire at once.
            BeginTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.05f);
            Assert.Greater(shots, 0, "Normal fire shoots at once from the hip");
            Assert.IsFalse(gun.AdsRequested);
            EndTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.2f);

            // ADS-linked: no shot before fully aimed, then shooting while aiming; release stops and drops ADS.
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            shots = 0;
            float yaw = motor.Yaw;
            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            float pressTime = Time.time;
            float firstShot = -1f;
            gun.Fired += () => { if (firstShot < 0f) firstShot = Time.time; };
            while (Time.time - pressTime < gun.Definition.adsTime * 0.8f)
            {
                Assert.AreEqual(0, shots, "No shot while the sights are still rising");
                yield return null;
            }
            yield return Drag(2, firePoint, new Vector2(-150f, 0f), 10);
            Assert.AreNotEqual(yaw, motor.Yaw, "The FIRE finger aims during ADS-linked fire");
            Assert.Greater(shots, 0, "Shoots once fully aimed");
            Assert.AreEqual(1f, gun.AdsBlend, 0.001f);
            Assert.GreaterOrEqual(firstShot - pressTime, gun.Definition.adsTime - 0.02f, "First shot after the full ADS time");
            EndTouch(2, firePoint + new Vector2(-150f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            int after = shots;
            yield return Wait(0.3f);
            Assert.AreEqual(after, shots, "Release stops firing");
            Assert.IsFalse(gun.AdsRequested, "Release drops the ADS that FIRE asked for");

            // Let go before fully aimed: never a late shot.
            shots = 0;
            BeginTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(gun.Definition.adsTime * 0.4f);
            EndTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.5f);
            Assert.AreEqual(0, shots, "Released before fully aimed: no shot, not even later");
        }

        [UnityTest]
        public IEnumerator AdsLinked_FirstShotTime_DoesNotDependOnFrameRate()
        {
            yield return LoadTouch();
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            float[] delays = new float[2];
            float[] frameTimes = { 1f / 60f, 1f / 30f };
            for (int r = 0; r < 2; r++)
            {
                Time.captureDeltaTime = frameTimes[r];
                yield return Wait(0.6f);
                float first = -1f;
                System.Action onFire = () => { if (first < 0f) first = Time.time; };
                gun.Fired += onFire;
                BeginTouch(10 + r, firePoint, queueEventOnly: true, screen: touchscreen);
                yield return null;
                float press = Time.time;
                while (first < 0f && Time.time - press < 1f)
                    yield return null;
                gun.Fired -= onFire;
                EndTouch(10 + r, firePoint, queueEventOnly: true, screen: touchscreen);
                delays[r] = first - press;
                yield return Wait(0.5f);
            }
            Time.captureDeltaTime = 0f;
            Assert.Greater(delays[0], 0f);
            Assert.AreEqual(delays[0], delays[1], 1f / 30f + 0.005f, $"60 fps {delays[0]:0.000}s vs 30 fps {delays[1]:0.000}s");
        }

        [UnityTest]
        public IEnumerator IndependentAdsRequests_ReleasingOneKeepsTheOther()
        {
            yield return LoadTouch();
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            // ADS button held, then FIRE (ADS-linked) held; let go of FIRE: still ADS from the button.
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.3f);
            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.2f);
            Assert.IsTrue(controls.AdsFromButton && controls.AdsFromFire);
            Assert.Greater(shots, 0, "Already fully aimed by the ADS button: shoots at once");
            EndTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "FIRE released: the ADS button still holds ADS");

            // FIRE held, then the ADS button released: still ADS (and firing) from FIRE.
            BeginTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            EndTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            int before = shots;
            yield return Wait(0.2f);
            Assert.IsTrue(gun.AdsRequested, "ADS button released: FIRE still holds ADS");
            Assert.Greater(shots, before, "Still firing");
            EndTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested);

            // Toggle ADS on, FIRE (ADS-linked) pressed and released: the toggle is not cleared.
            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
            yield return Tap(4, adsPoint);
            yield return Wait(0.3f);
            BeginTouch(5, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.15f);
            EndTouch(5, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "FIRE does not clear ADS kept by the ADS toggle");
        }

        [UnityTest]
        public IEnumerator Knife_FireStillSwings_InAdsLinkedMode()
        {
            yield return LoadTouch();
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.1f);
            yield return Tap(1, firePoint);
            yield return Wait(melee.Definition.TotalTime + 0.1f);
            Assert.AreEqual(1, melee.SwingCount);
            Assert.IsFalse(gun.AdsRequested);
            Assert.IsFalse(controls.AdsFromFire);
        }

        [UnityTest]
        public IEnumerator MultipleFingers_FirstLookFingerWins_HandOverWithoutJump_NoDoubleCount_ButtonsDoNotLook()
        {
            yield return LoadTouch();
            var stickPoint = new Vector2(Screen.width * 0.12f, Screen.height * 0.3f);
            // Move with the stick, look with the empty area, then also drag with FIRE.
            BeginTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            MoveTouch(1, stickPoint + new Vector2(0f, 120f), queueEventOnly: true, screen: touchscreen);
            BeginTouch(2, emptyPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            BeginTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.AreEqual(2, lookArea.LookerCount);

            // Both look fingers move by the same amount in the same frames: the view turns once, not twice.
            float yaw = motor.Yaw;
            Vector3 position = player.transform.position;
            for (int i = 1; i <= 5; i++)
            {
                MoveTouch(2, emptyPoint + new Vector2(-40f * i, 0f), queueEventOnly: true, screen: touchscreen);
                MoveTouch(3, firePoint + new Vector2(-40f * i, 0f), queueEventOnly: true, screen: touchscreen);
                yield return null;
            }
            float oneFinger = Mathf.DeltaAngle(yaw, motor.Yaw);
            Assert.Greater(Mathf.Abs(oneFinger), 1f);
            Assert.Greater(Vector3.Distance(position, player.transform.position), 0.1f, "Still moving");
            Assert.Greater(shots, 0, "Still firing");

            // Hand-over: the first look finger lifts; the FIRE finger takes over from where it is (no jump).
            EndTouch(2, emptyPoint + new Vector2(-200f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return null;
            float before = motor.Yaw;
            yield return null;
            // FIRE keeps shooting, so recoil recovery moves the view slightly; a jump would be the finger's whole travel.
            Assert.AreEqual(before, motor.Yaw, 0.5f, "No jump at the hand-over");
            MoveTouch(3, firePoint + new Vector2(-240f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return null;
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(before, motor.Yaw)), 0.1f, "The FIRE finger now aims");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(before, motor.Yaw)), Mathf.Abs(oneFinger), "Only its own small step, not its whole travel");
            EndTouch(3, firePoint + new Vector2(-240f, 0f), queueEventOnly: true, screen: touchscreen);
            EndTouch(1, stickPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;

            // A finger that starts on JUMP never aims.
            yaw = motor.Yaw;
            BeginTouch(4, jumpPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            yield return Drag(4, jumpPoint, new Vector2(-250f, 0f));
            Assert.AreEqual(yaw, motor.Yaw, 0.5f, "A drag from JUMP would turn the view by degrees (the rest is recoil recovery)");
            EndTouch(4, jumpPoint + new Vector2(-250f, 0f), queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeldFireAndAds_ClearedByMenuDeathSwitchBackground_NoResumeWithHeldFinger()
        {
            yield return LoadTouch();
            ControlSettings.TouchFire = TouchFireMode.AdsLinked;
            var menu = Object.FindAnyObjectByType<GameMenu>();

            // Menu with FIRE and ADS held.
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            BeginTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            Assert.Greater(shots, 0);
            menu.Open();
            yield return null;
            menu.Close();
            yield return Wait(0.3f);
            int s = shots;
            yield return Wait(0.3f);
            Assert.AreEqual(s, shots, "No firing with fingers held through the menu");
            Assert.IsFalse(gun.AdsRequested, "No ADS either");
            EndTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            EndTouch(2, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;

            // Background.
            BeginTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            router.SendMessage("OnApplicationPause", true);
            router.SendMessage("OnApplicationPause", false);
            yield return Wait(0.2f);
            s = shots;
            yield return Wait(0.3f);
            Assert.AreEqual(s, shots, "No firing after returning from the background");
            Assert.IsFalse(gun.AdsRequested);
            EndTouch(3, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;

            // Weapon switch with FIRE held: back on the gun, the held finger does not fire.
            BeginTouch(4, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.1f);
            weapons.Request(WeaponSlot.Gun);
            yield return Wait(weapons.SwitchTime + 0.1f);
            s = shots;
            yield return Wait(0.4f);
            Assert.AreEqual(s, shots, "No firing with the finger held through the switch");
            EndTouch(4, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;

            // Death.
            BeginTouch(5, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.4f);
            player.GetComponent<PlayerHealth>().TakeDamage(1000f, Vector3.zero);
            yield return Wait(0.1f);
            s = shots;
            yield return Wait(0.3f);
            Assert.AreEqual(s, shots);
            Assert.IsFalse(gun.AdsRequested);
        }

        [UnityTest]
        public IEnumerator Settings_SavedAndRestored_LayoutKept_FromTheMenu()
        {
            yield return LoadTouch();
            var menu = Object.FindAnyObjectByType<GameMenu>();
            // A saved layout must survive changing control settings.
            var layout = TouchLayout.Defaults();
            var fire = layout.Get(TouchLayout.Fire);
            fire.center += new Vector2(-0.02f, 0.01f);
            layout.Set(fire);
            TouchLayout.Save(layout);

            menu.OpenControlSettings();
            yield return null;
            GameObject.Find("DragAimRight").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            GameObject.Find("FireModeRight").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            GameObject.Find("TouchAdsRight").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            menu.CloseControlSettings();
            menu.Close();
            Assert.IsFalse(ControlSettings.TouchAdsDragAim);
            Assert.AreEqual(TouchFireMode.AdsLinked, ControlSettings.TouchFire);
            Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Touch));
            Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "PC unchanged");

            yield return LoadTouch(); // next start
            Assert.IsFalse(ControlSettings.TouchAdsDragAim);
            Assert.AreEqual(TouchFireMode.AdsLinked, ControlSettings.TouchFire);
            Assert.AreEqual(fire.center, TouchLayout.Load().Get(TouchLayout.Fire).center, "Layout kept");
            // And it behaves accordingly: ADS-linked fire does not shoot at once.
            BeginTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.05f);
            Assert.AreEqual(0, shots);
            EndTouch(1, firePoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }
    }
}
