using System.Linq;
using Deadlock.Editor;
using Deadlock.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Home background framing (Fit on every aspect, height-matched on iPhone), candle frame sequences, home scene wiring.</summary>
    public class HomeScreenTests
    {
        // candle body and flame in the 1672x941 reference (home_layout.json)
        static readonly Rect CandleBody = new Rect(1466, 539, 88, 93);
        static readonly Rect Flame = new Rect(1494, 498, 40, 44);

        [TestCase(2622, 1206)] // iPhone 16 Pro
        [TestCase(2556, 1179)] // iPhone 15 / 16
        [TestCase(2400, 1080)] // 20:9
        [TestCase(1920, 1080)] // 16:9
        [TestCase(1672, 941)]
        [TestCase(1920, 1200)] // 16:10
        [TestCase(2048, 1536)] // iPad 4:3
        [TestCase(2732, 2048)]
        public void Mode_IsFitOnEveryAspect(int w, int h) => Assert.AreEqual(HomeFramingMode.Fit, HomeFraming.Choose(w, h));

        [Test]
        public void Fit_iPhone16Pro_MatchesScreenHeight_NoCrop_SideMarginsEqual()
        {
            Assert.IsTrue(HomeFraming.FitsHeight(2622, 1206));
            var v = HomeFraming.Visible(HomeFramingMode.Fit, 2622, 1206);
            float s = HomeFraming.Scale(HomeFramingMode.Fit, 2622, 1206);
            Assert.AreEqual(1206f / 941f, s, 1e-5f, "reference height = screen height");
            Assert.AreEqual(0f, v.y, 1e-3f);
            Assert.AreEqual(941f, v.height, 1e-2f, "no top / bottom crop");
            Assert.AreEqual(-v.x, v.xMax - 1672f, 1e-3f, "left and right margins are equal");
            Assert.AreEqual(239.6f, -v.x * s, 0.1f, "margin in screen px");
        }

        /// <summary>Cover (D-045, replaced by D-046) stays computable for comparison.</summary>
        [Test]
        public void Cover_iPhone16Pro_FillsWidth_CropsTopBottomEqually()
        {
            var v = HomeFraming.Visible(HomeFramingMode.Cover, 2622, 1206);
            Assert.AreEqual(1.56818f, HomeFraming.Scale(HomeFramingMode.Cover, 2622, 1206), 1e-4f);
            Assert.AreEqual(0f, v.x, 1e-3f);
            Assert.AreEqual(1672f, v.width, 1e-2f);
            Assert.AreEqual(85.98f, v.y, 0.01f);
            Assert.AreEqual(v.y, 941f - v.yMax, 1e-3f, "top and bottom crop are equal");
        }

        [Test]
        public void Fit_iPad_ShowsWholeReference()
        {
            var v = HomeFraming.Visible(HomeFramingMode.Fit, 2048, 1536);
            Assert.AreEqual(0f, v.x, 1e-3f);
            Assert.AreEqual(1672f, v.width, 1e-2f);
            Assert.LessOrEqual(v.y, 0f);
            Assert.GreaterOrEqual(v.yMax, 941f);
        }

        [TestCase(2622, 1206)]
        [TestCase(2400, 1080)]
        [TestCase(1920, 1080)]
        [TestCase(1920, 1200)]
        [TestCase(2048, 1536)]
        [TestCase(2732, 2048)]
        public void ChosenMode_KeepsCandleAndFlameOnScreen(int w, int h)
        {
            var v = HomeFraming.Visible(HomeFraming.Choose(w, h), w, h);
            foreach (var r in new[] { CandleBody, Flame })
                Assert.IsTrue(v.xMin <= r.xMin && r.xMax <= v.xMax && v.yMin <= r.yMin && r.yMax <= v.yMax, $"{r} outside {v} at {w}x{h}");
        }

        [Test]
        public void Cover_On4x3_WouldCutTheCandle_SoItIsNotUsedThere()
        {
            var v = HomeFraming.Visible(HomeFramingMode.Cover, 2048, 1536);
            Assert.Less(v.xMax, CandleBody.xMax);
            Assert.AreEqual(HomeFramingMode.Fit, HomeFraming.Choose(2048, 1536));
            Assert.IsFalse(HomeFraming.FitsHeight(2048, 1536), "iPad: width-matched Fit");
        }

        [Test]
        public void CandleSequences_FlameAndLight00To03_Glow01010203()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Enumerable.Range(0, 4).Select(HomeCandleAnimator.FlameIndex));
            CollectionAssert.AreEqual(new[] { 1, 1, 2, 3 }, Enumerable.Range(0, 4).Select(HomeCandleAnimator.GlowIndex));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Enumerable.Range(0, 4).Select(HomeCandleAnimator.LightIndex));
        }

        [Test]
        public void FrameAt_ChangesEvery015s_AndLoops()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 2, 3, 0, 1 },
                new[] { 0f, 0.149f, 0.151f, 0.31f, 0.46f, 0.61f, 0.76f }.Select(t => HomeCandleAnimator.FrameAt(t, 0.15f)));
        }

        [Test]
        public void LightingSettings_Asset_Is065And015s()
        {
            var s = AssetDatabase.LoadAssetAtPath<HomeLightingSettings>(HomeSceneBuilder.LightingPath);
            Assert.IsNotNull(s);
            Assert.AreEqual(0.65f, s.lightAlpha, 1e-6f);
            Assert.AreEqual(0.15f, s.frameInterval, 1e-6f);
        }

        [Test]
        public void HomeScene_IsFirstInBuild_AndWiredToRegisteredArt()
        {
            Assert.AreEqual(HomeSceneBuilder.ScenePath, EditorBuildSettings.scenes[0].path);
            foreach (var name in new[] { "Proto_FiringRange", "Proto_Training", "Proto_Online", "Proto_Ruins" })
                Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.enabled && s.path.EndsWith(name + ".unity")), name);
            var scene = EditorSceneManager.OpenScene(HomeSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                var candle = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<HomeCandleAnimator>()).Single();
                Assert.IsNull(candle.glowFrames[0], "glow 00 is not used");
                for (int i = 0; i < 4; i++)
                {
                    StringAssert.EndsWith($"candle_flame_{i:00}.png", AssetDatabase.GetAssetPath(candle.flameFrames[i]));
                    StringAssert.EndsWith($"candle_light_{i:00}.png", AssetDatabase.GetAssetPath(candle.lightFrames[i]));
                    if (i > 0)
                        StringAssert.EndsWith($"candle_glow_{i:00}.png", AssetDatabase.GetAssetPath(candle.glowFrames[i]));
                }
                var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpriteRenderer>()).ToArray();
                Assert.AreEqual(11, renderers.Length);
                foreach (var sr in renderers)
                    StringAssert.StartsWith("Assets/Game/Art/Home/", AssetDatabase.GetAssetPath(sr.sprite));
                Assert.AreEqual(new Vector3(1494f, -498f, 0f), candle.flameRenderer.transform.position);
                Assert.AreEqual(new Vector3(1448f, -457f, 0f), candle.glowRenderer.transform.position);
                Assert.AreEqual(new Vector3(860f, -245f, 0f), candle.lightRenderer.transform.position);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
