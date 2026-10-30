using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using Deadlock.View;
using UnityEngine;
using UnityEngine.UI;

namespace Deadlock.UI
{
    /// <summary>
    /// Minimal prototype HUD: crosshair (gap follows spread), ammo panel, reload progress, hit marker, posture and sprint
    /// state. Reads player state only; never changes it.
    /// The ammo panel is one box placed by the touch layout (<see cref="TouchLayout.Ammo"/>, moved in the layout editor,
    /// saved with the buttons): on top "loaded / reserve" (reserve "∞" where it never runs out, the firing range), under
    /// it the gun in hand's side view from the skin catalog (with its skin), or the gun's name where it has none yet (the
    /// SMG candidate); with the knife out, the knife's name. The posture line hangs under the panel.
    /// The whole panel (count, divider, picture or name, face, frame, posture line) takes the on-screen controls'
    /// transparency setting through one CanvasGroup: exactly the controls' own multiplier
    /// (<see cref="TouchControlsOpacity"/>: 1 − transparency / 100, full strength while the layout editor is open), so
    /// it always looks as see-through as the buttons; the parts keep their own alpha under it (no second group).
    /// <code>
    /// ┌──────────────┐
    /// │   30 / ∞     │
    /// ├──────────────┤
    /// │ [side view]  │
    /// └──────────────┘
    ///          立ち
    /// </code>
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
        PlayerHealth health;
        int stanceState = -1;
        Image hpFill;
        Text hpText;
        Image damageFlash;
        float damageFlashStart = -10f;
        RectTransform ammoPanel;
        CanvasGroup ammoGroup;
        TouchControlsOpacity controlsOpacity;
        RectTransform ammoArtHost;
        Text ammoCaption;
        string ammoArtId;
        SkinCatalog skins;

        // Damage flash: short and faint. A hit during a running flash does not brighten it again, so several
        // zombies hitting in quick succession never strobe the screen.
        const float DamageFlashTime = 0.2f;
        const float DamageFlashAlpha = 0.18f;

        /// <summary>HUD text the floor map keeps clear of (ammo / melee name, posture line), with their widest contents.</summary>
        public Text AmmoText => ammoText;
        /// <summary>The ammo panel (count, divider, side view), placed by the touch layout.</summary>
        public RectTransform AmmoPanel => ammoPanel;
        /// <summary>The ammo panel's see-through multiplier now (the on-screen controls').</summary>
        public float AmmoPanelAlpha => ammoGroup != null ? ammoGroup.alpha : 1f;

        /// <summary>The side view shown in the ammo panel (null when the gun has none, or the knife is out).</summary>
        public Image AmmoArt { get; private set; }
        /// <summary>Shown instead of the side view: the gun's name when it has none, or empty.</summary>
        public Text AmmoCaption => ammoCaption;

        /// <summary>Size of the ammo count (canvas units; the count used to be 56 / 64), and the height of its row.</summary>
        public const int AmmoCountFont = 40;
        public const float AmmoCountRow = 54f;

        /// <summary>The ammo line: the rounds in the magazine, then the reserve ("∞" when it never runs out).</summary>
        public static string AmmoLine(AmmoState ammo) => $"{ammo.Loaded} / {(ammo.InfiniteReserve ? "∞" : ammo.Reserve.ToString())}";
        public Text StanceText => stanceText;
        public string MeleeName => melee != null && melee.Definition != null ? melee.Definition.displayName : "";
        /// <summary>The HP bar and its number (bottom centre), when the player has health.</summary>
        public RectTransform HealthArea => hpFill != null ? (RectTransform)hpFill.transform.parent : null;

        /// <summary>Current alpha of the red damage flash (0 when not shown).</summary>
        public float DamageFlashLevel => damageFlash != null ? damageFlash.color.a : 0f;

        void Awake()
        {
            weapons = gun.GetComponent<WeaponSwitcher>();
            melee = gun.GetComponent<MeleeWeapon>();
            posture = gun.GetComponent<PostureController>();
            motor = gun.GetComponent<FirstPersonMotor>();
            health = gun.GetComponent<PlayerHealth>();
            if (health != null)
                BuildHealthWidgets();
            BuildAmmoPanel();
            CentreOnScreen();
        }

        /// <summary>
        /// The crosshair and the hit marker mark where shots go: the camera's centre, which is the screen's centre. They
        /// were laid out in the Safe Area, whose centre is not the screen's where its insets differ (an iPhone in landscape:
        /// 63 px for the home indicator at the bottom, none at the top, so 31.5 px ≈ 10.5 pt higher), and every shot landed
        /// that far below the crosshair. Both now hang from the canvas (the whole screen), centred.
        /// </summary>
        void CentreOnScreen()
        {
            foreach (var r in new[] { crosshairGroup != null ? (RectTransform)crosshairGroup.transform : null, hitMarker != null ? hitMarker.rectTransform : null })
            {
                if (r == null || canvasRect == null)
                    continue;
                r.SetParent(canvasRect, false);
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>The crosshair's centre on screen (pixels): where the shots go.</summary>
        public Vector2 CrosshairScreenPoint
        {
            get
            {
                var c = new Vector3[4];
                ((RectTransform)crosshairGroup.transform).GetWorldCorners(c);
                var canvas = canvasRect.GetComponentInParent<Canvas>();
                Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                return RectTransformUtility.WorldToScreenPoint(uiCam, (c[0] + c[2]) * 0.5f);
            }
        }

        void Start()
        {
            var menu = FindAnyObjectByType<GameMenu>(FindObjectsInactive.Include);
            skins = menu != null ? menu.Skins : null;
            ammoArtId = null; // shown with the catalog from the next frame
            // Placed by the saved layout now, and by the layout's applier from then on (also while editing).
            PlaceAmmo(PlacedOnScreen(TouchLayout.Load().Get(TouchLayout.Ammo)));
            var applier = FindAnyObjectByType<TouchLayoutApplier>(FindObjectsInactive.Include);
            if (applier != null)
                applier.Attach(TouchLayout.Ammo, ammoPanel, PlaceAmmo);
            controlsOpacity = FindAnyObjectByType<TouchControlsOpacity>(FindObjectsInactive.Include);
            ApplyControlsAlpha();
        }

        /// <summary>The on-screen controls' multiplier (their own while they are shown, else from the same setting).</summary>
        void ApplyControlsAlpha()
        {
            if (ammoGroup == null)
                return;
            ammoGroup.alpha = controlsOpacity != null && controlsOpacity.isActiveAndEnabled
                ? controlsOpacity.Alpha
                : 1f - ControlSettings.TouchControlTransparency / 100f;
        }

        TouchLayoutItem PlacedOnScreen(TouchLayoutItem item)
        {
            var safe = (RectTransform)ammoPanel.parent;
            item.center = TouchLayout.ClampBox(item.center, TouchLayout.BoxSize(item), safe.rect.size, TouchLayoutApplier.SideRoomOf(safe));
            return item;
        }

        /// <summary>Puts the ammo panel at a layout item's place and size (centre normalised to the Safe Area).</summary>
        void PlaceAmmo(TouchLayoutItem item)
        {
            ammoPanel.anchorMin = ammoPanel.anchorMax = item.center;
            ammoPanel.anchoredPosition = Vector2.zero;
            ammoPanel.localScale = new Vector3(item.scale, item.scale, 1f);
        }

        /// <summary>
        /// The ammo panel: a framed box with the count (the scene's ammo text, moved in) over a divider and the gun's side
        /// view; the posture line moves under it.
        /// </summary>
        void BuildAmmoPanel()
        {
            var safe = (RectTransform)ammoText.transform.parent;
            ammoPanel = UiBuild.Rect("AmmoPanel", safe);
            ammoPanel.pivot = new Vector2(0.5f, 0.5f);
            ammoPanel.sizeDelta = TouchLayout.AmmoBaseSize;
            ammoGroup = ammoPanel.gameObject.AddComponent<CanvasGroup>();
            ammoGroup.interactable = false;
            ammoGroup.blocksRaycasts = false;
            var face = UiBuild.Image(ammoPanel, new Color(0f, 0f, 0f, 0.28f), false);
            face.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.45f);
            float countH = AmmoCountRow;
            var count = ammoText.rectTransform;
            count.SetParent(ammoPanel, false);
            count.anchorMin = new Vector2(0f, 1f);
            count.anchorMax = new Vector2(1f, 1f);
            count.pivot = new Vector2(0.5f, 1f);
            count.anchoredPosition = new Vector2(0f, -2f);
            count.sizeDelta = new Vector2(-16f, countH);
            ammoText.alignment = TextAnchor.MiddleCenter;
            ammoText.resizeTextForBestFit = true; // "180 / 30" still fits
            ammoText.resizeTextMinSize = 24;
            ammoText.resizeTextMaxSize = AmmoCountFont;
            ammoText.fontSize = AmmoCountFont;
            var divider = UiBuild.Rect("Divider", ammoPanel);
            divider.anchorMin = new Vector2(0f, 1f);
            divider.anchorMax = new Vector2(1f, 1f);
            divider.pivot = new Vector2(0.5f, 0.5f);
            divider.anchoredPosition = new Vector2(0f, -countH - 4f);
            divider.sizeDelta = new Vector2(-12f, 2f);
            UiBuild.Image(divider, new Color(1f, 1f, 1f, 0.45f), false);
            ammoArtHost = UiBuild.Stretch(UiBuild.Rect("ArtArea", ammoPanel));
            ammoArtHost.offsetMin = new Vector2(10f, 6f);
            ammoArtHost.offsetMax = new Vector2(-10f, -countH - 10f);
            ammoCaption = UiBuild.Text(UiBuild.Stretch(UiBuild.Rect("Caption", ammoArtHost)), "", 24, TextAnchor.MiddleCenter);
            if (stanceText != null)
            {
                var stance = stanceText.rectTransform;
                stance.SetParent(ammoPanel, false);
                stance.anchorMin = stance.anchorMax = new Vector2(1f, 0f);
                stance.pivot = new Vector2(1f, 1f);
                stance.anchoredPosition = new Vector2(0f, -6f);
            }
            PlaceAmmo(TouchLayout.Defaults().Get(TouchLayout.Ammo));
        }

        /// <summary>Side view of the gun in hand (with its skin), or its name when it has none; nothing for the knife.</summary>
        void RefreshAmmoArt(bool meleeOut)
        {
            string id = meleeOut ? "" : gun.Definition != null ? gun.Definition.weaponId : "";
            if (id == ammoArtId)
                return;
            ammoArtId = id;
            if (AmmoArt != null)
                Destroy(AmmoArt.gameObject);
            AmmoArt = null;
            var set = skins != null && id.Length > 0 ? skins.WeaponSet(id) : null;
            if (set != null && set.sideView != null)
            {
                var art = UiBuild.Stretch(UiBuild.Rect("SideView_" + id, ammoArtHost));
                art.gameObject.AddComponent<Image>();
                art.gameObject.AddComponent<SkinnedImage>().Init(skins, set.sideView, set.sideViewRegions, id, false);
                AmmoArt = art.GetComponent<Image>();
                ammoCaption.text = "";
            }
            else
                ammoCaption.text = meleeOut ? "" : set != null ? set.label : gun.Definition != null ? gun.Definition.displayName : "";
        }

        /// <summary>HP bar (bottom centre, inside the Safe Area) and a red screen flash when hit.</summary>
        void BuildHealthWidgets()
        {
            damageFlash = UiBuild.Image(UiBuild.Stretch(UiBuild.Rect("DamageFlash", transform)), new Color(0.8f, 0f, 0f, 0f), false);
            damageFlash.transform.SetAsFirstSibling();
            var safe = (RectTransform)ammoText.transform.parent;
            var back = UiBuild.Place(UiBuild.Rect("HealthBar", safe), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(360f, 24f));
            UiBuild.Image(back, new Color(0f, 0f, 0f, 0.55f), false);
            var fill = UiBuild.Stretch(UiBuild.Rect("Fill", back));
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            hpFill = UiBuild.Image(fill, new Color(0.85f, 0.2f, 0.2f, 0.95f), false);
            hpFill.sprite = reloadBar.sprite; // filled images need a sprite
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpText = UiBuild.Text(UiBuild.Place(UiBuild.Rect("HealthText", back), new Vector2(0.5f, 1f), new Vector2(0f, 30f), new Vector2(360f, 30f)),
                "", 24, TextAnchor.LowerCenter);
            health.Damaged += (amount, from) =>
            {
                if (Time.time - damageFlashStart >= DamageFlashTime)
                    damageFlashStart = Time.time;
            };
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
            "C：しゃがみ（ダッシュ中はスライディング）　／　Z：伏せ　／　1：銃　／　2：ナイフ\n" +
            "左クリック：射撃　／　右クリック：ADS（長押し／切り替えは設定で変更）\n" +
            "R：リロード　／　Tab：メニュー　／　Esc：カーソル解放";
        const string DesktopNotCaptured = "\n▶ Game Viewをクリックすると操作できます";
        const string TouchHelp =
            "入力モード：タッチ（マウスで1本指を再現）\n" +
            "このモードではPCのキー操作は効きません　／　F2：PC操作へ戻る";

        // Half width kept free around the top centre (menu button, training status line).
        const float TopCentreHalfWidth = 270f;

        bool helpVisible;
        int helpState = -1;
        float helpLayoutWidth = -1f;
        float helpLeft = float.NaN;

        /// <summary>Keeps this much width free at the top-left (e.g. the floor map): the help text starts right of it.</summary>
        public void ReserveTopLeft(float width)
        {
            if (helpText == null)
                return;
            var rect = helpText.rectTransform;
            if (float.IsNaN(helpLeft))
                helpLeft = rect.anchoredPosition.x;
            rect.anchoredPosition = new Vector2(Mathf.Max(helpLeft, width), rect.anchoredPosition.y);
            helpLayoutWidth = -1f;
        }

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

            // Wrap inside the space left of the ammo counter and of the top-centre menu button / training status,
            // so they never overlap (narrow 4:3 screens).
            var helpRect = helpText.rectTransform;
            float width = ((RectTransform)helpRect.parent).rect.width;
            if (Mathf.Approximately(width, helpLayoutWidth))
                return;
            helpLayoutWidth = width;
            helpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            float available = width - helpRect.anchoredPosition.x
                              - ammoText.rectTransform.sizeDelta.x + ammoText.rectTransform.anchoredPosition.x - 24f;
            available = Mathf.Min(available, width * 0.5f - TopCentreHalfWidth - helpRect.anchoredPosition.x);
            helpRect.sizeDelta = new Vector2(Mathf.Max(200f, available), helpRect.sizeDelta.y);
        }

        void LateUpdate()
        {
            UpdateHelp();
            UpdateStance();
            if (health != null)
            {
                hpFill.fillAmount = health.Health / health.MaxHealth;
                hpText.text = health.IsDead ? "HP 0" : $"HP {Mathf.CeilToInt(health.Health)}";
                float flash = 1f - Mathf.Clamp01((Time.time - damageFlashStart) / DamageFlashTime);
                damageFlash.color = new Color(0.8f, 0f, 0f, DamageFlashAlpha * flash);
            }
            AmmoState ammo = gun.Ammo;
            bool meleeOut = weapons != null && weapons.Current == WeaponSlot.Melee;
            ammoText.text = meleeOut ? melee.Definition.displayName : AmmoLine(ammo); // ∞: the firing range
            RefreshAmmoArt(meleeOut);
            ApplyControlsAlpha();

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
                Posture.Sliding => "スライディング",
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
