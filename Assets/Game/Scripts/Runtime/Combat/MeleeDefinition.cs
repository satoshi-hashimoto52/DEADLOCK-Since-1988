using Deadlock.View;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Static tuning for one melee weapon. Melee weapons are meant to differ only slightly (GAME_SPEC 7.1);
    /// all values here are provisional prototype values.
    /// </summary>
    [CreateAssetMenu(menuName = "DEADLOCK/Melee Definition", fileName = "Melee_")]
    public sealed class MeleeDefinition : ScriptableObject
    {
        public string weaponId = "proto_knife";
        public string displayName = "ナイフ";
        [Tooltip("First-person sprite and swing motion. Presentation only.")]
        public MeleeViewProfile viewProfile;

        [Min(0f)] public float damage = 50f;
        [Tooltip("Reach from the eye, metres.")]
        [Min(0.1f)] public float range = 1.7f;
        [Tooltip("Radius of the swept sphere (attack width).")]
        [Min(0.01f)] public float radius = 0.3f;

        [Header("Timing (seconds)")]
        [Min(0f)] public float windup = 0.12f;
        [Tooltip("Hits are checked only during this window, right after the windup.")]
        [Min(0.01f)] public float activeTime = 0.12f;
        [Min(0f)] public float recovery = 0.28f;

        public float TotalTime => windup + activeTime + recovery;
    }
}
