using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// First-person 2D weapon sprites. Rendered by a dedicated overlay camera on the ViewModel layer,
    /// so they never clip into walls and keep their own resolution independent of the world.
    /// Hip and ADS use separate sprites pre-rendered from the same model (<see cref="WeaponViewProfile"/>):
    /// the hip weapon swings toward the centre while the sight picture rises into place, with a short crossfade,
    /// and the world camera FOV narrows at the same time.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        // The view-model camera renders sprites at z = 1 with a fixed vertical FOV of 50°.
        const float SpriteDepth = 1f;
        static readonly float HalfHeight = Mathf.Tan(25f * Mathf.Deg2Rad);

        [SerializeField] GunController gun;
        [SerializeField] FirstPersonMotor motor;
        [SerializeField] Camera worldCamera;

        [Header("Sprites")]
        [SerializeField] SpriteRenderer hipSprite;
        [SerializeField] SpriteRenderer adsSprite;
        [SerializeField] SpriteRenderer hipMuzzleFlash;
        [SerializeField] SpriteRenderer adsMuzzleFlash;

        WeaponViewProfile profile;
        WeaponSwitcher switcher;
        float kick;
        float bobPhase;
        float flashUntil;

        void Awake()
        {
            profile = gun.Definition.viewProfile;
            switcher = gun.GetComponent<WeaponSwitcher>();
            hipSprite.sprite = profile.hipSprite;
            adsSprite.sprite = profile.adsSprite;
            // Flashes sit on the muzzle of the sprite they belong to (hip pivot = muzzle).
            hipMuzzleFlash.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            hipMuzzleFlash.transform.localScale = Vector3.one * profile.hipMuzzleFlashScale;
            adsMuzzleFlash.transform.localPosition = new Vector3(profile.adsMuzzleOffset.x, profile.adsMuzzleOffset.y, 0.01f);
            adsMuzzleFlash.transform.localScale = Vector3.one * profile.adsMuzzleFlashScale;
            // In ADS the muzzle sits just under the front sight: draw the flash in front so it is not hidden by the sight.
            adsMuzzleFlash.sortingOrder = adsSprite.sortingOrder + 1;
            hipMuzzleFlash.sortingOrder = hipSprite.sortingOrder + 1;
        }

        void OnEnable()
        {
            gun.Fired += OnFired;
        }

        void OnDisable()
        {
            gun.Fired -= OnFired;
        }

        void OnFired()
        {
            kick = 1f;
            flashUntil = Time.time + profile.muzzleFlashTime;
            float angle = Random.Range(0f, 360f);
            hipMuzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            adsMuzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            WeaponDefinition def = gun.Definition;
            WeaponViewProfile p = profile;
            float t = Smooth(gun.AdsBlend);

            worldCamera.fieldOfView = Mathf.Lerp(def.hipFov, def.adsFov, t);

            // Put away / drawn while switching to the melee weapon.
            float presence = switcher != null ? switcher.Presence(WeaponSlot.Gun) : 1f;
            if (presence <= 0.001f)
            {
                hipSprite.enabled = adsSprite.enabled = hipMuzzleFlash.enabled = adsMuzzleFlash.enabled = false;
                return;
            }
            Vector2 holster = new Vector2(0.05f, -0.45f) * (1f - Smooth(presence));

            // Keep the hip weapon in the lower right on narrower (iPad 4:3) and wider (tall-phone) screens.
            float aspect = worldCamera.aspect;
            float edgeShift = (aspect - p.referenceAspect) * HalfHeight * p.hipRightEdgeFollow;
            float narrow = Mathf.InverseLerp(p.referenceAspect, 4f / 3f, aspect);
            float hipScale = Mathf.Lerp(1f, p.hipScaleAt4x3, narrow);

            // Small walk bob, nearly off while aiming.
            Vector3 v = motor.Velocity;
            float planarSpeed = new Vector2(v.x, v.z).magnitude;
            float bobWeight = motor.IsGrounded ? Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, motor.WalkSpeed)) : 0f;
            bobPhase += dt * p.bobFrequency * Mathf.PI * 2f * Mathf.Max(0.2f, bobWeight) * (motor.IsSprinting ? 1.35f : 1f);
            Vector2 bob = new Vector2(Mathf.Cos(bobPhase), -Mathf.Abs(Mathf.Sin(bobPhase))) * (p.bobAmount * bobWeight * Mathf.Lerp(1f, 0.1f, t));

            // Fire: pushed down/back toward the player, then springs back.
            kick = Mathf.MoveTowards(kick, 0f, p.kickRecoverSpeed * dt);
            float kickWeight = kick * kick * Mathf.Lerp(1f, p.adsKickMultiplier, t);
            Vector2 kickOffset = p.kickOffset * kickWeight;

            // Reload: lowered and rolled over the real reload time.
            float reloadCurve = gun.IsReloading ? Mathf.Sin(gun.ReloadProgress * Mathf.PI) : 0f;
            Vector2 reloadOffset = new Vector2(0.02f, -p.reloadDrop) * reloadCurve;
            Quaternion rotation = Quaternion.Euler(0f, 0f, p.reloadRollDegrees * reloadCurve + p.kickRollDegrees * kickWeight);
            float scale = 1f + p.kickScale * kickWeight;

            Vector2 shared = bob + kickOffset + reloadOffset + holster;

            Vector2 hipRest = p.hipMuzzlePosition + new Vector2(edgeShift, 0f);
            Vector2 hip = Vector2.Lerp(hipRest, p.hipToAdsMuzzlePosition, t) + shared;
            Place(hipSprite.transform, hip, p.hipHoldOffset * hipScale, rotation, hipScale * scale);

            Vector2 ads = Vector2.Lerp(p.adsStartOffset, Vector2.zero, t) + shared;
            Place(adsSprite.transform, ads, p.adsHoldOffset, rotation, scale);

            float adsAlpha = Mathf.InverseLerp(p.crossfadeStart, p.crossfadeEnd, t);
            SetAlpha(hipSprite, 1f - adsAlpha);
            SetAlpha(adsSprite, adsAlpha);

            bool flash = Time.time < flashUntil;
            hipMuzzleFlash.enabled = flash && adsAlpha < 0.5f;
            adsMuzzleFlash.enabled = flash && adsAlpha >= 0.5f;
        }

        /// <summary>Positions a sprite so that it turns around its hold point (hands) instead of its pivot (muzzle / sight).</summary>
        static void Place(Transform sprite, Vector2 pivotPosition, Vector2 holdOffset, Quaternion rotation, float scale)
        {
            Vector2 rotatedHold = rotation * holdOffset;
            Vector2 position = pivotPosition + holdOffset - rotatedHold;
            sprite.localPosition = new Vector3(position.x, position.y, SpriteDepth);
            sprite.localRotation = rotation;
            sprite.localScale = Vector3.one * scale;
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            renderer.enabled = alpha > 0.001f;
            Color c = renderer.color;
            c.a = alpha;
            renderer.color = c;
        }
    }
}
