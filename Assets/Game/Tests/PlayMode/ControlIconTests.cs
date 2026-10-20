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
    /// <summary>Control icons: state display, and that they never change what the buttons do.</summary>
    public class ControlIconTests : FiringRangeTestBase
    {
        static readonly string[] ButtonNames = { "FireButton", "AdsButton", "ReloadButton", "JumpButton", "PostureButton", "WeaponButton" };

        [UnityTest]
        public IEnumerator Icons_FollowWeaponPostureAndActualAds_AndTakeNoTouches()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var icons = AssetIcons();
            TouchButton B(string name) => GameObject.Find(name).GetComponent<TouchButton>();

            foreach (var name in ButtonNames)
            {
                var icon = B(name).Icon;
                Assert.IsNotNull(icon, name);
                Assert.IsNotNull(icon.sprite, name);
                Assert.IsFalse(icon.raycastTarget, $"{name}: the icon never takes the touch");
                Assert.IsTrue(icon.preserveAspect, $"{name}: aspect kept");
                Assert.AreEqual(B(name).transform, icon.transform.parent, $"{name}: icon is a child of the button");
            }
            Assert.AreEqual(icons.fire, B("FireButton").Icon.sprite);
            Assert.AreEqual(icons.stand, B("PostureButton").Icon.sprite);
            Assert.AreEqual(icons.weaponSwap, B("WeaponButton").Icon.sprite);

            // Gun ⇄ knife.
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(icons.melee, B("FireButton").Icon.sprite);
            weapons.Request(WeaponSlot.Gun);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.AreEqual(icons.fire, B("FireButton").Icon.sprite);

            // Posture icon = current posture.
            posture.Request(Posture.Crouching);
            yield return Wait(0.3f);
            Assert.AreEqual(icons.crouch, B("PostureButton").Icon.sprite);
            posture.Request(Posture.Prone);
            yield return Wait(0.5f);
            Assert.AreEqual(icons.prone, B("PostureButton").Icon.sprite);
            posture.Request(Posture.Standing);
            yield return Wait(0.5f);
            Assert.AreEqual(icons.stand, B("PostureButton").Icon.sprite);

            // ADS display = the gun's actual ADS state, in toggle (standard) and hold.
            var ads = B("AdsButton");
            Vector2 adsPoint = ScreenCenter((RectTransform)ads.transform);
            BeginTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(1, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested);
            Assert.IsTrue(ads.IsLatched, "Toggle on: shown active");
            BeginTouch(2, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return null;
            EndTouch(2, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested);
            Assert.IsFalse(ads.IsLatched, "Toggle off: shown inactive");
            ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Hold);
            BeginTouch(3, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(gun.AdsRequested && ads.IsLatched, "Hold: active while held");
            EndTouch(3, adsPoint, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsFalse(gun.AdsRequested || ads.IsLatched, "Hold: inactive after release");

            // Disabled (knife out): the ADS button looks disabled and does not take presses.
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.1f);
            Assert.IsFalse(ads.Interactable);
            Assert.Less(ads.Icon.color.a, 0.5f, "Disabled icon is faded");
        }

        [UnityTest]
        public IEnumerator Slide_ShowsTheSlideIcon()
        {
            yield return LoadScene(InputMode.Desktop);
            var controls = Object.FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);
            var postureButton = controls.transform.Find("SafeArea/PostureButton").GetComponent<TouchButton>();
            controls.SetVisible(true);
            Teleport(new Vector3(1f, 0.05f, -4f));
            yield return Wait(0.3f);
            Press(keyboard.leftShiftKey, queueEventOnly: true);
            Press(keyboard.wKey, queueEventOnly: true);
            yield return Wait(0.5f);
            yield return Tap(keyboard.cKey);
            Assert.IsTrue(posture.IsSliding);
            controls.Read(); // the display is refreshed when the touch controls are read
            Assert.AreEqual(AssetIcons().slide, postureButton.Icon.sprite);
            Release(keyboard.wKey, queueEventOnly: true);
            Release(keyboard.leftShiftKey, queueEventOnly: true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResizingInTheLayoutEditor_KeepsIconInsideItsButton_AndTheTouchAreaIsThePlate()
        {
            yield return LoadScene(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var menu = Object.FindAnyObjectByType<GameMenu>();
            var editor = menu.GetComponent<TouchLayoutEditor>();
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.Resize(TouchLayout.Fire, +1));
            Assert.IsTrue(editor.Resize(TouchLayout.Fire, +1));
            editor.ApplyAndClose();
            menu.Close();
            yield return null;

            var fire = GameObject.Find("FireButton").GetComponent<TouchButton>();
            var buttonRect = (RectTransform)fire.transform;
            var iconRect = fire.Icon.rectTransform;
            Assert.AreEqual(0.76f, iconRect.rect.width / buttonRect.rect.width, 0.01f, "Icon box scales with the button");
            Assert.AreEqual(ScreenCenter(buttonRect), ScreenCenter(iconRect), "Icon centred on the button");

            // A touch near the plate's edge (outside the icon box) still presses FIRE.
            int shots = 0;
            gun.Fired += () => shots++;
            Vector2 edge = ScreenCenter(buttonRect) + new Vector2(buttonRect.rect.width * 0.44f * Object.FindAnyObjectByType<Canvas>().scaleFactor, 0f);
            BeginTouch(1, edge, queueEventOnly: true, screen: touchscreen);
            yield return Wait(0.1f);
            Assert.IsTrue(fire.IsHeld);
            Assert.Greater(shots, 0);
            EndTouch(1, edge, queueEventOnly: true, screen: touchscreen);
            yield return null;
        }

        static ControlIconSet AssetIcons()
        {
            var controls = Object.FindAnyObjectByType<TouchControls>(FindObjectsInactive.Include);
            var field = typeof(TouchControls).GetField("icons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (ControlIconSet)field.GetValue(controls);
        }
    }
}
