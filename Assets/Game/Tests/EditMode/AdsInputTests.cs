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

        [Test]
        public void Settings_SavedSeparatelyForTouchAndPc_DefaultHold()
        {
            int touch = PlayerPrefs.GetInt(ControlSettings.TouchAdsKey, -1);
            int desktop = PlayerPrefs.GetInt(ControlSettings.DesktopAdsKey, -1);
            try
            {
                ControlSettings.ClearSaved();
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Touch));
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop));
                ControlSettings.SetAdsMode(InputMode.Touch, AdsMode.Toggle);
                Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Touch));
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "PC unaffected");
                PlayerPrefs.SetInt(ControlSettings.DesktopAdsKey, 99);
                Assert.AreEqual(AdsMode.Hold, ControlSettings.GetAdsMode(InputMode.Desktop), "Broken value: default");
            }
            finally
            {
                ControlSettings.ClearSaved();
                if (touch >= 0) PlayerPrefs.SetInt(ControlSettings.TouchAdsKey, touch);
                if (desktop >= 0) PlayerPrefs.SetInt(ControlSettings.DesktopAdsKey, desktop);
            }
        }
    }
}
