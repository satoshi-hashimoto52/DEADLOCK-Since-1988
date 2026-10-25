using System.Collections;
using System.Collections.Generic;
using Deadlock.Game;
using Deadlock.Home;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>Home scene at play: framing follows the screen size, candle frames stay in sync, continue to the firing range.</summary>
    public class HomeSceneTests : InputTestFixture
    {
        // Input runs on the test fixture (the home's UI input module must not touch the real input state).
        public override void Setup()
        {
            base.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        HomeBackgroundView view;
        HomeCandleAnimator candle;
        Camera cam;

        IEnumerator LoadHome()
        {
            SceneManager.LoadScene(GameScenes.Home);
            yield return null;
            yield return null;
            view = Object.FindAnyObjectByType<HomeBackgroundView>();
            candle = Object.FindAnyObjectByType<HomeCandleAnimator>();
            cam = view.GetComponent<Camera>();
        }

        [UnityTest]
        public IEnumerator Framing_FollowsScreen_HeightMatchedOnIPhone_FitOnIPad()
        {
            yield return LoadHome();
            Assert.AreEqual(HomeFraming.Choose(Screen.width, Screen.height), view.Mode);
            view.Apply(2622, 1206);
            Assert.AreEqual(HomeFramingMode.Fit, view.Mode);
            Assert.AreEqual(470.5f, cam.orthographicSize, 1e-3f, "half of the 941 px reference height = screen height");
            view.Apply(2048, 1536);
            Assert.AreEqual(HomeFramingMode.Fit, view.Mode);
            Assert.AreEqual(627f, cam.orthographicSize, 1e-3f);
            Assert.AreEqual(new Vector3(836f, -470.5f, cam.transform.position.z), cam.transform.position);
            yield return null; // the real screen size is applied again on the next frame
            Assert.AreEqual(HomeFraming.Choose(Screen.width, Screen.height), view.Mode);
        }

        [UnityTest]
        public IEnumerator Candle_ThreeLayersShareTheFrame_GlowSkips00_Light065_NothingMoves()
        {
            yield return LoadHome();
            var start = new[] { candle.flameRenderer.transform.position, candle.glowRenderer.transform.position, candle.lightRenderer.transform.position };
            var seen = new HashSet<int>();
            float t0 = Time.time;
            while (Time.time - t0 < 1f)
            {
                yield return null;
                int f = candle.Frame;
                seen.Add(f);
                Assert.AreEqual(HomeCandleAnimator.FrameAt(Time.time, 0.15f), f);
                Assert.AreEqual($"candle_flame_{f:00}", candle.flameRenderer.sprite.name);
                Assert.AreEqual($"candle_glow_{HomeCandleAnimator.GlowIndex(f):00}", candle.glowRenderer.sprite.name);
                Assert.AreEqual($"candle_light_{f:00}", candle.lightRenderer.sprite.name);
                Assert.AreNotEqual("candle_glow_00", candle.glowRenderer.sprite.name);
                Assert.AreEqual(0.65f, candle.lightRenderer.color.a, 1e-6f);
                Assert.AreEqual(1f, candle.flameRenderer.color.a, 1e-6f);
                Assert.AreEqual(1f, candle.glowRenderer.color.a, 1e-6f);
                Assert.AreEqual(start[0], candle.flameRenderer.transform.position);
                Assert.AreEqual(start[1], candle.glowRenderer.transform.position);
                Assert.AreEqual(start[2], candle.lightRenderer.transform.position);
            }
            Assert.AreEqual(4, seen.Count, "all four frames shown within 1 s");
        }

        [UnityTest]
        public IEnumerator SafeAreaPanel_FitsAGivenSafeArea()
        {
            yield return LoadHome();
            var panel = GameObject.Find("FixedSafeArea").GetComponent<HomeSafeAreaPanel>();
            panel.enabled = false; // keep the test values (the real safe area is re-applied while enabled)
            panel.Fit(new Rect(186, 63, 2250, 1143), 2622, 1206);
            var rt = (RectTransform)panel.transform;
            Assert.AreEqual(186f / 2622f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(63f / 1206f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(2436f / 2622f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.y, 1e-5f);
            Assert.IsNotNull(rt.Find("LayoutEditButton"), "fixed way into the layout editor");
        }
    }
}
