using Deadlock.Input;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    public class TouchLayoutTests
    {
        // Safe Area sizes in canvas units (reference height 1080): 16:9, iPad 4:3, iPhone 16 Pro landscape minus
        // the Dynamic Island / home-indicator insets.
        static readonly Vector2[] SafeSizes = { new Vector2(1920f, 1080f), new Vector2(1440f, 1080f), new Vector2(2140f, 1020f) };

        [Test]
        public void Defaults_AreUsable_OnPhoneTabletAndWideScreens()
        {
            foreach (var size in SafeSizes)
                Assert.IsTrue(TouchLayout.IsUsable(TouchLayout.Defaults(), size, out string problem), $"{size}: {problem}");
        }

        [Test]
        public void ClampCircle_KeepsTheWholeButtonInsideAndBelowTheMenuBand()
        {
            foreach (var size in SafeSizes)
            {
                float d = 240f;
                Vector2 c = TouchLayout.ClampCircle(new Vector2(2f, 2f), d, size);
                Assert.LessOrEqual(c.x * size.x + d / 2f, size.x + 0.01f);
                Assert.LessOrEqual(c.y * size.y + d / 2f, TouchLayout.TopLimit * size.y + 0.01f);
                c = TouchLayout.ClampCircle(new Vector2(-1f, -1f), d, size);
                Assert.GreaterOrEqual(c.x * size.x - d / 2f, -0.01f);
                Assert.GreaterOrEqual(c.y * size.y - d / 2f, -0.01f);
            }
        }

        [Test]
        public void Overlapping_Buttons_And_StickOutsideZone_AreRejected()
        {
            var data = TouchLayout.Defaults();
            var fire = data.Get(TouchLayout.Fire);
            fire.center = data.Get(TouchLayout.Ads).center;
            data.Set(fire);
            Assert.IsFalse(TouchLayout.IsUsable(data, SafeSizes[0], out _));

            data = TouchLayout.Defaults();
            var stick = data.Get(TouchLayout.MoveStick);
            stick.center = new Vector2(0.5f, 0.6f); // right of the 40 % wide zone, clear of every button
            data.Set(stick);
            Assert.IsFalse(TouchLayout.IsUsable(data, SafeSizes[0], out string problem));
            StringAssert.Contains("操作開始領域", problem);
        }

        [Test]
        public void SaveLoad_RoundTrips_AndBrokenDataFallsBackToDefaults()
        {
            string original = PlayerPrefs.GetString(TouchLayout.PrefsKey, null);
            try
            {
                var data = TouchLayout.Defaults();
                var jump = data.Get(TouchLayout.Jump);
                jump.center = new Vector2(0.5f, 0.5f);
                jump.scale = 1.3f;
                data.Set(jump);
                TouchLayout.Save(data);
                var loaded = TouchLayout.Load().Get(TouchLayout.Jump);
                Assert.AreEqual(0.5f, loaded.center.x, 1e-4f);
                Assert.AreEqual(1.3f, loaded.scale, 1e-4f);

                PlayerPrefs.SetString(TouchLayout.PrefsKey, "{not json");
                Assert.AreEqual(TouchLayout.Defaults().Get(TouchLayout.Jump).center, TouchLayout.Load().Get(TouchLayout.Jump).center);
            }
            finally
            {
                if (string.IsNullOrEmpty(original))
                    TouchLayout.ClearSaved();
                else
                    PlayerPrefs.SetString(TouchLayout.PrefsKey, original);
            }
        }
    }
}
