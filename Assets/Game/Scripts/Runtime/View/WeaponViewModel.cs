using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// First-person 2D weapon sprites. Rendered by a dedicated overlay camera on the ViewModel layer,
    /// so they never clip into walls and keep their own resolution independent of the world.
    /// Hip and ADS use separate sprites: the hip sprite slides toward the centre and fades out while the
    /// ADS (sight picture) sprite rises into place, and the world camera FOV narrows at the same time.
    /// </summary>
    public sealed class WeaponViewModel : MonoBehaviour
    {
        [SerializeField] GunController gun;
        [SerializeField] FirstPersonMotor motor;
        [SerializeField] Camera worldCamera;

        [Header("Sprites")]
        [SerializeField] SpriteRenderer hipSprite;
        [SerializeField] SpriteRenderer adsSprite;
        [SerializeField] SpriteRenderer hipMuzzleFlash;
        [SerializeField] SpriteRenderer adsMuzzleFlash;

        [Header("Poses (local positions under this transform)")]
        [SerializeField] Vector3 hipPosition = new Vector3(0.36f, -0.27f, 1f);
        [SerializeField] Vector3 hipToAdsPosition = new Vector3(0.12f, -0.36f, 1f);
        [SerializeField] Vector3 adsStartPosition = new Vector3(0f, -0.22f, 1f);
        [SerializeField] Vector3 adsPosition = new Vector3(0f, 0f, 1f);
        [SerializeField, Range(0f, 1f)] float crossfadeStart = 0.3f;
        [SerializeField, Range(0f, 1f)] float crossfadeEnd = 0.75f;

        [Header("Feel")]
        [SerializeField] float bobAmount = 0.012f;
        [SerializeField] float bobFrequency = 1.8f;
        [SerializeField] float kickDistance = 0.025f;
        [SerializeField] float kickRecoverSpeed = 14f;
        [SerializeField] float muzzleFlashTime = 0.045f;
        [SerializeField] float reloadDrop = 0.22f;
        [SerializeField] float reloadTilt = 25f;

        float kick;
        float bobPhase;
        float flashUntil;

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
            flashUntil = Time.time + muzzleFlashTime;
            float angle = Random.Range(0f, 360f);
            hipMuzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            adsMuzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            WeaponDefinition def = gun.Definition;
            float t = Smooth(gun.AdsBlend);

            worldCamera.fieldOfView = Mathf.Lerp(def.hipFov, def.adsFov, t);

            // Walk bob, reduced while aiming.
            Vector3 v = motor.Velocity;
            float planarSpeed = new Vector2(v.x, v.z).magnitude;
            float bobWeight = motor.IsGrounded ? Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, motor.WalkSpeed)) : 0f;
            bobPhase += dt * bobFrequency * Mathf.PI * 2f * Mathf.Max(0.2f, bobWeight) * (motor.IsSprinting ? 1.35f : 1f);
            Vector3 bob = new Vector3(Mathf.Cos(bobPhase) * bobAmount, -Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmount, 0f)
                          * bobWeight * Mathf.Lerp(1f, 0.15f, t);

            kick = Mathf.MoveTowards(kick, 0f, kickRecoverSpeed * dt);
            Vector3 kickOffset = new Vector3(0f, -0.3f * kickDistance, -kickDistance) * kick * Mathf.Lerp(1f, 0.4f, t);

            // Reload: dip down and tilt, then come back.
            float reloadCurve = gun.IsReloading ? Mathf.Sin(gun.ReloadProgress * Mathf.PI) : 0f;
            Vector3 reloadOffset = new Vector3(0f, -reloadDrop * reloadCurve, 0f);
            Quaternion reloadRotation = Quaternion.Euler(0f, 0f, reloadTilt * reloadCurve);

            Vector3 shared = bob + kickOffset + reloadOffset;
            hipSprite.transform.localPosition = Vector3.Lerp(hipPosition, hipToAdsPosition, t) + shared;
            hipSprite.transform.localRotation = reloadRotation;
            adsSprite.transform.localPosition = Vector3.Lerp(adsStartPosition, adsPosition, t) + shared;
            adsSprite.transform.localRotation = reloadRotation;

            float adsAlpha = Mathf.InverseLerp(crossfadeStart, crossfadeEnd, t);
            SetAlpha(hipSprite, 1f - adsAlpha);
            SetAlpha(adsSprite, adsAlpha);

            bool flash = Time.time < flashUntil;
            hipMuzzleFlash.enabled = flash && adsAlpha < 0.5f;
            adsMuzzleFlash.enabled = flash && adsAlpha >= 0.5f;
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
