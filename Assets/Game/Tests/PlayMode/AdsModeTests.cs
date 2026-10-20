using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>ADS hold / toggle setting (Menu → Settings → Controls), for PC (right click) and touch (ADS button).</summary>
    public class AdsModeTests : FiringRangeTestBase
    {
        GameMenu menu;

        IEnumerator Load(InputMode mode)
        {
            yield return LoadScene(mode);
            menu = Object.FindAnyObjectByType<GameMenu>();
        }

        /// <summary>Chooses a mode through the real settings screen.</summary>
        IEnumerator ChooseInMenu(string buttonName)
        {
            menu.OpenControlSettings();
            yield return null;
            GameObject.Find(buttonName).GetComponent<Button>().onClick.Invoke();
            menu.CloseControlSettings();
            menu.Close();
            yield return null;
        }

        IEnumerator Click(UnityEngine.InputSystem.Controls.ButtonControl button) => Tap(button);

        [UnityTest]
        public IEnumerator Pc_HoldDefault_ToggleFromSettings_NoRepeatWhileHeld_SavedAndPcOnly()
        {
            yield return Load(InputMode.Desktop);
            Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "Default: hold");
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "Hold: on while pressed");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Hold: off on release");

            yield return ChooseInMenu("DesktopAdsRight");
            Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Desktop));
            Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Touch), "Touch setting untouched");
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.6f);
            Assert.IsTrue(gun.AdsRequested, "Toggle: on at the press, and holding does not flip it back");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "Toggle: stays on after release");
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Toggle: next press turns it off");

            // Restored on the next start (scene reload reads the saved setting).
            yield return Load(InputMode.Desktop);
            Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Desktop));
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "Toggle restored");
        }

        [UnityTest]
        public IEnumerator Touch_Hold_EndsEvenWhenReleasedOutsideTheButton_Toggle_OncePerPress()
        {
            yield return Load(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            Vector2 ads = ScreenCenter((RectTransform)GameObject.Find("AdsButton").transform);
            Vector2 outside = ads + new Vector2(-400f, 250f);

            BeginTouch(1, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(controls.AdsActive && gun.AdsRequested, "Hold: on while the finger is down");
            MoveTouch(1, outside, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested, "Still held after sliding off the button");
            EndTouch(1, outside, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Released outside the button: off");

            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
            Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "PC setting untouched");
            BeginTouch(2, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.6f);
            Assert.IsTrue(gun.AdsRequested, "Toggle: on, no repeat while held");
            EndTouch(2, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            BeginTouch(3, ads, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(3, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Toggle: off on the next tap");
        }

        [UnityTest]
        public IEnumerator Cancelled_ByMenu_Switch_Death_Background_ModeChange_NoAutoResume()
        {
            yield return Load(InputMode.Desktop);
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Toggle);

            // Menu.
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            yield return Tap(keyboard.tabKey);
            Assert.IsTrue(menu.IsOpen);
            yield return Tap(keyboard.tabKey);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Menu turns ADS off; it does not come back after closing");

            // Hold mode: the right button held through the menu does not resume ADS.
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Hold);
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            yield return Tap(keyboard.tabKey);
            yield return Tap(keyboard.tabKey);
            yield return Wait(0.2f);
            Assert.IsFalse(gun.AdsRequested, "Still holding after the menu: no ADS until a new press");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return null;
            yield return Click(mouse.rightButton);

            // Weapon switch: off, and not back on with the gun even while holding.
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.IsFalse(gun.AdsRequested);
            yield return Tap(keyboard.digit1Key);
            yield return Wait(weapons.SwitchTime + 0.2f);
            Assert.IsFalse(gun.AdsRequested, "Back to the gun while holding: needs a new press");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return null;

            // Knife out: presses are ignored and nothing is kept for later (toggle mode).
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Toggle);
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.1f);
            yield return Click(mouse.rightButton);
            yield return Tap(keyboard.digit1Key);
            yield return Wait(weapons.SwitchTime + 0.2f);
            Assert.IsFalse(gun.AdsRequested, "No toggle queued while the knife was out");

            // Background (app pause) and return.
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            router.SendMessage("OnApplicationPause", true);
            router.SendMessage("OnApplicationPause", false);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Backgrounding turns ADS off");

            // Input mode change (F2) and back.
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            router.SetMode(InputMode.Touch);
            yield return null;
            router.SetMode(InputMode.Desktop);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Changing the input mode turns ADS off");

            // Changing the setting.
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Hold);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Changing the mode turns ADS off");

            // Reload keeps the existing rule: ADS may stay on while reloading.
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(0.1f);
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.3f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return Tap(keyboard.rKey);
            yield return null;
            Assert.IsTrue(gun.IsReloading && gun.AdsRequested, "ADS during reload (existing rule)");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return null;

            // Death.
            yield return Click(mouse.leftButton);
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Toggle);
            yield return Wait(gun.Definition.reloadTime);
            yield return Click(mouse.rightButton);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            player.GetComponent<PlayerHealth>().TakeDamage(1000f, Vector3.zero);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Death turns ADS off");
        }

        [UnityTest]
        public IEnumerator Touch_MenuAndLayoutEditor_TurnAdsOff_FingerStillOnButtonDoesNotResume()
        {
            yield return Load(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            Vector2 ads = ScreenCenter((RectTransform)GameObject.Find("AdsButton").transform);
            BeginTouch(1, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            menu.Open();
            yield return null;
            menu.Close();
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Menu: off, and the finger still down does not bring it back");
            EndTouch(1, ads, queueEventOnly: true, screen: touchscreen);
            yield return null;

            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
            BeginTouch(2, ads, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(2, ads, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsFalse(gun.AdsRequested, "Layout editor: off");
            menu.GetComponent<TouchLayoutEditor>().CancelAndClose();
            menu.Close();
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested, "Not resumed after editing");
        }
    }
}
