using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Player
{
    /// <summary>
    /// First-person movement and look on a CharacterController. Reads only <see cref="PlayerInputFrame"/>.
    /// Speeds and jump height are provisional prototype values.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        [SerializeField] PlayerInputRouter input;
        [SerializeField] Transform cameraPivot;

        [Header("Movement (m/s)")]
        [SerializeField, Min(0f)] float walkSpeed = 4.5f;
        [SerializeField, Min(0f)] float sprintSpeed = 7f;
        [SerializeField, Min(0f)] float acceleration = 40f;
        [SerializeField, Range(0f, 1f)] float airControl = 0.35f;

        [Header("Jump / Gravity")]
        [SerializeField, Min(0f)] float jumpHeight = 1.1f;
        [SerializeField] float gravity = -20f;

        [Header("Look")]
        [SerializeField, Range(0f, 89f)] float maxPitch = 85f;
        [Tooltip("Degrees per second the recoil offset returns toward the aim point.")]
        [SerializeField, Min(0f)] float recoilRecovery = 9f;

        CharacterController controller;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float yaw;
        float pitch;
        Vector2 recoil;
        Vector2 recoveryRemaining;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        public float WalkSpeed => walkSpeed;
        public float Yaw => yaw;
        public float Pitch => pitch;

        /// <summary>Set by the equipped weapon each frame (ADS slows movement and look, firing/ADS blocks sprint).</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public float LookSensitivityMultiplier { get; set; } = 1f;
        public bool SprintBlocked { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
        }

        /// <summary>Kicks the aim. Positive pitch is upward. Part of it recovers over time.</summary>
        public void AddRecoil(float pitchDegrees, float yawDegrees)
        {
            recoil.x += yawDegrees;
            recoil.y += pitchDegrees;
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            float dt = Time.deltaTime;
            UpdateLook(frame, dt);
            UpdateMovement(frame, dt);
        }

        void UpdateLook(PlayerInputFrame frame, float dt)
        {
            Vector2 look = frame.LookDegrees * LookSensitivityMultiplier;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch + look.y, -maxPitch, maxPitch);

            // Recoil: the kick is applied to the aim instantly; about half of it is recovered smoothly.
            Vector2 kick = recoil;
            recoil = Vector2.zero;
            yaw += kick.x;
            pitch = Mathf.Clamp(pitch + kick.y, -maxPitch, maxPitch);
            recoveryRemaining += kick * 0.5f;
            Vector2 recover = Vector2.MoveTowards(Vector2.zero, recoveryRemaining, recoilRecovery * dt);
            recoveryRemaining -= recover;
            yaw -= recover.x;
            pitch = Mathf.Clamp(pitch - recover.y, -maxPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        }

        void UpdateMovement(PlayerInputFrame frame, float dt)
        {
            IsGrounded = controller.isGrounded;

            Vector2 move = Vector2.ClampMagnitude(frame.Move, 1f);
            IsSprinting = frame.Sprint && !SprintBlocked && move.y > 0.5f;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * (IsSprinting ? 1f : MoveSpeedMultiplier);
            Vector3 target = (transform.right * move.x + transform.forward * move.y) * speed;

            float accel = acceleration * (IsGrounded ? 1f : airControl);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, accel * dt);

            if (IsGrounded)
            {
                if (verticalVelocity < 0f)
                    verticalVelocity = -2f; // keep the controller pressed to the ground
                if (frame.JumpPressed)
                    verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            verticalVelocity += gravity * dt;

            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
        }
    }
}
