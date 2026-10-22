using Deadlock.Combat;
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
        [Tooltip("Region map of the sprite's image for skins (same canvas, Tools/pixelart/make_skin_regions.py). Empty = drawn colours only.")]
        public Texture2D skinRegions;
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

        [Header("Attack poses (optional, drawn art; written by the art import)")]
        [Tooltip("Pose images shown during the swing (pivot = grip, same pixels per unit as the rest sprite). With poses and a timeline, the timeline replaces the swing poses above.")]
        public Sprite[] poseSprites = System.Array.Empty<Sprite>();
        [Tooltip("Region map per pose image for skins (same order as poseSprites; empty = drawn colours only).")]
        public Texture2D[] poseSkinRegions = System.Array.Empty<Texture2D>();
        [Tooltip("When each image is shown, where and how rolled (from the weapon's phase; the view never runs its own timer).")]
        public MeleePoseKey[] poseTimeline = System.Array.Empty<MeleePoseKey>();

        [Header("Narrow screens")]
        [Tooltip("Horizontal positions are pulled toward this x (units) on screens narrower than the reference, so a wide swing stays on screen. The images keep their size.")]
        public float narrowCentreX;
        [Tooltip("Horizontal scale of the whole path at 4:3 (1 = off), blended in from the reference aspect to 4:3.")]
        [Range(0.1f, 1f)] public float narrowScaleAt4x3 = 1f;

        [Header("Equip / idle")]
        [Tooltip("How far below rest the knife starts when drawn.")]
        public float lowered = 0.35f;
        public float bobAmount = 0.008f;
        [Tooltip("Extra hand bob in step with the camera while sprinting (CameraBob): lowest point and side sway, view units. 0 = none.")]
        public Vector2 sprintBob = Vector2.zero;
    }

    /// <summary>
    /// One stretch of the pose timeline: inside <see cref="phase"/>, from progress <see cref="from"/> to <see cref="to"/>,
    /// <see cref="pose"/> is shown with its grip moving from offsetFrom to offsetTo and its roll from rollFrom to rollTo.
    /// Positions are for the reference aspect (16:9, before the right-edge follow), view units at z = 1.
    /// </summary>
    [System.Serializable]
    public struct MeleePoseKey
    {
        public MeleePhase phase;
        [Range(0f, 1f)] public float from;
        [Range(0f, 1f)] public float to;
        [Tooltip("Index into poseSprites; -1 = the rest sprite.")]
        public int pose;
        [Tooltip("On: the offsets are added to the rest grip (gripPosition). Off: to the grip below.")]
        public bool relativeToRest;
        public Vector2 grip;
        public Vector2 offsetFrom;
        public Vector2 offsetTo;
        public float rollFrom;
        public float rollTo;
        [Tooltip("Speed curve inside the stretch: on = fast start, slowing (1 - (1 - t)^2); off = smooth in / out.")]
        public bool easeOut;
    }
}
