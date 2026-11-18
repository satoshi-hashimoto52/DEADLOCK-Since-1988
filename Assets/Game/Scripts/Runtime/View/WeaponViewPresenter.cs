using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// The first-person gun's look, shared by the offline view model (<see cref="WeaponViewModel"/>, fed by the local
    /// gun) and the online views (fed by the server's confirmed state, Stage 38): the hip / ADS sprites of a
    /// <see cref="WeaponViewProfile"/>, where they sit on a screen of any aspect, the kick, the reload lowering and roll,
    /// the put-away / draw motion and the hip ⇄ ADS cross-fade, the muzzle flashes, and the optional glow layers of the
    /// hip and ADS images (emission core and glow, drawn as children of their sprite with its pivot, so they follow every
    /// move; each image has its own glow settings).
    /// Looks only: no combat value lives or is decided here.
    /// </summary>
    public sealed class WeaponViewPresenter
    {
        /// <summary>The sprites are drawn at this depth in front of the view-model camera (vertical FOV 50°).</summary>
        public const float SpriteDepth = 1f;
        public static readonly float HalfHeight = Mathf.Tan(25f * Mathf.Deg2Rad);

        /// <summary>How far a jet-along-the-barrel flash may turn off the barrel per shot (degrees either way).</summary>
        public const float FlashJitterDegrees = 6f;

        /// <summary>
        /// Off: the glow (the soft light around the emission core) is not drawn anywhere; the gun and its emission core
        /// are drawn as without it. A switch for checks and settings (looks only).
        /// </summary>
        public static bool GlowEnabled = true;

        /// <summary>What drives the pose this frame (the source differs: the local gun offline, the server's state online).</summary>
        public struct Input
        {
            /// <summary>Hip (0) → ADS (1), already eased.</summary>
            public float Ads;
            /// <summary>Raised into view (1) or put away (0): weapon switches.</summary>
            public float Presence;
            /// <summary>The screen's aspect (width / height).</summary>
            public float Aspect;
            /// <summary>Walk / sprint hand motion (view units), already weighted.</summary>
            public Vector2 Bob;
            public bool Reloading;
            /// <summary>0 → 1 over the reload.</summary>
            public float ReloadProgress;
            public float DeltaTime;
            public float Time;
        }

        readonly SpriteRenderer hip, ads, hipFlash, adsFlash;
        readonly SpriteRenderer emission, glow;
        readonly SpriteRenderer adsEmission, adsGlow;
        readonly Material glowDefaultMaterial, adsGlowDefaultMaterial;
        WeaponViewProfile profile;
        float kick;
        float flashUntil;

        /// <summary>The ADS image's share of the cross-fade last frame (0 hip … 1 ADS).</summary>
        public float AdsAlpha { get; private set; }
        public WeaponViewProfile Profile => profile;
        public SpriteRenderer Hip => hip;
        public SpriteRenderer Ads => ads;
        public SpriteRenderer HipFlash => hipFlash;
        public SpriteRenderer AdsFlash => adsFlash;
        /// <summary>The emission core over the hip image (no sprite: the gun has none).</summary>
        public SpriteRenderer Emission => emission;
        /// <summary>The glow over the hip image (no sprite: the gun has none).</summary>
        public SpriteRenderer Glow => glow;
        /// <summary>The emission core over the ADS image (no sprite: the gun has none).</summary>
        public SpriteRenderer AdsEmission => adsEmission;
        /// <summary>The glow over the ADS image (no sprite: the gun has none).</summary>
        public SpriteRenderer AdsGlow => adsGlow;
        /// <summary>The current kick (1 just after a shot, back to 0).</summary>
        public float Kick => kick;

        /// <summary>
        /// Takes the four renderers of a view (hip / ADS sprites and their flashes, as the scene builder or the online view
        /// made them) and adds each image's two glow layers as its children (same layer, that image's default material).
        /// </summary>
        public WeaponViewPresenter(SpriteRenderer hip, SpriteRenderer ads, SpriteRenderer hipFlash, SpriteRenderer adsFlash)
        {
            this.hip = hip;
            this.ads = ads;
            this.hipFlash = hipFlash;
            this.adsFlash = adsFlash;
            glow = Layer(hip, "Glow");
            emission = Layer(hip, "Emission");
            glowDefaultMaterial = glow.sharedMaterial;
            adsGlow = Layer(ads, "Glow");
            adsEmission = Layer(ads, "Emission");
            adsGlowDefaultMaterial = adsGlow.sharedMaterial;
        }

        static SpriteRenderer Layer(SpriteRenderer parent, string name)
        {
            var existing = parent.transform.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var r = go.GetComponent<SpriteRenderer>();
            if (r == null)
                r = go.AddComponent<SpriteRenderer>();
            r.sharedMaterial = parent.sharedMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return r;
        }

        /// <summary>
        /// Sprites, flash places and glow layers from a profile (again after a gun swap). Null: nothing is drawn — never
        /// another gun's images. A profile without a hip (or ADS) image draws nothing in that view.
        /// </summary>
        public void SetProfile(WeaponViewProfile p)
        {
            profile = p;
            if (p == null)
            {
                hip.sprite = ads.sprite = null;
                emission.sprite = glow.sprite = null;
                adsEmission.sprite = adsGlow.sprite = null;
                return;
            }
            hip.sprite = p.hipSprite;
            ads.sprite = p.adsSprite;
            emission.sprite = p.hipSprite != null ? p.hipEmissionSprite : null;
            glow.sprite = p.hipSprite != null ? p.hipGlowSprite : null;
            glow.sharedMaterial = p.glowMaterial != null ? p.glowMaterial : glowDefaultMaterial;
            adsEmission.sprite = p.adsSprite != null ? p.adsEmissionSprite : null;
            adsGlow.sprite = p.adsSprite != null ? p.adsGlowSprite : null;
            adsGlow.sharedMaterial = p.adsGlowMaterial != null ? p.adsGlowMaterial : adsGlowDefaultMaterial;
            if (p.muzzleFlashSprite != null)
                hipFlash.sprite = adsFlash.sprite = p.muzzleFlashSprite;
            // Flashes sit on the muzzle of the sprite they belong to (hip pivot = muzzle), just behind its image.
            hipFlash.transform.localPosition = new Vector3(p.hipMuzzleFlashOffset.x, p.hipMuzzleFlashOffset.y, 0.01f);
            hipFlash.transform.localScale = p.hipMuzzleFlashAlongBarrel
                ? new Vector3(p.hipMuzzleFlashScale * p.hipMuzzleFlashStretch.x, p.hipMuzzleFlashScale * p.hipMuzzleFlashStretch.y, 1f)
                : Vector3.one * p.hipMuzzleFlashScale;
            if (p.hipMuzzleFlashAlongBarrel)
                hipFlash.transform.localRotation = Quaternion.Euler(0f, 0f, p.hipMuzzleFlashBarrelDegrees);
            // A front sight hides the flash's middle: it is held just under the sight tip. An optical sight's glass is clear,
            // so its flash stays at the muzzle under the glass (hidden by the housing) and the aiming dot is never covered.
            adsFlash.transform.localPosition = new Vector3(p.adsMuzzleOffset.x,
                p.adsMuzzleFlashUnderSight ? p.adsMuzzleOffset.y : -Mathf.Min(-p.adsMuzzleOffset.y, WeaponViewModel.AdsFlashMaxBelowSight), 0.01f);
            adsFlash.transform.localScale = Vector3.one * (p.adsMuzzleFlashScale * WeaponViewModel.AdsFlashBehindScale);
            // Every gun, hip and ADS: the gun (and its hands) in front, the flash behind it, the world behind both. The
            // flash shows only beyond the gun's outline: out of the muzzle, never painted over the gun.
            adsFlash.sortingOrder = ads.sortingOrder - 1;
            hipFlash.sortingOrder = hip.sortingOrder - 1;
            glow.sortingOrder = hip.sortingOrder + 1;
            emission.sortingOrder = hip.sortingOrder + 2;
            adsGlow.sortingOrder = ads.sortingOrder + 1;
            adsEmission.sortingOrder = ads.sortingOrder + 2;
        }

        /// <summary>A shot: the kick starts again and the flash shows for the profile's flash time.</summary>
        public void Fire(float time)
        {
            kick = 1f;
            if (profile == null)
                return;
            flashUntil = time + profile.muzzleFlashTime;
            float angle = Random.Range(0f, 360f);
            // A jet along the barrel keeps its direction (a little jitter per shot); a star is turned at random.
            hipFlash.transform.localRotation = Quaternion.Euler(0f, 0f, profile.hipMuzzleFlashAlongBarrel
                ? profile.hipMuzzleFlashBarrelDegrees + Random.Range(-FlashJitterDegrees, FlashJitterDegrees) : angle);
            adsFlash.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>Hides everything (gun put away, no art, not playing).</summary>
        public void Hide()
        {
            hip.enabled = ads.enabled = hipFlash.enabled = adsFlash.enabled = false;
            emission.enabled = glow.enabled = false;
            adsEmission.enabled = adsGlow.enabled = false;
        }

        /// <summary>
        /// Places and fades the sprites for this frame. The same numbers as the offline view model has always used: the
        /// hip image anchored by its profile (right-edge follow, <see cref="WeaponViewModel.HipShiftLeft"/>,
        /// <see cref="WeaponViewModel.HipShiftDownAt"/>), the ADS aim point (front-sight tip or optical dot) on the screen centre, both turning
        /// around their hold point.
        /// </summary>
        public void Update(in Input i)
        {
            WeaponViewProfile p = profile;
            if (i.Presence <= 0.001f || p == null)
            {
                Hide();
                return;
            }
            float t = i.Ads;
            Vector2 holster = new Vector2(0.05f, -0.45f) * (1f - Smooth(i.Presence));

            // Keep the hip weapon in the lower right on narrower (iPad 4:3) and wider (tall-phone) screens.
            float aspect = i.Aspect;
            float follow = aspect < p.referenceAspect ? p.hipRightEdgeFollowNarrow : p.hipRightEdgeFollow;
            float edgeShift = (aspect - p.referenceAspect) * HalfHeight * follow;
            float narrow = Mathf.InverseLerp(p.referenceAspect, 4f / 3f, aspect);
            float hipScale = Mathf.Lerp(1f, p.hipScaleAt4x3, narrow);

            // Fire: pushed down/back toward the player, then springs back.
            kick = Mathf.MoveTowards(kick, 0f, p.kickRecoverSpeed * i.DeltaTime);
            float kickWeight = kick * kick * Mathf.Lerp(1f, p.adsKickMultiplier, t);
            Vector2 kickOffset = p.kickOffset * kickWeight;

            // Reload: lowered and rolled over the real reload time.
            float reloadCurve = i.Reloading ? Mathf.Sin(Mathf.Clamp01(i.ReloadProgress) * Mathf.PI) : 0f;
            Vector2 reloadOffset = new Vector2(0.02f, -p.reloadDrop) * reloadCurve;
            Quaternion rotation = Quaternion.Euler(0f, 0f, p.reloadRollDegrees * reloadCurve + p.kickRollDegrees * kickWeight);
            float scale = 1f + p.kickScale * kickWeight;

            Vector2 shared = i.Bob + kickOffset + reloadOffset + holster;

            Vector2 hipRest = p.hipMuzzlePosition + new Vector2(edgeShift - WeaponViewModel.HipShiftLeft,
                -(WeaponViewModel.HipShiftDownAt(aspect) + WeaponViewModel.HipShiftDownForComparison) * 2f * HalfHeight);
            Vector2 hipTarget = p.hipToAdsRelative ? hipRest + p.hipToAdsMuzzlePosition : p.hipToAdsMuzzlePosition;
            // Without an ADS image the hip image stays where it is held (it is the picture while aiming too).
            Vector2 hipPlace = Vector2.Lerp(hipRest, hipTarget, ads.sprite == null && hip.sprite != null ? 0f : t) + shared;
            Place(hip.transform, hipPlace, p.hipHoldOffset * hipScale, rotation * Quaternion.Euler(0f, 0f, p.hipRollDegrees), hipScale * scale);

            Vector2 adsPlace = Vector2.Lerp(p.adsStartOffset, Vector2.zero, t) + shared;
            Place(ads.transform, adsPlace, p.adsHoldOffset, rotation, scale);

            float adsAlpha = Mathf.InverseLerp(p.crossfadeStart, p.crossfadeEnd, t);
            // A gun whose ADS image is not delivered keeps its hip image while aiming (it never vanishes, and no other
            // gun's image stands in).
            if (ads.sprite == null && hip.sprite != null)
                adsAlpha = 0f;
            AdsAlpha = adsAlpha;
            SetAlpha(hip, 1f - adsAlpha);
            SetAlpha(ads, adsAlpha);
            bool hipShown = hip.enabled && hip.sprite != null;
            SetLayer(emission, hipShown, (1f - adsAlpha) * p.emissionIntensity * EmissionPulse(p, i.Time));
            SetLayer(glow, hipShown && GlowEnabled, (1f - adsAlpha) * p.glowIntensity * GlowPulse(p, i.Time));
            bool adsShown = ads.enabled && ads.sprite != null;
            SetLayer(adsEmission, adsShown, adsAlpha * p.adsEmissionIntensity * AdsEmissionPulse(p, i.Time));
            SetLayer(adsGlow, adsShown && GlowEnabled, adsAlpha * p.adsGlowIntensity * AdsGlowPulse(p, i.Time));

            bool flash = i.Time < flashUntil;
            hipFlash.enabled = flash && adsAlpha < 0.5f && hip.sprite != null;
            adsFlash.enabled = flash && adsAlpha >= 0.5f && ads.sprite != null;
        }

        /// <summary>Positions a sprite so that it turns around its hold point (hands) instead of its pivot (muzzle / sight).</summary>
        public static void Place(Transform sprite, Vector2 pivotPosition, Vector2 holdOffset, Quaternion rotation, float scale)
        {
            Vector2 rotatedHold = rotation * holdOffset;
            Vector2 position = pivotPosition + holdOffset - rotatedHold;
            sprite.localPosition = new Vector3(position.x, position.y, SpriteDepth);
            sprite.localRotation = rotation;
            sprite.localScale = Vector3.one * scale;
        }

        public static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>The glow's share of its brightness at a time: 1, dipping by the profile's pulse once per pulse length.</summary>
        public static float GlowPulse(WeaponViewProfile p, float time) => Breath(p.glowPulse, p.glowPulseSeconds, time);

        /// <summary>The emission core's share of its brightness at a time: breathes with the glow, by its own depth.</summary>
        public static float EmissionPulse(WeaponViewProfile p, float time) => Breath(p.emissionPulse, p.glowPulseSeconds, time);

        /// <summary>The ADS glow's share of its brightness at a time (its own depth and length, not the hip's).</summary>
        public static float AdsGlowPulse(WeaponViewProfile p, float time) => Breath(p.adsGlowPulse, p.adsGlowPulseSeconds, time);

        /// <summary>The ADS emission core's share of its brightness at a time: breathes with the ADS glow, by its own depth.</summary>
        public static float AdsEmissionPulse(WeaponViewProfile p, float time) => Breath(p.adsEmissionPulse, p.adsGlowPulseSeconds, time);

        /// <summary>1 at the top of each period, easing down to 1 - depth half a period later and back (no steps).</summary>
        public static float Breath(float depth, float seconds, float time)
        {
            if (depth <= 0f || seconds <= 0f)
                return 1f;
            return 1f - depth * 0.5f * (1f - Mathf.Cos(time * 2f * Mathf.PI / seconds));
        }

        static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            renderer.enabled = alpha > 0.001f;
            Color c = renderer.color;
            c.a = alpha;
            renderer.color = c;
        }

        static void SetLayer(SpriteRenderer renderer, bool on, float alpha)
        {
            if (!on || renderer.sprite == null || alpha <= 0.001f)
            {
                renderer.enabled = false;
                return;
            }
            renderer.enabled = true;
            Color c = renderer.color;
            c.a = Mathf.Clamp01(alpha);
            renderer.color = c;
        }

        /// <summary>
        /// How far a weapon is raised during a switch that started <paramref name="elapsed"/> seconds ago (the offline
        /// <see cref="Deadlock.Combat.WeaponSwitcher.Presence"/> rule): the old one goes down over the first half, the new
        /// one comes up over the second.
        /// </summary>
        public static float SwitchPresence(float elapsed, float switchTime, bool isNew)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, switchTime));
            return isNew ? Mathf.Clamp01(t * 2f - 1f) : Mathf.Clamp01(1f - t * 2f);
        }
    }
}
