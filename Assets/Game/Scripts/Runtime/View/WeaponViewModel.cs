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
        [Tooltip("Optional: the camera's sprint step motion; the hands move in step with it.")]
        [SerializeField] CameraBob cameraBob;
        [SerializeField] SpriteRenderer adsMuzzleFlash;
        [Tooltip("Optional: skins (the chosen weapon / hand-arm colours reach the hip and ADS images).")]
        [SerializeField] SkinCatalog skins;

        WeaponViewProfile profile;
        readonly Vector4[] skinColours = new Vector4[SkinCatalog.TableSize];
        WeaponSwitcher switcher;
        float kick;
        float bobPhase;
        float flashUntil;
        Material hipDefaultMaterial, adsDefaultMaterial;

        void Awake()
        {
            switcher = gun.GetComponent<WeaponSwitcher>();
            hipDefaultMaterial = hipSprite.sharedMaterial;
            adsDefaultMaterial = adsSprite.sharedMaterial;
            ApplyProfile();
        }

        /// <summary>Sprites, flash places and skin from the gun's current definition (again after a gun swap).</summary>
        void ApplyProfile()
        {
            profile = gun.Definition.viewProfile;
            hipSprite.sprite = profile.hipSprite;
            adsSprite.sprite = profile.adsSprite;
            if (profile.muzzleFlashSprite != null)
                hipMuzzleFlash.sprite = adsMuzzleFlash.sprite = profile.muzzleFlashSprite;
            // Flashes sit on the muzzle of the sprite they belong to (hip pivot = muzzle).
            hipMuzzleFlash.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            hipMuzzleFlash.transform.localScale = Vector3.one * profile.hipMuzzleFlashScale;
            adsMuzzleFlash.transform.localPosition = new Vector3(profile.adsMuzzleOffset.x, profile.adsMuzzleOffset.y, 0.01f);
            adsMuzzleFlash.transform.localScale = Vector3.one * profile.adsMuzzleFlashScale;
            // In ADS the muzzle sits just under the front sight: draw the flash in front so it is not hidden by the sight.
            adsMuzzleFlash.sortingOrder = adsSprite.sortingOrder + 1;
            hipMuzzleFlash.sortingOrder = hipSprite.sortingOrder + 1;
            ApplySkin();
        }

        void OnEnable()
        {
            gun.Fired += OnFired;
            gun.DefinitionChanged += ApplyProfile;
            SkinSelection.Changed += ApplySkin;
        }

        void OnDisable()
        {
            gun.Fired -= OnFired;
            gun.DefinitionChanged -= ApplyProfile;
            SkinSelection.Changed -= ApplySkin;
        }

        /// <summary>Re-colours the hip and ADS images with the current choice (looks only; the flash keeps its colour).</summary>
        void ApplySkin()
        {
            if (skins == null || profile == null)
                return;
            skins.FillColours(gun.Definition.weaponId, skinColours);
            // A gun without a region map (after a swap) goes back to its drawn colours, never keeps another gun's map.
            if (!SpriteSkin.Apply(hipSprite, profile.hipSkinRegions, skins, skinColours))
                Unskin(hipSprite, hipDefaultMaterial);
            if (!SpriteSkin.Apply(adsSprite, profile.adsSkinRegions, skins, skinColours))
                Unskin(adsSprite, adsDefaultMaterial);
            string id = gun.Definition.weaponId;
            Debug.Log($"[DEADLOCK][skin] applied {id}: weapon={SkinSelection.GetWeaponSkinId(id)} player={SkinSelection.PlayerSkinId}");
        }

        static void Unskin(SpriteRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.SetPropertyBlock(null);
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
            float follow = aspect < p.referenceAspect ? p.hipRightEdgeFollowNarrow : p.hipRightEdgeFollow;
            float edgeShift = (aspect - p.referenceAspect) * HalfHeight * follow;
            float narrow = Mathf.InverseLerp(p.referenceAspect, 4f / 3f, aspect);
            float hipScale = Mathf.Lerp(1f, p.hipScaleAt4x3, narrow);

            // Small walk bob, nearly off while aiming.
            Vector3 v = motor.Velocity;
            float planarSpeed = new Vector2(v.x, v.z).magnitude;
            float bobWeight = motor.IsGrounded ? Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, motor.WalkSpeed)) : 0f;
            bobPhase += dt * p.bobFrequency * Mathf.PI * 2f * Mathf.Max(0.2f, bobWeight) * (motor.IsSprinting ? 1.35f : 1f);
            Vector2 bob = new Vector2(Mathf.Cos(bobPhase), -Mathf.Abs(Mathf.Sin(bobPhase))) * (p.bobAmount * bobWeight * Mathf.Lerp(1f, 0.1f, t));
            if (cameraBob != null && cameraBob.Blend > 0f)
            {
                // Sprint: the hands dip a little after the camera (heavier), and the walk bob gives way to it.
                float k = cameraBob.Blend * (1f - t);
                float down = 0.5f - 0.5f * Mathf.Cos((cameraBob.StepPhase - 0.08f) * 2f * Mathf.PI);
                float side = Mathf.Sin(cameraBob.StepPhase * Mathf.PI) * cameraBob.Side;
                bob = bob * (1f - 0.6f * cameraBob.Blend) + new Vector2(side * p.sprintBob.y, -down * p.sprintBob.x) * k;
            }

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
            Vector2 hipTarget = p.hipToAdsRelative ? hipRest + p.hipToAdsMuzzlePosition : p.hipToAdsMuzzlePosition;
            Vector2 hip = Vector2.Lerp(hipRest, hipTarget, t) + shared;
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
