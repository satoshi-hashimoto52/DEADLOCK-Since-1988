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
        public void ClampCircle_KeepsTheWholeButtonInsideTheSafeArea_TopIncluded()
        {
            foreach (var size in SafeSizes)
            {
                float d = 240f;
                Vector2 c = TouchLayout.ClampCircle(new Vector2(2f, 2f), d, size);
                Assert.LessOrEqual(c.x * size.x + d / 2f, size.x + 0.01f);
                Assert.LessOrEqual(c.y * size.y + d / 2f, size.y + 0.01f);
                Assert.GreaterOrEqual(c.y * size.y + d / 2f, size.y - 0.01f, "Up to the top edge (no band reserved)");
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

        // iPhone 16 Pro (2622x1206 → canvas 2348x1080; Safe Area without the Dynamic Island / home indicator) and
        // iPad 4:3 (2048x1536 → 1440x1080). Rotating left / right keeps the same Safe Area size.
        static readonly Vector2 Phone = new Vector2(2140f, 1020f), Tablet = new Vector2(1440f, 1080f);

        static TouchLayoutData WithCircle(TouchLayoutData data, string id, Vector2 center)
        {
            var item = data.Get(id);
            item.center = center;
            data.Set(item);
            return data;
        }

        [Test]
        public void FireAndAds_InTheTopCorners_AreUsable_OnPhoneAndTablet()
        {
            foreach (var size in new[] { Phone, Tablet })
            {
                var data = TouchLayout.Defaults();
                // Pushed into the top corners (clamped inside the Safe Area), for index-finger use.
                WithCircle(data, TouchLayout.Fire, TouchLayout.ClampCircle(new Vector2(2f, 2f), TouchLayout.Diameter(data.Get(TouchLayout.Fire)), size));
                WithCircle(data, TouchLayout.Ads, TouchLayout.ClampCircle(new Vector2(-1f, 2f), TouchLayout.Diameter(data.Get(TouchLayout.Ads)), size));
                WithCircle(data, TouchLayout.Weapon, new Vector2(0.62f, 0.62f)); // out of FIRE's way
                Assert.IsTrue(TouchLayout.IsUsable(data, size, out string problem), $"{size}: {problem}");
            }
        }

        [Test]
        public void MenuButton_AndItsMargin_StayFree_ButTheSameHeightBesideItIsAllowed()
        {
            foreach (var size in new[] { Phone, Tablet, SafeSizes[0] })
            {
                Rect guard = TouchLayout.MenuGuard(size);
                var ads = TouchLayout.Defaults().Get(TouchLayout.Ads);
                float r = TouchLayout.Diameter(ads) * 0.5f;

                // Right on the menu button: refused.
                var data = WithCircle(TouchLayout.Defaults(), TouchLayout.Ads, new Vector2(0.5f, (size.y - r) / size.y));
                Assert.IsFalse(TouchLayout.IsUsable(data, size, out string problem), $"{size}");
                StringAssert.Contains("メニューボタン", problem);

                // Inside the margin only (just left of the button): refused.
                float buttonLeft = size.x * 0.5f - TouchLayout.MenuButtonSize.x * 0.5f;
                data = WithCircle(TouchLayout.Defaults(), TouchLayout.Ads, new Vector2((buttonLeft - r - 10f) / size.x, (size.y - r) / size.y));
                Assert.IsFalse(TouchLayout.IsUsable(data, size, out _), $"{size}: within the margin");

                // At the menu's height, just clear of the margin: allowed.
                data = WithCircle(TouchLayout.Defaults(), TouchLayout.Ads, new Vector2((guard.xMin - r - 2f) / size.x, (size.y - r) / size.y));
                Assert.IsTrue(TouchLayout.IsUsable(data, size, out problem), $"{size}: {problem}");
                data = WithCircle(TouchLayout.Defaults(), TouchLayout.Ads, new Vector2((guard.xMax + r + 2f) / size.x, (size.y - r) / size.y));
                Assert.IsTrue(TouchLayout.IsUsable(data, size, out problem), $"{size}: {problem}");
            }
        }

        [Test]
        public void MoveZone_MayReachTheTop_ButNotTheMenuButton()
        {
            var data = TouchLayout.Defaults();
            var zone = data.Get(TouchLayout.MoveZone);
            zone.size = new Vector2(0.4f, 1f);
            zone.center = new Vector2(0.2f, 0.5f); // left 40 %, full height
            data.Set(zone);
            Assert.IsTrue(TouchLayout.IsUsable(data, Phone, out string problem), problem);
            zone.size = new Vector2(0.6f, 1f);
            zone.center = new Vector2(0.3f, 0.5f); // reaches the centre at the top
            data.Set(zone);
            Assert.IsFalse(TouchLayout.IsUsable(data, Phone, out problem));
            StringAssert.Contains("メニューボタン", problem);
        }

        [Test]
        public void ExistingLayouts_StayUsable_AndALayoutWorksOnBothScreenShapes()
        {
            // Layouts saved under the old rule (below 84 %) remain valid.
            foreach (var size in SafeSizes)
                Assert.IsTrue(TouchLayout.IsUsable(TouchLayout.Defaults(), size, out string problem), problem);
            // Stored as fractions of the Safe Area: a top-corner layout made on the phone is still usable on the tablet.
            var data = TouchLayout.Defaults();
            WithCircle(data, TouchLayout.Fire, TouchLayout.ClampCircle(new Vector2(2f, 2f), TouchLayout.Diameter(data.Get(TouchLayout.Fire)), Phone));
            WithCircle(data, TouchLayout.Weapon, new Vector2(0.62f, 0.62f));
            Assert.IsTrue(TouchLayout.IsUsable(data, Phone, out string p1), p1);
            Assert.IsTrue(TouchLayout.IsUsable(data, Tablet, out string p2), p2);
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
