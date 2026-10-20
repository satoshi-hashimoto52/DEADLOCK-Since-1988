using Deadlock.Input;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    public class AdsInputTests
    {
        [Test]
        public void Hold_OnWhileHeld_OffOnRelease()
        {
            var a = new AdsInput();
            Assert.IsTrue(a.Update(AdsMode.Hold, true, true, true));
            Assert.IsTrue(a.Update(AdsMode.Hold, true, false, true));
            Assert.IsFalse(a.Update(AdsMode.Hold, false, false, true));
        }

        [Test]
        public void Toggle_FlipsOncePerPress_HoldingDoesNotRepeat()
        {
            var a = new AdsInput();
            Assert.IsTrue(a.Update(AdsMode.Toggle, true, true, true));
            for (int i = 0; i < 30; i++)
                Assert.IsTrue(a.Update(AdsMode.Toggle, true, false, true), "Held: no repeat");
            Assert.IsTrue(a.Update(AdsMode.Toggle, false, false, true), "Released: stays on");
            Assert.IsFalse(a.Update(AdsMode.Toggle, true, true, true), "Next press: off");
        }

        [Test]
        public void Cancel_NoAutomaticResume_UntilANewPress()
        {
            var hold = new AdsInput();
            hold.Update(AdsMode.Hold, true, true, true);
            hold.Cancel(stillHeld: true);
            Assert.IsFalse(hold.Update(AdsMode.Hold, true, false, true), "Still held after cancel: stays off");
            Assert.IsFalse(hold.Update(AdsMode.Hold, false, false, true));
            Assert.IsTrue(hold.Update(AdsMode.Hold, true, true, true), "New press");

            var toggle = new AdsInput();
            toggle.Update(AdsMode.Toggle, true, true, true);
            toggle.Cancel(stillHeld: false);
            Assert.IsFalse(toggle.Update(AdsMode.Toggle, false, false, true));
        }

        [Test]
        public void Unavailable_IgnoresInput_AndKeepsNothingForLater()
        {
            var a = new AdsInput();
            Assert.IsFalse(a.Update(AdsMode.Toggle, true, true, false), "Knife out: ignored");
            Assert.IsFalse(a.Update(AdsMode.Toggle, false, false, true), "Gun back: no queued toggle");
            var h = new AdsInput();
            Assert.IsFalse(h.Update(AdsMode.Hold, true, true, false));
            Assert.IsFalse(h.Update(AdsMode.Hold, true, false, true), "Held through the switch: needs a new press");
        }

        static readonly string[] Keys =
        {
            ControlSettings.TouchAdsKey, ControlSettings.DesktopAdsKey, ControlSettings.TouchAdsDragAimKey, ControlSettings.TouchFireModeKey,
        };

        [Test]
        public void Settings_Defaults_SavedSeparately_BrokenValuesFallBack()
        {
            var saved = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var k in Keys)
                if (PlayerPrefs.HasKey(k))
                    saved[k] = PlayerPrefs.GetInt(k);
            try
            {
                ControlSettings.ClearSaved();
                // Standard mobile controls when nothing is saved; PC unchanged.
                Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Touch), "Touch ADS: toggle (sights on / off)");
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "PC right button: hold, unchanged");
                Assert.IsFalse(ControlSettings.TouchAdsDragAim, "Aim with the ADS button: off");
                Assert.AreEqual(TouchFireMode.Normal, ControlSettings.TouchFire, "FIRE: normal fire");

                // The options stay selectable and saved choices win over the defaults.
                ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Hold);
                ControlSettings.TouchAdsDragAim = true;
                ControlSettings.TouchFire = TouchFireMode.AdsLinked;
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Touch));
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "PC unaffected");
                Assert.IsTrue(ControlSettings.TouchAdsDragAim);
                Assert.AreEqual(TouchFireMode.AdsLinked, ControlSettings.TouchFire);

                // Choosing the default explicitly is saved too (survives a later change of defaults).
                ControlSettings.ClearSaved();
                ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
                Assert.IsTrue(PlayerPrefs.HasKey(ControlSettings.TouchAdsKey));

                PlayerPrefs.SetInt(ControlSettings.DesktopAdsKey, 99);
                PlayerPrefs.SetInt(ControlSettings.TouchFireModeKey, 99);
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "Broken value: default");
                Assert.AreEqual(TouchFireMode.Normal, ControlSettings.TouchFire, "Broken value: default");
            }
            finally
            {
                ControlSettings.ClearSaved();
                foreach (var pair in saved)
                    PlayerPrefs.SetInt(pair.Key, pair.Value);
            }
        }
    }
}
