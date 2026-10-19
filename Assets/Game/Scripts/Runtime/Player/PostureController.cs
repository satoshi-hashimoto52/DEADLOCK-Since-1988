using System;
using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Player
{
    public enum Posture
    {
        Standing,
        Crouching,
        Prone,
        /// <summary>Transient: slide out of a sprint, ends in Crouching (or Prone if even crouching has no room).</summary>
        Sliding,
    }

    /// <summary>
    /// Stand / crouch / prone. Owns the movement capsule (CharacterController height, feet kept in place) and the eye
    /// height, both blended over a short transition. Growing (standing up) is only accepted when the target capsule fits.
    /// Prone is approximated by a short upright capsule (see docs/PROGRESS.md): it is the movement collider only,
    /// not a model for being hit by other players.
    /// Values are provisional prototype values.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PostureController : MonoBehaviour
    {
        [Serializable]
        public struct Shape
        {
            public float height;
            public float eyeHeight;
            [Range(0.05f, 1f)] public float speedMultiplier;
            [Tooltip("Step height for this posture. The CharacterController lifts by the step height before moving, so a low gap is only enterable when its clearance exceeds height + step.")]
            public float stepOffset;
        }

        [SerializeField] PlayerInputRouter input;
        [SerializeField] Transform cameraPivot;
        [Tooltip("What blocks standing up (everything except the player itself, triggers are ignored).")]
        [SerializeField] LayerMask obstacleMask = ~0;
        [SerializeField] Shape standing = new Shape { height = 1.8f, eyeHeight = 1.6f, speedMultiplier = 1f, stepOffset = 0.35f };
        [SerializeField] Shape crouching = new Shape { height = 1.2f, eyeHeight = 1.05f, speedMultiplier = 0.55f, stepOffset = 0.1f };
        [SerializeField] Shape prone = new Shape { height = 0.75f, eyeHeight = 0.45f, speedMultiplier = 0.25f, stepOffset = 0.05f };
        [SerializeField, Min(0.01f)] float crouchTransitionTime = 0.22f;
        [SerializeField, Min(0.01f)] float proneTransitionTime = 0.4f;

        [Header("Slide (provisional)")]
        [SerializeField] Shape sliding = new Shape { height = 1.0f, eyeHeight = 0.85f, speedMultiplier = 1f, stepOffset = 0.05f };
        [SerializeField, Min(0.01f)] float slideTransitionTime = 0.12f;
        [SerializeField, Min(0.1f)] float slideDuration = 0.75f;
        [Tooltip("Minimum actual horizontal speed (m/s) to start a slide.")]
        [SerializeField, Min(0f)] float slideMinSpeed = 5.5f;
        [SerializeField, Min(0.5f)] float slideStartSpeedMultiplier = 1.05f;
        [SerializeField, Min(0f)] float slideEndSpeed = 2.0f;
        [Tooltip("Seconds after a slide ends before the next one can start.")]
        [SerializeField, Min(0f)] float slideCooldown = 0.9f;

        CharacterController controller;
        FirstPersonMotor motor;
        Shape from;
        float slideStart;
        float slideStartSpeed;
        float slideReadyTime;
        float transitionStart;
        float transitionDuration;
        float progress = 1f;

        /// <summary>Requested (target) posture. While <see cref="IsTransitioning"/> the body is still changing shape.</summary>
        public Posture Current { get; private set; } = Posture.Standing;
        public bool IsTransitioning => progress < 1f;
        public float Height { get; private set; }
        public float EyeHeight { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;
        public bool CanSprint => Current == Posture.Standing && !IsTransitioning;
        public bool CanJump => Current == Posture.Standing && !IsTransitioning;
        /// <summary>True on a frame where the jump press was used as a stand-up request (so it must not also jump).</summary>
        public bool JumpConsumed { get; private set; }
        /// <summary>Time of the last stand-up / crouch request refused because there was no room.</summary>
        public float LastBlockedTime { get; private set; } = -10f;

        public bool IsSliding => Current == Posture.Sliding;
        /// <summary>Horizontal direction fixed at the start of the slide.</summary>
        public Vector3 SlideDirection { get; private set; }
        /// <summary>Current slide speed: starts near the sprint speed and eases down to <see cref="slideEndSpeed"/>.</summary>
        public float SlideSpeed
        {
            get
            {
                float t = Mathf.Clamp01((Time.time - slideStart) / slideDuration);
                return Mathf.Lerp(slideStartSpeed, slideEndSpeed, 1f - (1f - t) * (1f - t));
            }
        }
        public bool SlideReady => Time.time >= slideReadyTime;

        public event Action<Posture> Changed;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            motor = GetComponent<FirstPersonMotor>();
            from = standing;
            Apply(standing);
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            JumpConsumed = false;

            if (IsSliding)
            {
                // No jump-cancel and no posture input during a slide (prototype rule).
                JumpConsumed = frame.JumpPressed;
                if (Time.time - slideStart >= slideDuration)
                    EndSlide();
            }
            // Posture changes only on the ground (no shrinking the hitbox mid-air).
            else if (controller.isGrounded)
            {
                if (frame.CrouchPressed && CanStartSlide())
                    BeginSlide();
                else if (frame.CrouchPressed)
                    Request(Current == Posture.Crouching ? Posture.Standing : Posture.Crouching);
                else if (frame.PronePressed)
                    Request(Current == Posture.Prone ? Posture.Standing : Posture.Prone);
                else if (frame.JumpPressed && Current != Posture.Standing)
                {
                    JumpConsumed = true;
                    Request(Posture.Standing);
                }
            }
            else if (frame.JumpPressed && Current != Posture.Standing)
            {
                JumpConsumed = true;
            }

            if (progress < 1f)
            {
                progress = Mathf.Clamp01((Time.time - transitionStart) / transitionDuration);
                float t = progress * progress * (3f - 2f * progress);
                Shape to = ShapeOf(Current);
                Apply(new Shape
                {
                    height = Mathf.Lerp(from.height, to.height, t),
                    eyeHeight = Mathf.Lerp(from.eyeHeight, to.eyeHeight, t),
                    speedMultiplier = Mathf.Lerp(from.speedMultiplier, to.speedMultiplier, t),
                    stepOffset = Mathf.Lerp(from.stepOffset, to.stepOffset, t),
                });
            }
        }

        /// <summary>Asks for a posture. Returns false (and keeps the current one) when there is no room or not grounded.</summary>
        public bool Request(Posture target)
        {
            if (target == Current || target == Posture.Sliding || IsSliding || !controller.isGrounded)
                return false;
            Shape shape = ShapeOf(target);
            if (shape.height > Height + 0.001f && !HasRoomFor(shape.height))
            {
                LastBlockedTime = Time.time;
                return false;
            }

            bool involvesProne = target == Posture.Prone || Current == Posture.Prone;
            StartTransition(target, involvesProne ? proneTransitionTime : crouchTransitionTime);
            return true;
        }

        /// <summary>Slide: grounded, standing (fully), actually sprinting fast enough, and off cooldown.</summary>
        public bool CanStartSlide() =>
            motor != null && controller.isGrounded && Current == Posture.Standing && !IsTransitioning
            && motor.IsSprinting && motor.PlanarSpeed >= slideMinSpeed && SlideReady;

        void BeginSlide()
        {
            Vector3 v = motor.Velocity;
            v.y = 0f;
            SlideDirection = v.normalized;
            slideStartSpeed = v.magnitude * slideStartSpeedMultiplier;
            slideStart = Time.time;
            StartTransition(Posture.Sliding, slideTransitionTime);
        }

        /// <summary>
        /// Ends a slide (time up, blocked by a wall, or left the ground). Ends crouched; if there is no room even
        /// for crouching it ends prone (shrinking always fits). Starts the cooldown.
        /// </summary>
        public void EndSlide()
        {
            if (!IsSliding)
                return;
            slideReadyTime = Time.time + slideCooldown;
            Posture end = HasRoomFor(crouching.height) ? Posture.Crouching : Posture.Prone;
            if (end == Posture.Prone)
                LastBlockedTime = Time.time;
            StartTransition(end, end == Posture.Prone ? proneTransitionTime : crouchTransitionTime);
        }

        void StartTransition(Posture target, float duration)
        {
            from = new Shape { height = Height, eyeHeight = EyeHeight, speedMultiplier = SpeedMultiplier, stepOffset = controller.stepOffset };
            transitionDuration = duration;
            transitionStart = Time.time;
            progress = 0f;
            Current = target;
            Changed?.Invoke(target);
        }

        /// <summary>Does a capsule of this height fit at the current feet position?</summary>
        public bool HasRoomFor(float height)
        {
            float radius = controller.radius;
            float skin = controller.skinWidth;
            Vector3 feet = transform.position + Vector3.up * (controller.center.y - controller.height * 0.5f);
            // Bottom sphere lifted off the floor; top sphere reaches the full target height plus skin.
            Vector3 bottom = feet + Vector3.up * (radius + 0.05f);
            Vector3 top = feet + Vector3.up * Mathf.Max(radius + 0.05f, height + skin - radius);
            return !Physics.CheckCapsule(bottom, top, radius * 0.95f, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        Shape ShapeOf(Posture posture) => posture switch
        {
            Posture.Crouching => crouching,
            Posture.Prone => prone,
            Posture.Sliding => sliding,
            _ => standing,
        };

        void Apply(Shape shape)
        {
            Height = Mathf.Max(shape.height, controller.radius * 2f);
            EyeHeight = shape.eyeHeight;
            SpeedMultiplier = shape.speedMultiplier;
            // Feet stay where they are: the capsule bottom is always at the transform origin.
            controller.height = Height;
            controller.center = new Vector3(0f, Height * 0.5f, 0f);
            controller.stepOffset = Mathf.Min(shape.stepOffset, Height);
            Vector3 eye = cameraPivot.localPosition;
            eye.y = EyeHeight;
            cameraPivot.localPosition = eye;
        }
    }
}
