using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Running feel: a visual up-and-down step motion of the local camera while sprinting on the ground. Only the
    /// camera's local position moves (no tilt, no pitch), and only downward from the eye, so it never goes into a low
    /// ceiling. Its strength follows the real ground speed (none against a wall), fades in / out with the sprint, and
    /// is off in the air, while sliding and while aiming. Purely visual and local: the body, hitboxes and what the
    /// network sends are not moved (shots still start at the camera, so the crosshair and the shot stay together).
    /// The hand-held weapon view reads <see cref="Blend"/> / <see cref="StepPhase"/> to move in step.
    /// </summary>
    public sealed class CameraBob : MonoBehaviour
    {
        [SerializeField] FirstPersonMotor motor;
        [SerializeField] PostureController posture;
        [SerializeField] GunController gun;

        [Tooltip("Lowest point of a step below the eye, metres.")]
        [SerializeField, Min(0f)] float stepDrop = 0.08f;
        [Tooltip("Side sway at each step, metres.")]
        [SerializeField, Min(0f)] float sideSway = 0.015f;
        [Tooltip("Steps per second at full sprint speed.")]
        [SerializeField, Min(0.1f)] float stepsPerSecond = 3f;
        [Tooltip("How fast the bob fades in / out on the ground (per second).")]
        [SerializeField, Min(0.1f)] float fadeSpeed = 4f;
        [Tooltip("How fast it fades out after leaving the ground, starting a slide or starting to aim (per second).")]
        [SerializeField, Min(0.1f)] float airFadeSpeed = 10f;

        Vector3 baseLocalPosition;
        float stride; // running step count (one step = 1)

        /// <summary>0..1 strength of the sprint step motion right now.</summary>
        public float Blend { get; private set; }

        /// <summary>Step position: 0 at the top of a step, 0.5 at the lowest point, 1 at the next top.</summary>
        public float StepPhase => stride - Mathf.Floor(stride);

        /// <summary>Which foot the current step is on (+1 / -1), for the side sway.</summary>
        public float Side => Mathf.FloorToInt(stride) % 2 == 0 ? 1f : -1f;

        public Vector3 Offset { get; private set; }

        void Awake() => baseLocalPosition = transform.localPosition;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool sliding = posture != null && posture.IsSliding;
            bool aiming = gun != null && gun.isActiveAndEnabled && gun.AdsBlend > 0.01f;
            bool active = motor != null && motor.IsSprinting && motor.IsGrounded && !sliding && !aiming;
            float speedRatio = motor != null && motor.SprintSpeed > 0f ? Mathf.Clamp01(motor.PlanarSpeed / motor.SprintSpeed) : 0f;
            float target = active ? speedRatio : 0f;
            float rate = motor != null && motor.IsGrounded && !sliding && !aiming ? fadeSpeed : airFadeSpeed;
            Blend = Mathf.MoveTowards(Blend, target, rate * dt);
            if (Blend > 0f)
                stride += stepsPerSecond * Mathf.Max(0.3f, speedRatio) * dt;
            else
                stride = 0f;

            float down = 0.5f - 0.5f * Mathf.Cos(StepPhase * 2f * Mathf.PI); // 0 at the top, 1 at the bottom
            float side = Mathf.Sin(StepPhase * Mathf.PI) * Side;
            Offset = new Vector3(side * sideSway, -down * stepDrop, 0f) * Blend;
            transform.localPosition = baseLocalPosition + Offset;
        }

        void OnDisable()
        {
            Blend = 0f;
            Offset = Vector3.zero;
            transform.localPosition = baseLocalPosition;
        }
    }
}
