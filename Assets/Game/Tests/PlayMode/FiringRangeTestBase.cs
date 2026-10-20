using System.Collections;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Deadlock.Tests
{
    /// <summary>
    /// Shared setup for tests that play the prototype scene with simulated devices (Input System test fixture):
    /// desktop keyboard/mouse and multi-finger touch go through the same paths as real input.
    /// </summary>
    public abstract class FiringRangeTestBase : InputTestFixture
    {
        protected const string SceneName = "Proto_FiringRange";

        protected Keyboard keyboard;
        protected Mouse mouse;
        protected GameObject player;
        protected PlayerInputRouter router;
        protected FirstPersonMotor motor;
        protected PostureController posture;
        protected GunController gun;
        protected MeleeWeapon melee;
        protected WeaponSwitcher weapons;
        protected ProjectileSystem projectiles;

        string savedLayoutJson;
        readonly System.Collections.Generic.Dictionary<string, int> savedControls = new System.Collections.Generic.Dictionary<string, int>();
        static readonly string[] ControlKeys =
        {
            ControlSettings.TouchAdsKey, ControlSettings.DesktopAdsKey, ControlSettings.TouchAdsDragAimKey, ControlSettings.TouchFireModeKey,
        };

        // Tests run against the default touch layout; a layout the user saved in the Editor is put back afterwards.
        public override void Setup()
        {
            base.Setup();
            keyboard = null; // devices from a previous test were removed by the fixture
            mouse = null;
            savedLayoutJson = PlayerPrefs.GetString(TouchLayout.PrefsKey, null);
            TouchLayout.ClearSaved();
            // Tests start from the default control settings; the user's settings are put back afterwards.
            savedControls.Clear();
            foreach (var key in ControlKeys)
                if (PlayerPrefs.HasKey(key))
                    savedControls[key] = PlayerPrefs.GetInt(key);
            ControlSettings.ClearSaved();
            Time.timeScale = 1f;
        }

        public override void TearDown()
        {
            Time.timeScale = 1f;
            if (string.IsNullOrEmpty(savedLayoutJson))
                TouchLayout.ClearSaved();
            else
                PlayerPrefs.SetString(TouchLayout.PrefsKey, savedLayoutJson);
            ControlSettings.ClearSaved();
            foreach (var pair in savedControls)
                PlayerPrefs.SetInt(pair.Key, pair.Value);
            base.TearDown();
        }

        protected IEnumerator LoadScene(InputMode mode, string sceneName = SceneName)
        {
            // Added once per test (a test may load scenes several times).
            if (keyboard == null)
                keyboard = InputSystem.AddDevice<Keyboard>();
            if (mouse == null)
                mouse = InputSystem.AddDevice<Mouse>();
            SceneManager.LoadScene(sceneName);
            yield return null;
            yield return null; // let Start() run

            player = GameObject.Find("Player");
            router = player.GetComponent<PlayerInputRouter>();
            motor = player.GetComponent<FirstPersonMotor>();
            posture = player.GetComponent<PostureController>();
            gun = player.GetComponent<GunController>();
            melee = player.GetComponent<MeleeWeapon>();
            weapons = player.GetComponent<WeaponSwitcher>();
            projectiles = Object.FindAnyObjectByType<ProjectileSystem>();
            router.RequireCursorLock = false; // no focused game view in batch runs
            router.SetMode(mode);
            yield return null;
        }

        protected static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        protected static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
                yield return null;
        }

        /// <summary>One queued press + release of a key/button, processed in the next frames.</summary>
        protected IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            Press(button, queueEventOnly: true);
            yield return null;
            Release(button, queueEventOnly: true);
            yield return null;
        }

        protected void Teleport(Vector3 position, float yaw = 0f)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
            motor.SetYawPitch(yaw, 0f);
            Physics.SyncTransforms();
        }

        protected static Vector2 ScreenCenter(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners); // overlay canvas: world == screen
            return (corners[0] + corners[2]) * 0.5f;
        }
    }
}
