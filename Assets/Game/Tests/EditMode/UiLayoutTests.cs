using System.Linq;
using Deadlock.Home;
using Deadlock.UI.Layout;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Free UI layout: normalised coordinates, aspect ratios, save / load, edit session, version, safe area.</summary>
    public class UiLayoutTests
    {
        const string TestLayoutId = "test_ui_layout";
        static readonly UiLayoutSpec Button = new UiLayoutSpec("b", new Vector2(300f, 76f));

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(UiLayoutStore.Key(TestLayoutId));

        static UiLayoutItem Item(float x, float y, float scale = 1f) => new UiLayoutItem { id = "b", center = new Vector2(x, y), scale = scale, visible = true };

        [Test]
        public void NormalizedCoordinates_RoundTrip_AndFollowTheScreen()
        {
            var canvas = new Vector2(2046f, 941f);
            var p = UiLayoutMath.ToCanvas(new Vector2(0.25f, 0.5f), canvas);
            Assert.AreEqual(new Vector2(511.5f, 470.5f), p);
            Assert.AreEqual(new Vector2(0.25f, 0.5f), UiLayoutMath.ToNormalized(p, canvas));
            // same normalised point on a 4:3 canvas
            Assert.AreEqual(new Vector2(313.675f, 470.5f).x, UiLayoutMath.ToCanvas(new Vector2(0.25f, 0.5f), new Vector2(1254.7f, 941f)).x, 1e-3f);
        }

        [TestCase(2046f, 941f)]   // iPhone 16 Pro (2622x1206) at canvas height 941
        [TestCase(1672f, 941f)]   // 16:9
        [TestCase(1254.7f, 941f)] // 4:3
        public void KeepOnScreen_PullsAnOffScreenElementBack_AnyAspect(float w, float h)
        {
            var canvas = new Vector2(w, h);
            var item = UiLayoutMath.KeepOnScreen(Item(1.3f, -0.2f), Button, canvas);
            Assert.IsTrue(UiLayoutMath.Contains(new Rect(Vector2.zero, canvas), UiLayoutMath.ItemRect(item, Button, canvas)));
            // the black margins / outside the safe area are allowed: a corner stays a corner
            var corner = UiLayoutMath.KeepOnScreen(Item(0f, 1f), Button, canvas);
            var r = UiLayoutMath.ItemRect(corner, Button, canvas);
            Assert.AreEqual(0f, r.xMin, 1e-3f);
            Assert.AreEqual(h, r.yMax, 1e-3f);
        }

        [Test]
        public void Scale_IsClamped_AndKeepsTheAspect()
        {
            Assert.AreEqual(0.6f, UiLayoutMath.ClampScale(0.1f, Button));
            Assert.AreEqual(1.6f, UiLayoutMath.ClampScale(9f, Button));
            var size = UiLayoutMath.Size(Item(0.5f, 0.5f, 1.3f), Button);
            Assert.AreEqual(300f / 76f, size.x / size.y, 1e-4f);
        }

        [Test]
        public void SafeArea_InsideAndOutside()
        {
            var canvas = new Vector2(2046f, 941f);
            var safe = UiLayoutMath.SafeRectInCanvas(new Rect(186, 63, 2250, 1143), new Vector2(2622, 1206), canvas);
            Assert.AreEqual(145.14f, safe.xMin, 0.05f);
            Assert.AreEqual(49.15f, safe.yMin, 0.05f);
            Assert.IsTrue(UiLayoutMath.InsideSafeArea(Item(0.5f, 0.5f), Button, canvas, safe));
            Assert.IsFalse(UiLayoutMath.InsideSafeArea(Item(0.05f, 0.5f), Button, canvas, safe), "in the left margin, outside the safe area");
        }

        [Test]
        public void SaveAndLoad_PositionScaleVisibilityVersion()
        {
            var data = new UiLayoutData { layoutVersion = 3 };
            data.items.Add(new UiLayoutItem { id = "b", center = new Vector2(0.12f, 0.87f), scale = 1.4f, visible = false });
            UiLayoutStore.Save(TestLayoutId, data);
            var loaded = UiLayoutStore.Load(TestLayoutId);
            Assert.AreEqual(3, loaded.layoutVersion);
            var item = loaded.Get("b");
            Assert.AreEqual(new Vector2(0.12f, 0.87f), item.center);
            Assert.AreEqual(1.4f, item.scale);
            Assert.IsFalse(item.visible);
        }

        [Test]
        public void Load_BrokenOrMissing_IsNull()
        {
            Assert.IsNull(UiLayoutStore.Load(TestLayoutId));
            PlayerPrefs.SetString(UiLayoutStore.Key(TestLayoutId), "{not json");
            Assert.IsNull(UiLayoutStore.Load(TestLayoutId));
        }

        [Test]
        public void Resolve_OtherVersion_UsesDefaults_SavedDataUntouched()
        {
            var defaults = new UiLayoutData { layoutVersion = 2 };
            defaults.items.Add(Item(0.5f, 0.5f));
            var saved = new UiLayoutData { layoutVersion = 1 };
            saved.items.Add(Item(0.1f, 0.1f));
            var result = UiLayoutMath.Resolve(saved, defaults, 2, out bool usedDefaults);
            Assert.IsTrue(usedDefaults);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), result.Get("b").center);
            Assert.AreEqual(new Vector2(0.1f, 0.1f), saved.Get("b").center);
            saved.layoutVersion = 2;
            result = UiLayoutMath.Resolve(saved, defaults, 2, out usedDefaults);
            Assert.IsFalse(usedDefaults);
            Assert.AreEqual(new Vector2(0.1f, 0.1f), result.Get("b").center);
        }

        [Test]
        public void Resolve_MissingIdsFromDefaults_UnknownIdsDropped()
        {
            var defaults = new UiLayoutData { layoutVersion = 1 };
            defaults.items.Add(Item(0.5f, 0.5f));
            defaults.items.Add(new UiLayoutItem { id = "c", center = new Vector2(0.2f, 0.2f), scale = 1f, visible = true });
            var saved = new UiLayoutData { layoutVersion = 1 };
            saved.items.Add(Item(0.3f, 0.3f));
            saved.items.Add(new UiLayoutItem { id = "gone", center = Vector2.zero, scale = 1f, visible = true });
            var result = UiLayoutMath.Resolve(saved, defaults, 1, out _);
            CollectionAssert.AreEqual(new[] { "b", "c" }, result.items.Select(i => i.id));
            Assert.AreEqual(new Vector2(0.3f, 0.3f), result.Get("b").center);
        }

        [Test]
        public void EditSession_Move_Resize_Cancel_Reset_Commit()
        {
            var canvas = new Vector2(2046f, 941f);
            var current = new UiLayoutData { layoutVersion = 1 };
            current.items.Add(Item(0.5f, 0.5f));
            var defaults = new UiLayoutData { layoutVersion = 1 };
            defaults.items.Add(Item(0.2f, 0.2f));
            var session = new UiLayoutEditSession(current, () => defaults, new[] { Button });
            session.Move("b", new Vector2(204.6f, 0f), canvas);
            Assert.AreEqual(0.6f, session.Draft.Get("b").center.x, 1e-4f);
            session.SetScale("b", 5f, canvas);
            Assert.AreEqual(1.6f, session.Draft.Get("b").scale);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), session.Cancel().Get("b").center, "cancel: the layout before editing");
            Assert.AreEqual(1f, session.Draft.Get("b").scale);
            session.ResetToDefaults();
            Assert.AreEqual(new Vector2(0.2f, 0.2f), session.Draft.Get("b").center);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), current.Get("b").center, "the shown layout is not changed by the session");
            Assert.AreEqual(new Vector2(0.2f, 0.2f), session.Commit().Get("b").center);
        }

        static (Vector2 canvas, Rect safe) TestScreen(int w, int h)
        {
            var canvas = new Vector2(941f * w / h, 941f);
            var safe = w == 2622 ? UiLayoutMath.SafeRectInCanvas(new Rect(186, 63, 2250, 1143), new Vector2(w, h), canvas) : new Rect(Vector2.zero, canvas);
            return (canvas, safe);
        }

        static UiLayoutSpec HomeSpec(string id) => HomeMenu.Specs.First(s => s.id == id);

        [TestCase(2622, 1206)]
        [TestCase(1920, 1080)]
        [TestCase(2048, 1536)]
        public void HomeDefaults_InsideSafeArea_NoOverlap_ClearOfWindowAndCandle(int w, int h)
        {
            var (canvas, safe) = TestScreen(w, h);
            var data = HomeMenu.Defaults(canvas, safe);
            CollectionAssert.AreEqual(new[] { HomeMenu.Logo, HomeMenu.Survival, HomeMenu.Training, HomeMenu.Event, HomeMenu.Gear, HomeMenu.Points, HomeMenu.Settings, HomeMenu.Crate },
                data.items.Select(i => i.id));
            Assert.AreEqual(2, data.layoutVersion);
            var rects = data.items.Select(i => UiLayoutMath.ItemRect(i, HomeSpec(i.id), canvas)).ToArray();
            foreach (var r in rects)
                Assert.IsTrue(UiLayoutMath.Contains(safe, r), $"{r} outside the safe area {safe}");
            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), $"{data.items[i].id} overlaps {data.items[j].id}");
            // window opening and candle (with glow) of the background as framed (Fit), in canvas units
            float s = Mathf.Min(canvas.x / 1672f, canvas.y / 941f), x0 = (canvas.x - 1672f * s) / 2f, y0 = (canvas.y - 941f * s) / 2f;
            Rect Ref(float x, float y, float rw, float rh) => new Rect(x0 + x * s, canvas.y - y0 - (y + rh) * s, rw * s, rh * s);
            foreach (var important in new[] { Ref(1306, 164, 116, 228), Ref(1448, 457, 132, 175) })
                foreach (var r in rects)
                    Assert.IsFalse(r.Overlaps(important), $"{r} covers {important}");
        }

        [TestCase(2622, 1206)]
        [TestCase(2048, 1536)]
        public void HomeDefaults_FourMainButtons_SameSize_LeftAligned_GearSetApart(int w, int h)
        {
            var (canvas, safe) = TestScreen(w, h);
            var data = HomeMenu.Defaults(canvas, safe);
            var r = HomeMenu.MainButtons.Select(id => UiLayoutMath.ItemRect(data.Get(id), HomeSpec(id), canvas)).ToArray();
            foreach (var x in r)
            {
                Assert.AreEqual(r[0].size.x, x.size.x, 1e-3f, "same width");
                Assert.AreEqual(r[0].size.y, x.size.y, 1e-3f, "same height");
                Assert.AreEqual(r[0].xMin, x.xMin, 1e-3f, "left edges aligned");
            }
            float gap1 = r[0].yMin - r[1].yMax, gap2 = r[1].yMin - r[2].yMax, gearGap = r[2].yMin - r[3].yMax;
            Assert.AreEqual(gap1, gap2, 1e-3f, "Survival / Training / Event equally spaced");
            Assert.Greater(gearGap, gap1 * 2f, "Gear is set apart below Event");
            float s = Mathf.Min(canvas.x / 1672f, canvas.y / 941f);
            Assert.AreEqual(490f * s, r[0].width, 1e-2f, "design width 490 (reference px)");
            Assert.AreEqual(18f * s, gap1, 1e-2f);
            Assert.AreEqual(59f * s, gearGap, 1e-2f);
        }

        [Test]
        public void HomeSpecs_MainButtonsShareOneSize()
        {
            foreach (var id in HomeMenu.MainButtons)
                Assert.AreEqual(HomeMenu.MainButtonSize, HomeSpec(id).baseSize, id);
        }

        [Test]
        public void HomeMenu_NamesIds_Version()
        {
            Assert.AreEqual(2, HomeMenu.LayoutVersion);
            CollectionAssert.AreEqual(new[] { "survival", "training", "event", "gear" }, HomeMenu.MainButtons);
            CollectionAssert.AreEqual(new[] { "Survival", "Training", "Event", "Gear", "Crate" },
                new[] { HomeMenu.Survival, HomeMenu.Training, HomeMenu.Event, HomeMenu.Gear, HomeMenu.Crate }.Select(HomeMenu.DisplayName));
        }

        [Test]
        public void SavedVersion1Layout_IsNotUsed_NorOverwritten_ByVersion2()
        {
            var v1 = new UiLayoutData { layoutVersion = 1 };
            v1.items.Add(new UiLayoutItem { id = "mode", center = new Vector2(0.1f, 0.6f), scale = 1f, visible = true });
            UiLayoutStore.Save(TestLayoutId, v1);
            var (canvas, safe) = TestScreen(2622, 1206);
            var shown = UiLayoutMath.Resolve(UiLayoutStore.Load(TestLayoutId), HomeMenu.Defaults(canvas, safe), HomeMenu.LayoutVersion, out bool usedDefaults);
            Assert.IsTrue(usedDefaults);
            Assert.AreEqual(8, shown.items.Count);
            Assert.AreEqual(1, UiLayoutStore.Load(TestLayoutId).layoutVersion, "the saved v1 data stays as it was");
        }

        [Test]
        public void GearTabs_And_EventEntries()
        {
            CollectionAssert.AreEqual(new[] { "Loadout", "Skin", "Skill" }, HomeMenu.GearTabs);
            var events = HomeMenu.EventEntries();
            CollectionAssert.AreEqual(new[] { "Ruins（廃墟）", "Assault（急襲）" }, events.Select(e => e.name));
            Assert.AreEqual(HomeEntryStatus.Test, events[0].status);
            Assert.IsNotNull(events[0].open);
            Assert.AreEqual(HomeEntryStatus.ComingSoon, events[1].status);
            Assert.IsNull(events[1].open, "no game mode is started for Assault");
            var survival = HomeMenu.SurvivalEntries();
            Assert.IsTrue(survival.Where(e => e.status == HomeEntryStatus.ComingSoon).All(e => e.open == null));
            Assert.AreEqual(1, survival.Count(e => e.status == HomeEntryStatus.Test));
            Assert.AreEqual(2, HomeMenu.TrainingEntries().Length);
        }
    }
}
