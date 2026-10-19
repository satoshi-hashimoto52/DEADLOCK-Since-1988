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
        }

        [SerializeField] PlayerInputRouter input;
        [SerializeField] Transform cameraPivot;
        [Tooltip("What blocks standing up (everything except the player itself, triggers are ignored).")]
        [SerializeField] LayerMask obstacleMask = ~0;
        [SerializeField] Shape standing = new Shape { height = 1.8f, eyeHeight = 1.6f, speedMultiplier = 1f };
        [SerializeField] Shape crouching = new Shape { height = 1.2f, eyeHeight = 1.05f, speedMultiplier = 0.55f };
        [SerializeField] Shape prone = new Shape { height = 0.75f, eyeHeight = 0.45f, speedMultiplier = 0.25f };
        [SerializeField, Min(0.01f)] float crouchTransitionTime = 0.22f;
        [SerializeField, Min(0.01f)] float proneTransitionTime = 0.4f;

        CharacterController controller;
        Shape from;
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

        public event Action<Posture> Changed;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            from = standing;
            Apply(standing);
        }

        void Update()
        {
            PlayerInputFrame frame = input.Current;
            JumpConsumed = false;

            // Posture changes only on the ground (no shrinking the hitbox mid-air).
            if (controller.isGrounded)
            {
                if (frame.CrouchPressed)
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
                });
            }
        }

        /// <summary>Asks for a posture. Returns false (and keeps the current one) when there is no room or not grounded.</summary>
        public bool Request(Posture target)
        {
            if (target == Current || !controller.isGrounded)
                return false;
            Shape shape = ShapeOf(target);
            if (shape.height > Height + 0.001f && !HasRoomFor(shape.height))
            {
                LastBlockedTime = Time.time;
                return false;
            }

            from = new Shape { height = Height, eyeHeight = EyeHeight, speedMultiplier = SpeedMultiplier };
            bool involvesProne = target == Posture.Prone || Current == Posture.Prone;
            transitionDuration = involvesProne ? proneTransitionTime : crouchTransitionTime;
            transitionStart = Time.time;
            progress = 0f;
            Current = target;
            Changed?.Invoke(target);
            return true;
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
            Vector3 eye = cameraPivot.localPosition;
            eye.y = EyeHeight;
            cameraPivot.localPosition = eye;
        }
    }
}
