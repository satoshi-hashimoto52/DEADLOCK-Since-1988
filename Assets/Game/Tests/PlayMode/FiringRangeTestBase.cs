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

        protected IEnumerator LoadScene(InputMode mode)
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            SceneManager.LoadScene(SceneName);
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
