using Deadlock.Combat;
using Deadlock.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Minimal prototype HUD: crosshair (gap follows spread), ammo counter, reload progress, hit marker.
    /// Reads combat state only; never changes it.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] GunController gun;
        [SerializeField] ProjectileSystem projectiles;
        [SerializeField] PlayerInputRouter input;
        [SerializeField] Camera worldCamera;
        [SerializeField] RectTransform canvasRect;

        [Header("Widgets")]
        [SerializeField] RectTransform[] crosshairLines = new RectTransform[4]; // up, down, left, right
        [SerializeField] CanvasGroup crosshairGroup;
        [SerializeField] Text ammoText;
        [SerializeField] Text statusText;
        [SerializeField] Image reloadBar;
        [SerializeField] Graphic hitMarker;
        [SerializeField] Text helpText;

        [Header("Tuning")]
        [SerializeField] float minGap = 6f;
        [SerializeField] float hitMarkerTime = 0.12f;
        [SerializeField] Color killMarkerColor = new Color(1f, 0.3f, 0.2f);

        float hitMarkerUntil;

        void OnEnable()
        {
            projectiles.Hit += OnProjectileHit;
            input.ModeChanged += OnModeChanged;
        }

        void OnDisable()
        {
            projectiles.Hit -= OnProjectileHit;
            input.ModeChanged -= OnModeChanged;
        }

        void OnProjectileHit(ProjectileHit hit)
        {
            if (hit.Shooter != gun.gameObject || hit.Result == DamageResult.Ignored)
                return;
            hitMarkerUntil = Time.time + hitMarkerTime;
            hitMarker.color = hit.Result == DamageResult.Destroyed ? killMarkerColor : Color.white;
        }

        void OnModeChanged(InputMode mode)
        {
            if (helpText == null)
                return;
            helpText.text = mode == InputMode.Desktop
                ? "WASD move / Mouse look / Shift sprint / Space jump\nLMB fire / RMB (hold) ADS / R reload / Esc cursor / F2 touch mode"
                : Application.isMobilePlatform ? "" : "TOUCH MODE (mouse simulates one finger) / F2 desktop mode";
        }

        void LateUpdate()
        {
            AmmoState ammo = gun.Ammo;
            ammoText.text = $"{ammo.Loaded} / {ammo.Reserve}";

            if (gun.IsReloading)
                statusText.text = "RELOADING";
            else if (ammo.IsCompletelyEmpty)
                statusText.text = "NO AMMO";
            else if (ammo.Loaded == 0)
                statusText.text = "RELOAD";
            else
                statusText.text = "";

            reloadBar.enabled = gun.IsReloading;
            reloadBar.fillAmount = gun.ReloadProgress;

            UpdateCrosshair();
            hitMarker.enabled = Time.time < hitMarkerUntil;
        }

        void UpdateCrosshair()
        {
            // Convert the spread cone to canvas units so the crosshair gap matches where shots can land.
            float halfFov = worldCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float spread = Mathf.Tan(gun.CurrentSpreadDegrees * Mathf.Deg2Rad) / Mathf.Tan(halfFov);
            float gap = minGap + spread * canvasRect.rect.height * 0.5f;

            crosshairLines[0].anchoredPosition = new Vector2(0f, gap);
            crosshairLines[1].anchoredPosition = new Vector2(0f, -gap);
            crosshairLines[2].anchoredPosition = new Vector2(-gap, 0f);
            crosshairLines[3].anchoredPosition = new Vector2(gap, 0f);

            // The sight picture replaces the crosshair once fully aimed.
            crosshairGroup.alpha = 1f - Mathf.InverseLerp(0.4f, 0.8f, gun.AdsBlend);
        }
    }
}
