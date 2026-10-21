using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// Presentation data for a first-person melee sprite. Same space as <see cref="WeaponViewProfile"/>
    /// (view-model camera, z = 1, vertical FOV 50°). Sprite/grip fields are baked; swing poses are hand-tuned.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Melee View Profile", fileName = "MeleeView_")]
    public sealed class MeleeViewProfile : ScriptableObject
    {
        [Header("Sprite (baked)")]
        [Tooltip("Pivot is the grip (the hand).")]
        public Sprite sprite;
        [Tooltip("Where the grip sits for a 16:9 screen.")]
        public Vector2 gripPosition = new Vector2(0.38f, -0.3f);
        public float referenceAspect = 16f / 9f;
        [Range(0f, 1f)] public float rightEdgeFollow = 0.5f;

        [Header("Swing poses (offset from rest, roll in degrees)")]
        [Tooltip("End of the windup: drawn back to the right.")]
        public Vector2 windupOffset = new Vector2(0.12f, 0.06f);
        public float windupRoll = -30f;
        [Tooltip("End of the active window: slashed across toward the centre-left.")]
        public Vector2 strikeOffset = new Vector2(-0.46f, 0.12f);
        public float strikeRoll = 60f;
        [Tooltip("Slightly larger at the strike (closer to the camera).")]
        public float strikeScale = 1.2f;

        [Header("Slash arc (optional)")]
        [Tooltip("On: the active window follows a curve from the windup pose through the mid pose to the strike pose (a slash), instead of a straight line.")]
        public bool useStrikeArc;
        [Tooltip("Control pose of the arc (offset from rest / roll); the blade crosses near the screen centre around here.")]
        public Vector2 strikeMidOffset;
        public float strikeMidRoll;
        [Tooltip("Speed curve of the active window: 0 = smooth in/out; > 1 = fast start, slowing follow-through (1 - (1 - t)^power).")]
        [Min(0f)] public float strikeEaseOutPower;

        [Header("Equip / idle")]
        [Tooltip("How far below rest the knife starts when drawn.")]
        public float lowered = 0.35f;
        public float bobAmount = 0.008f;
        [Tooltip("Extra hand bob in step with the camera while sprinting (CameraBob): lowest point and side sway, view units. 0 = none.")]
        public Vector2 sprintBob = Vector2.zero;
    }
}
