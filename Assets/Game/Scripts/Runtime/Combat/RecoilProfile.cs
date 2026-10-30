using System;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>Which recoil components a profile uses.</summary>
    public enum RecoilAxis
    {
        /// <summary>Up or down only.</summary>
        Vertical,
        /// <summary>Left, right or alternating only.</summary>
        Horizontal,
        /// <summary>One fixed diagonal: up / down plus left / right.</summary>
        Diagonal,
        /// <summary>Up / down plus left-right alternating (e.g. "up, alternating sides").</summary>
        Mixed,
    }

    public enum RecoilVertical
    {
        None,
        Up,
        Down,
    }

    public enum RecoilHorizontal
    {
        None,
        Left,
        Right,
        /// <summary>Right on even shot numbers (0, 2, …), left on odd ones.</summary>
        Alternating,
    }

    public enum RecoilStrength
    {
        Weak,
        Medium,
        Strong,
    }

    /// <summary>
    /// Optional, data-driven recoil of a gun (direction, strength, base angles, ADS factor). Disabled by default: a gun
    /// without an enabled, valid profile keeps the earlier recoil (fixed pitch, random left / right yaw). The strength
    /// factors are trial values for the balance lab, not decided balance. Spread is separate and unchanged.
    /// </summary>
    [Serializable]
    public sealed class RecoilProfile
    {
        /// <summary>Trial factors for Weak / Medium / Strong (not decided balance).</summary>
        public const float WeakFactor = 0.7f, MediumFactor = 1f, StrongFactor = 1.4f;

        [Tooltip("Off: the gun keeps the earlier recoil (recoilPitchDegrees, random yaw).")]
        public bool enabled;
        public RecoilAxis axis = RecoilAxis.Vertical;
        public RecoilVertical vertical = RecoilVertical.Up;
        public RecoilHorizontal horizontal = RecoilHorizontal.None;
        public RecoilStrength strength = RecoilStrength.Medium;
        [Tooltip("Base vertical kick per shot (degrees, before strength / ADS).")]
        [Min(0f)] public float pitchDegrees = 0.7f;
        [Tooltip("Base horizontal kick per shot (degrees, before strength / ADS).")]
        [Min(0f)] public float yawDegrees = 0.25f;
        [Tooltip("Recoil factor when fully aimed (1 = same as hip).")]
        [Range(0f, 1f)] public float adsMultiplier = 0.55f;
        [Tooltip("Recovery amount: share of each kick that returns afterwards (0 = all of it stays, 1 = all of it returns). The earlier recoil uses 0.5.")]
        [Range(0f, 1f)] public float recoveryFraction = 0.5f;
        [Tooltip("Recovery speed in degrees per second (" + "0.5–60" + "). The prototype scenes use 9.")]
        [Range(MinRecoverySpeed, MaxRecoverySpeed)] public float recoverySpeed = 9f;

        public const float MinRecoverySpeed = 0.5f, MaxRecoverySpeed = 60f;

        public static float Factor(RecoilStrength strength) => strength switch
        {
            RecoilStrength.Weak => WeakFactor,
            RecoilStrength.Strong => StrongFactor,
            _ => MediumFactor,
        };

        public RecoilProfile Clone() => (RecoilProfile)MemberwiseClone();

        /// <summary>The axis that up / down and left / right make (both none: null, no valid profile).</summary>
        public static RecoilAxis? AxisFor(RecoilVertical v, RecoilHorizontal h)
        {
            bool up = v != RecoilVertical.None;
            if (h == RecoilHorizontal.Alternating)
                return up ? RecoilAxis.Mixed : RecoilAxis.Horizontal;
            if (h == RecoilHorizontal.None)
                return up ? RecoilAxis.Vertical : (RecoilAxis?)null;
            return up ? RecoilAxis.Diagonal : RecoilAxis.Horizontal;
        }

        /// <summary>
        /// Sets the directions and the axis they make, so the profile is never contradictory. Both none is refused
        /// (false, nothing changed): a profile always kicks somewhere.
        /// </summary>
        public bool SetDirections(RecoilVertical v, RecoilHorizontal h)
        {
            var axis = AxisFor(v, h);
            if (axis == null)
                return false;
            vertical = v;
            horizontal = h;
            this.axis = axis.Value;
            return true;
        }

        /// <summary>
        /// Checks that the directions match the axis and the numbers are usable. Vertical: up / down, no sideways.
        /// Horizontal: left / right / alternating, no up / down. Diagonal: up / down plus left or right. Mixed: up / down
        /// plus alternating.
        /// </summary>
        public bool IsValid(out string error)
        {
            error = null;
            bool v = vertical != RecoilVertical.None;
            switch (axis)
            {
                case RecoilAxis.Vertical when !v || horizontal != RecoilHorizontal.None:
                    error = "縦方向：上か下を選び、左右はなしにしてください";
                    break;
                case RecoilAxis.Horizontal when v || horizontal == RecoilHorizontal.None:
                    error = "横方向：左・右・交互のどれかを選び、上下はなしにしてください";
                    break;
                case RecoilAxis.Diagonal when !v || (horizontal != RecoilHorizontal.Left && horizontal != RecoilHorizontal.Right):
                    error = "斜め：上か下と、左か右を選んでください";
                    break;
                case RecoilAxis.Mixed when !v || horizontal != RecoilHorizontal.Alternating:
                    error = "複合：上か下と、左右交互を選んでください";
                    break;
            }
            if (error == null && !(Finite(pitchDegrees) && Finite(yawDegrees) && Finite(adsMultiplier)
                                   && pitchDegrees >= 0f && yawDegrees >= 0f && adsMultiplier >= 0f && adsMultiplier <= 1f))
                error = "角度は0以上、ADS倍率は0〜1の数値にしてください";
            if (error == null && !(Finite(recoveryFraction) && recoveryFraction >= 0f && recoveryFraction <= 1f))
                error = "回復量は0〜1（戻る割合）にしてください";
            if (error == null && !(Finite(recoverySpeed) && recoverySpeed >= MinRecoverySpeed && recoverySpeed <= MaxRecoverySpeed))
                error = $"回復速度は{MinRecoverySpeed}〜{MaxRecoverySpeed}°/秒にしてください";
            return error == null;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    /// <summary>
    /// Pure recoil calculation from a <see cref="RecoilProfile"/>: the same inputs always give the same kick (no
    /// randomness), so test shots can be repeated. Pitch is positive upwards, yaw positive to the right (the signs
    /// <see cref="Deadlock.Player.FirstPersonMotor.AddRecoil"/> takes).
    /// </summary>
    public static class RecoilCalculator
    {
        /// <summary>
        /// Kick of shot number <paramref name="shotIndex"/> (0 = first shot of a trigger pull) at
        /// <paramref name="adsBlend"/> (0 = hip, 1 = fully aimed). Returns false (zero kick) for an invalid profile.
        /// </summary>
        public static bool TryCompute(RecoilProfile profile, int shotIndex, float adsBlend, out float pitch, out float yaw)
        {
            pitch = yaw = 0f;
            if (profile == null || !profile.IsValid(out _))
                return false;
            float scale = RecoilProfile.Factor(profile.strength) * Mathf.Lerp(1f, profile.adsMultiplier, Mathf.Clamp01(adsBlend));
            float v = profile.vertical switch
            {
                RecoilVertical.Up => 1f,
                RecoilVertical.Down => -1f,
                _ => 0f,
            };
            float h = profile.horizontal switch
            {
                RecoilHorizontal.Right => 1f,
                RecoilHorizontal.Left => -1f,
                RecoilHorizontal.Alternating => (Math.Abs(shotIndex) % 2 == 0) ? 1f : -1f,
                _ => 0f,
            };
            pitch = v * profile.pitchDegrees * scale;
            yaw = h * profile.yawDegrees * scale;
            return true;
        }

        /// <summary>Short Japanese name of the direction (for the lab UI).</summary>
        public static string DirectionName(RecoilProfile p)
        {
            string v = p.vertical == RecoilVertical.Up ? "上" : p.vertical == RecoilVertical.Down ? "下" : "";
            string h = p.horizontal switch
            {
                RecoilHorizontal.Left => "左",
                RecoilHorizontal.Right => "右",
                RecoilHorizontal.Alternating => "左右交互",
                _ => "",
            };
            if (p.horizontal == RecoilHorizontal.Alternating && v != "")
                return v + "＋左右交互";
            return h == "" ? (v == "" ? "なし" : v) : (v == "" ? h : h + v);
        }
    }
}
