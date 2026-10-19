using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Minimal prototype HUD: crosshair (gap follows spread), ammo counter / weapon name, reload progress, hit marker,
    /// posture and sprint state. Reads player state only; never changes it.
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
        [Tooltip("Posture / sprint / 'no room to stand' line under the ammo counter.")]
        [SerializeField] Text stanceText;

        [Header("Tuning")]
        [SerializeField] float minGap = 6f;
        [SerializeField] float hitMarkerTime = 0.12f;
        [SerializeField] Color killMarkerColor = new Color(1f, 0.3f, 0.2f);

        float hitMarkerUntil;
        WeaponSwitcher weapons;
        MeleeWeapon melee;
        PostureController posture;
        FirstPersonMotor motor;
        int stanceState = -1;

        void Awake()
        {
            weapons = gun.GetComponent<WeaponSwitcher>();
            melee = gun.GetComponent<MeleeWeapon>();
            posture = gun.GetComponent<PostureController>();
            motor = gun.GetComponent<FirstPersonMotor>();
        }

        void OnEnable()
        {
            projectiles.Hit += OnProjectileHit;
            input.ModeChanged += OnModeChanged;
            if (melee != null)
                melee.Hit += OnMeleeHit;
        }

        void OnDisable()
        {
            projectiles.Hit -= OnProjectileHit;
            input.ModeChanged -= OnModeChanged;
            if (melee != null)
                melee.Hit -= OnMeleeHit;
        }

        void OnMeleeHit(Collider target, DamageResult result)
        {
            if (result == DamageResult.Ignored)
                return;
            hitMarkerUntil = Time.time + hitMarkerTime;
            hitMarker.color = result == DamageResult.Destroyed ? killMarkerColor : Color.white;
        }

        void OnProjectileHit(ProjectileHit hit)
        {
            if (hit.Shooter != gun.gameObject || hit.Result == DamageResult.Ignored)
                return;
            hitMarkerUntil = Time.time + hitMarkerTime;
            hitMarker.color = hit.Result == DamageResult.Destroyed ? killMarkerColor : Color.white;
        }

        // Editor / desktop check guidance only. Never shown on mobile devices.
        const string DesktopHelp =
            "入力モード：PC（キーボード／マウス）　F2：タッチモードへ\n" +
            "Space：ジャンプ　／　WASD：移動　／　Shift：ダッシュ　／　マウス：視点\n" +
            "C：しゃがみ　／　Z：伏せ　／　1：銃　／　2：ナイフ\n" +
            "左クリック：射撃　／　右クリック長押し：ADS\n" +
            "R：リロード　／　Esc：カーソル解放";
        const string DesktopNotCaptured = "\n▶ Game Viewをクリックすると操作できます";
        const string TouchHelp =
            "入力モード：タッチ（マウスで1本指を再現）\n" +
            "このモードではPCのキー操作は効きません　／　F2：PC操作へ戻る";

        bool helpVisible;
        int helpState = -1;
        float helpLayoutWidth = -1f;

        void OnModeChanged(InputMode mode)
        {
            helpVisible = helpText != null && !Application.isMobilePlatform;
            if (helpText != null)
                helpText.enabled = helpVisible;
            helpState = -1;
        }

        void UpdateHelp()
        {
            if (!helpVisible)
                return;
            int state = input.Mode == InputMode.Touch ? 2 : input.DesktopInputActive ? 1 : 0;
            if (state != helpState)
            {
                helpState = state;
                helpText.text = state == 2 ? TouchHelp : state == 1 ? DesktopHelp : DesktopHelp + DesktopNotCaptured;
            }

            // Wrap inside the space left of the ammo counter so the two never overlap (narrow 4:3 screens).
            var helpRect = helpText.rectTransform;
            float width = ((RectTransform)helpRect.parent).rect.width;
            if (Mathf.Approximately(width, helpLayoutWidth))
                return;
            helpLayoutWidth = width;
            helpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float available = width - helpRect.anchoredPosition.x
                              - ammoText.rectTransform.sizeDelta.x + ammoText.rectTransform.anchoredPosition.x - 24f;
            helpRect.sizeDelta = new Vector2(Mathf.Max(200f, available), helpRect.sizeDelta.y);
        }

        void LateUpdate()
        {
            UpdateHelp();
            UpdateStance();
            AmmoState ammo = gun.Ammo;
            bool meleeOut = weapons != null && weapons.Current == WeaponSlot.Melee;
            ammoText.text = meleeOut ? melee.Definition.displayName : $"{ammo.Loaded} / {ammo.Reserve}";

            if (meleeOut)
                statusText.text = "";
            else if (gun.IsReloading)
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

        void UpdateStance()
        {
            if (stanceText == null || posture == null)
                return;
            bool blocked = Time.time - posture.LastBlockedTime < 1.2f;
            int state = (int)posture.Current * 4 + (motor.IsSprinting ? 1 : 0) + (blocked ? 2 : 0);
            if (state == stanceState)
                return;
            stanceState = state;
            string name = posture.Current switch
            {
                Posture.Crouching => "しゃがみ",
                Posture.Prone => "伏せ",
                _ => "立ち",
            };
            if (motor.IsSprinting)
                name += "　ダッシュ";
            if (blocked)
                name += "\n頭上が塞がっています";
            stanceText.text = name;
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
