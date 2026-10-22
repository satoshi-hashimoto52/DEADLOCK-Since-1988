using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// First-person knife sprite on the view-model camera. The swing follows <see cref="MeleeWeapon.Phase"/>, so the
    /// visible slash and the damage window happen at the same time: windup draws back, active slashes across,
    /// recovery returns. Raised/lowered by <see cref="WeaponSwitcher.Presence"/>.
    /// With drawn attack poses (<see cref="MeleeViewProfile.poseTimeline"/>), the image itself changes with the phase
    /// (rest -> pose 1 -> pose 2 -> pose 3 -> rest); it is chosen from the phase every frame, so a swing that is cut short
    /// (weapon put away, respawn) is back on the rest image at once.
    /// </summary>
    public sealed class MeleeViewModel : MonoBehaviour
    {
        static readonly float HalfHeight = Mathf.Tan(25f * Mathf.Deg2Rad);

        [SerializeField] MeleeWeapon melee;
        [SerializeField] WeaponSwitcher switcher;
        [SerializeField] FirstPersonMotor motor;
        [SerializeField] Camera worldCamera;
        [SerializeField] SpriteRenderer sprite;
        [Tooltip("Optional: the camera's sprint step motion; the hand moves in step with it.")]
        [SerializeField] CameraBob cameraBob;
        [Tooltip("Optional: skins (the chosen hand-arm colours, and the knife's own skin once it has a weapon mask).")]
        [SerializeField] SkinCatalog skins;

        MeleeViewProfile profile;
        float bobPhase;
        readonly Vector4[] skinColours = new Vector4[SkinCatalog.TableSize];
        int shownPose = -1;

        /// <summary>The image on screen: -1 = the rest sprite, otherwise the index into the profile's pose sprites.</summary>
        public int ShownPose => shownPose;

        void Awake()
        {
            profile = melee.Definition.viewProfile;
            sprite.sprite = profile.sprite;
            shownPose = -1;
            ApplySkin();
        }

        void OnEnable() => SkinSelection.Changed += ApplySkin;

        void OnDisable() => SkinSelection.Changed -= ApplySkin;

        /// <summary>
        /// Re-colours the knife sprite with the current choice. Each attack pose has its own region map, swapped in with
        /// the image (<see cref="ShowPose"/>); the colour table is the same for all of them.
        /// </summary>
        void ApplySkin()
        {
            if (skins == null || profile == null)
                return;
            skins.FillColours(melee.Definition.weaponId, skinColours);
            SpriteSkin.Apply(sprite, RegionsOf(shownPose), skins, skinColours);
            string id = melee.Definition.weaponId;
            Debug.Log($"[DEADLOCK][skin] applied {id}: weapon={SkinSelection.GetWeaponSkinId(id)} player={SkinSelection.PlayerSkinId}");
        }

        Texture2D RegionsOf(int pose) =>
            pose < 0 ? profile.skinRegions : pose < profile.poseSkinRegions.Length ? profile.poseSkinRegions[pose] : null;

        void ShowPose(int pose)
        {
            if (pose == shownPose)
                return;
            shownPose = pose;
            sprite.sprite = pose < 0 ? profile.sprite : profile.poseSprites[pose];
            if (skins != null)
                SpriteSkin.Apply(sprite, RegionsOf(pose), skins, skinColours);
        }

        bool HasPoses => profile.poseTimeline.Length > 0 && profile.poseSprites.Length > 0;

        /// <summary>
        /// The pose timeline at the weapon's current phase: which image, and its grip (reference aspect, before the edge
        /// follow) and roll. Idle, or a moment no stretch covers, is the rest image at rest.
        /// </summary>
        public static int EvaluatePoses(MeleeViewProfile p, MeleePhase phase, float progress, out Vector2 grip, out float roll)
        {
            grip = p.gripPosition;
            roll = 0f;
            if (phase == MeleePhase.Idle)
                return -1;
            MeleePoseKey[] keys = p.poseTimeline;
            for (int i = 0; i < keys.Length; i++)
            {
                MeleePoseKey key = keys[i];
                bool last = i == keys.Length - 1 || keys[i + 1].phase != key.phase;
                if (key.phase != phase || progress < key.from || (progress >= key.to && !(last && progress <= key.to)))
                    continue;
                float u = key.to > key.from ? Mathf.Clamp01((progress - key.from) / (key.to - key.from)) : 1f;
                u = key.easeOut ? 1f - (1f - u) * (1f - u) : Ease(u);
                grip = (key.relativeToRest ? p.gripPosition : key.grip) + Vector2.Lerp(key.offsetFrom, key.offsetTo, u);
                roll = Mathf.Lerp(key.rollFrom, key.rollTo, u);
                return key.pose < p.poseSprites.Length ? key.pose : -1;
            }
            return -1;
        }

        /// <summary>
        /// Screen x of a grip after the right-edge follow and the narrow-screen pull toward
        /// <see cref="MeleeViewProfile.narrowCentreX"/> (the whole path is scaled, so nothing jumps; images keep their size).
        /// </summary>
        public static float PlaceX(MeleeViewProfile p, float x, float aspect)
        {
            x += (aspect - p.referenceAspect) * HalfHeight * p.rightEdgeFollow;
            if (p.narrowScaleAt4x3 >= 1f)
                return x;
            float narrow = Mathf.InverseLerp(p.referenceAspect, 4f / 3f, aspect);
            return p.narrowCentreX + (x - p.narrowCentreX) * Mathf.Lerp(1f, p.narrowScaleAt4x3, narrow);
        }

        void LateUpdate()
        {
            MeleeViewProfile p = profile;
            float presence = switcher.Presence(WeaponSlot.Melee);
            sprite.enabled = presence > 0.001f;
            if (!sprite.enabled)
                return;

            float aspect = worldCamera.aspect;
            float edgeShift = (aspect - p.referenceAspect) * HalfHeight * p.rightEdgeFollow;
            Vector2 rest = p.gripPosition + new Vector2(edgeShift, 0f);

            // Swing pose from the weapon's phase (the view never runs its own timer).
            Vector2 offset = Vector2.zero;
            float roll = 0f;
            float scale = 1f;
            float k = melee.PhaseProgress;
            if (HasPoses)
            {
                ShowPose(EvaluatePoses(p, melee.Phase, k, out Vector2 grip, out roll));
                rest = new Vector2(PlaceX(p, grip.x, aspect), grip.y);
            }
            else switch (melee.Phase)
            {
                case MeleePhase.Windup:
                    k = Ease(k);
                    offset = p.windupOffset * k;
                    roll = p.windupRoll * k;
                    break;
                case MeleePhase.Active:
                    k = p.strikeEaseOutPower > 0f ? 1f - Mathf.Pow(1f - k, p.strikeEaseOutPower) : Ease(k);
                    if (p.useStrikeArc)
                    {
                        // Quadratic curve windup -> mid -> strike: the blade sweeps across instead of being pushed forward.
                        float a = (1f - k) * (1f - k), b = 2f * (1f - k) * k, c = k * k;
                        offset = a * p.windupOffset + b * p.strikeMidOffset + c * p.strikeOffset;
                        roll = a * p.windupRoll + b * p.strikeMidRoll + c * p.strikeRoll;
                    }
                    else
                    {
                        offset = Vector2.Lerp(p.windupOffset, p.strikeOffset, k);
                        roll = Mathf.Lerp(p.windupRoll, p.strikeRoll, k);
                    }
                    scale = Mathf.Lerp(1f, p.strikeScale, k);
                    break;
                case MeleePhase.Recovery:
                    k = Ease(k);
                    offset = Vector2.Lerp(p.strikeOffset, Vector2.zero, k);
                    roll = Mathf.Lerp(p.strikeRoll, 0f, k);
                    scale = Mathf.Lerp(p.strikeScale, 1f, k);
                    break;
            }

            Vector3 v = motor.Velocity;
            float bobWeight = motor.IsGrounded ? Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / Mathf.Max(0.01f, motor.WalkSpeed)) : 0f;
            bobPhase += Time.deltaTime * Mathf.PI * 2f * 1.8f * Mathf.Max(0.2f, bobWeight);
            Vector2 bob = new Vector2(Mathf.Cos(bobPhase), -Mathf.Abs(Mathf.Sin(bobPhase))) * (p.bobAmount * bobWeight);
            if (cameraBob != null && cameraBob.Blend > 0f)
            {
                float down = 0.5f - 0.5f * Mathf.Cos((cameraBob.StepPhase - 0.08f) * 2f * Mathf.PI);
                float side = Mathf.Sin(cameraBob.StepPhase * Mathf.PI) * cameraBob.Side;
                bob = bob * (1f - 0.6f * cameraBob.Blend) + new Vector2(side * p.sprintBob.y, -down * p.sprintBob.x) * cameraBob.Blend;
            }

            Vector2 position = rest + offset + bob + new Vector2(0f, -p.lowered * (1f - Ease(presence)));
            sprite.transform.localPosition = new Vector3(position.x, position.y, 1f);
            sprite.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
            sprite.transform.localScale = Vector3.one * scale;
        }

        static float Ease(float x) => x * x * (3f - 2f * x);
    }
}
