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
        PostureController posture;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float yaw;
        float pitch;
        Vector2 recoil;
        Vector2 recoveryRemaining;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        /// <summary>Measured horizontal speed of the last frame (what actually moved, after collisions).</summary>
        public float PlanarSpeed { get; private set; }
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        public float Yaw => yaw;
        public float Pitch => pitch;

        /// <summary>Set by the equipped weapon each frame (ADS slows movement and look, firing/ADS blocks sprint).</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public float LookSensitivityMultiplier { get; set; } = 1f;
        public bool SprintBlocked { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            posture = GetComponent<PostureController>();
            yaw = transform.eulerAngles.y;
        }

        /// <summary>Sets the view direction directly (spawn / teleport). Clears pending recoil.</summary>
        public void SetYawPitch(float yawDegrees, float pitchDegrees)
        {
            yaw = yawDegrees;
            pitch = Mathf.Clamp(pitchDegrees, -maxPitch, maxPitch);
            recoil = recoveryRemaining = Vector2.zero;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
        }

        /// <summary>Moves the player to a spawn point (respawn): no velocity carried over.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            SetYawPitch(yawDegrees, 0f);
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

            bool sliding = posture != null && posture.IsSliding;
            if (sliding)
            {
                // Slide: fixed direction from the start, easing down; stick input does not steer.
                IsSprinting = false;
                horizontalVelocity = posture.SlideDirection * posture.SlideSpeed;
            }
            else
            {
                Vector2 move = Vector2.ClampMagnitude(frame.Move, 1f);
                // Sprint: standing only, forward only; crouch/prone never stand up by themselves.
                bool postureAllowsSprint = posture == null || posture.CanSprint;
                IsSprinting = frame.Sprint && !SprintBlocked && postureAllowsSprint && move.y > 0.5f;
                float postureSpeed = posture != null ? posture.SpeedMultiplier : 1f;
                float speed = IsSprinting ? sprintSpeed : walkSpeed * MoveSpeedMultiplier * postureSpeed;
                Vector3 target = (transform.right * move.x + transform.forward * move.y) * speed;

                float accel = acceleration * (IsGrounded ? 1f : airControl);
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, accel * dt);
            }

            if (IsGrounded)
            {
                if (verticalVelocity < 0f)
                    verticalVelocity = -2f; // keep the controller pressed to the ground
                // From crouch/prone the press is a stand-up request (handled by PostureController), not a jump.
                bool postureAllowsJump = posture == null || (posture.CanJump && !posture.JumpConsumed);
                if (frame.JumpPressed && postureAllowsJump)
                    verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            verticalVelocity += gravity * dt;

            Vector3 before = transform.position;
            CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
            Vector3 moved = transform.position - before;
            float planarMoved = new Vector2(moved.x, moved.z).magnitude;
            PlanarSpeed = dt > 0f ? planarMoved / dt : 0f;

            if (sliding)
            {
                // Off a ledge: normal air movement takes over. Into a wall: the slide stops there.
                float expected = horizontalVelocity.magnitude * dt;
                if (!controller.isGrounded || planarMoved < expected * 0.5f)
                {
                    posture.EndSlide();
                    if (planarMoved < expected * 0.5f)
                        horizontalVelocity = Vector3.zero;
                }
            }
        }
    }
}
