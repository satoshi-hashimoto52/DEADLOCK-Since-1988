using System.Collections.Generic;
using Deadlock.Home;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Home panel fill transparency: defaults, 0–50 % range, saving, reset, border on small panels.</summary>
    public class HomePanelLookTests
    {
        readonly Dictionary<HomePanelTarget, int?> before = new Dictionary<HomePanelTarget, int?>();

        [SetUp]
        public void SetUp()
        {
            foreach (var t in HomePanelLook.Targets)
            {
                before[t] = PlayerPrefs.HasKey(HomePanelLook.Key(t)) ? PlayerPrefs.GetInt(HomePanelLook.Key(t)) : (int?)null;
                PlayerPrefs.DeleteKey(HomePanelLook.Key(t));
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in before)
            {
                if (pair.Value.HasValue)
                    PlayerPrefs.SetInt(HomePanelLook.Key(pair.Key), pair.Value.Value);
                else
                    PlayerPrefs.DeleteKey(HomePanelLook.Key(pair.Key));
            }
        }

        [Test]
        public void Defaults_AreTheUserApproved50Percent()
        {
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.MainButton));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Points));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Settings));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Crate));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Window));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Row));
            Assert.AreEqual(6, HomePanelLook.Targets.Length);
        }

        [Test]
        public void Values_Are0To50Percent_In1PercentSteps_Saved_AndReset()
        {
            int changed = 0;
            void OnChanged() => changed++;
            HomePanelLook.Changed += OnChanged;
            try
            {
                HomePanelLook.Set(HomePanelTarget.Window, 17);
                Assert.AreEqual(17, HomePanelLook.Get(HomePanelTarget.Window));
                Assert.AreEqual(17, PlayerPrefs.GetInt(HomePanelLook.Key(HomePanelTarget.Window)), "kept on the device");
                HomePanelLook.Set(HomePanelTarget.Window, 80);
                Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Window));
                HomePanelLook.Set(HomePanelTarget.Window, -5);
                Assert.AreEqual(0, HomePanelLook.Get(HomePanelTarget.Window));
                HomePanelLook.ResetAll();
                Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Window));
                Assert.AreEqual(4, changed, "every change is announced");
            }
            finally
            {
                HomePanelLook.Changed -= OnChanged;
            }
            Assert.AreEqual(1f, HomePanelLook.FillAlpha(0));
            Assert.AreEqual(0.88f, HomePanelLook.FillAlpha(12), 1e-5f);
            Assert.AreEqual(0.5f, HomePanelLook.FillAlpha(50));
            Assert.AreEqual(0.5f, HomePanelLook.FillAlpha(90), "never more than 50 %");
        }

        [Test]
        public void SavedOverride_IsKept_UntilResetForThatTargetOnly()
        {
            // A value saved on the device earlier (as the 50 % on the test iPhone) is read as it is, never replaced by the default.
            PlayerPrefs.SetInt(HomePanelLook.Key(HomePanelTarget.MainButton), 50);
            PlayerPrefs.SetInt(HomePanelLook.Key(HomePanelTarget.Points), 50);
            Assert.IsTrue(HomePanelLook.OverridesEnabled, "the Editor reads the development overrides");
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.MainButton));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Settings), "no saved value: the default");
            HomePanelLook.Reset(HomePanelTarget.MainButton);
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.MainButton));
            Assert.IsFalse(PlayerPrefs.HasKey(HomePanelLook.Key(HomePanelTarget.MainButton)));
            Assert.AreEqual(50, HomePanelLook.Get(HomePanelTarget.Points), "other targets keep theirs");
        }

        [Test]
        public void Border_Is20DesignPx_SmallerOnSmallPanels()
        {
            Assert.AreEqual(20f, HomePanelPart.BorderUnits(new Vector2(490f, 136f)));
            Assert.AreEqual(20f, HomePanelPart.BorderUnits(new Vector2(74f, 70f)));
            Assert.AreEqual(35f * 0.3f, HomePanelPart.BorderUnits(new Vector2(37f, 35f)), 1e-4f, "Settings at 0.5x: corners stay off the icon");
        }
    }
}
