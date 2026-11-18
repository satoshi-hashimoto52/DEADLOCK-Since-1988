using System.Collections;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.UI;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 32 in a running scene: keys 1 / 2 / 3 (main gun, knife, sub gun), the sub-gun touch button next to the
    /// unchanged weapon button, each gun keeping its rounds, firing / ADS / reload after the switches, the button's
    /// place saved and restored by the layout, and the loadout strip of the HUD (slots, rounds, highlight, stand-ins
    /// marked "（仮）") laid out on an iPhone and an iPad screen. The sub gun is a runtime copy of the rifle turned into
    /// a handgun without art (as the provisional handgun is); the provisional assets themselves are checked in EditMode.
    /// </summary>
    public class LoadoutSwitchHudTests : FiringRangeTestBase
    {
        readonly List<Object> clones = new List<Object>();

        public override void TearDown()
        {
            foreach (var c in clones)
                if (c != null)
                    Object.Destroy(c);
            clones.Clear();
            base.TearDown();
        }

        IEnumerator LoadTraining(InputMode mode)
        {
            yield return LoadScene(mode, GameScenes.Training);
            foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
                z.Active = false;
        }

        WeaponDefinition Handgun()
        {
            var d = Object.Instantiate(gun.Definition);
            clones.Add(d);
            d.weaponId = "test_handgun";
            d.displayName = "HANDGUN (PROVISIONAL)";
            d.category = WeaponCategory.Handgun;
            d.automatic = false;
            d.magazineSize = 12;
            d.startReserve = 24;
            d.reserveMax = 36;
            d.viewProfile = null; // no art yet
            return d;
        }

        IEnumerator WaitSwitch() => Wait(weapons.SwitchTime + 0.15f);

        [UnityTest]
        public IEnumerator Keys123_MainKnifeSub_RoundsKept_FireAdsReloadAfterSwitching()
        {
            yield return LoadTraining(InputMode.Desktop);
            var rifle = gun.Definition;
            var view = Object.FindAnyObjectByType<WeaponViewModel>();

            // No sub gun: key 3 is refused.
            yield return Tap(keyboard.digit3Key);
            yield return null;
            Assert.IsFalse(weapons.IsSwitching, "no sub gun: nothing happens");
            Assert.AreSame(rifle, gun.Definition);
            Assert.IsFalse(weapons.RequestGun(LoadoutSlot.Secondary));

            Assert.IsTrue(gun.EquipGun(LoadoutSlot.Secondary, Handgun()));
            var handgun = gun.Inventory.Gun(LoadoutSlot.Secondary);

            // Rifle: a few shots, a reload started.
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.35f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            int rifleLoaded = gun.Ammo.Loaded;
            Assert.Less(rifleLoaded, 30);
            Assert.IsTrue(gun.TryStartReload());

            // 3: the sub gun. Lowered, swapped at the midpoint, raised; the reload is canceled without moving rounds.
            yield return Tap(keyboard.digit3Key);
            Assert.IsTrue(weapons.IsSwappingGun);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current, "the gun stays out (no knife on the way)");
            Assert.IsFalse(gun.IsEquipped, "no shot during the switch");
            Assert.IsFalse(gun.IsReloading, "reload canceled");
            yield return WaitSwitch();
            Assert.IsFalse(weapons.IsSwitching);
            Assert.AreSame(handgun, gun.Definition);
            Assert.AreEqual(LoadoutSlot.Secondary, gun.ActiveSlot);
            Assert.AreEqual(12, gun.Ammo.Loaded);
            Assert.AreEqual(24, gun.Ammo.Reserve);
            Assert.AreEqual(rifleLoaded, gun.Inventory.Ammo(LoadoutSlot.Primary).Loaded, "the rifle's rounds stay as they were");
            Assert.AreEqual(60, gun.Inventory.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.IsFalse(view.TryGetMuzzleScreenPoint(out _), "no art: no weapon image (never another gun's)");

            // The handgun fires (semi-automatic) and reloads from its own reserve.
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(11, gun.Ammo.Loaded);
            Assert.IsTrue(gun.TryStartReload());
            yield return Wait(gun.Definition.reloadTime + 0.1f);
            Assert.AreEqual(12, gun.Ammo.Loaded);
            Assert.AreEqual(23, gun.Ammo.Reserve);

            // 1: the main gun again, with its rounds; ADS and reload work.
            yield return Tap(keyboard.digit1Key);
            yield return WaitSwitch();
            Assert.AreSame(rifle, gun.Definition);
            Assert.AreEqual(rifleLoaded, gun.Ammo.Loaded);
            Assert.IsTrue(view.TryGetMuzzleScreenPoint(out _), "the rifle's images are back");
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(gun.Definition.adsTime + 0.15f);
            Assert.IsTrue(gun.AdsRequested);
            Assert.AreEqual(1f, gun.AdsBlend, 0.001f);
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(gun.Definition.adsTime + 0.1f);
            Assert.IsTrue(gun.TryStartReload());
            yield return Wait(gun.Definition.reloadTime + 0.1f);
            Assert.AreEqual(30, gun.Ammo.Loaded);
            Assert.AreEqual(60 - (30 - rifleLoaded), gun.Ammo.Reserve);

            // 1 again: already out, nothing happens.
            yield return Tap(keyboard.digit1Key);
            Assert.IsFalse(weapons.IsSwitching);

            // 2: the knife (unchanged); 3 from the knife: the sub gun comes out; 2, then 1: the main gun.
            yield return Tap(keyboard.digit2Key);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            yield return Tap(keyboard.digit3Key);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.AreSame(handgun, gun.Definition);
            Assert.AreEqual(12, gun.Ammo.Loaded);
            yield return Tap(keyboard.digit2Key);
            yield return WaitSwitch();
            yield return Tap(keyboard.digit1Key);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.AreSame(rifle, gun.Definition);

            // The rifle still fires.
            int before = gun.Ammo.Loaded;
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(before - 1, gun.Ammo.Loaded);
        }

        [UnityTest]
        public IEnumerator Touch_SubGunButton_GreyedWithoutSubGun_SwapsMainAndSub_WeaponButtonUnchanged()
        {
            yield return LoadTraining(InputMode.Touch);
            var touchscreen = InputSystem.AddDevice<Touchscreen>();
            yield return null;
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var weaponButton = GameObject.Find("WeaponButton").GetComponent<TouchButton>();
            var subButton = GameObject.Find(TouchControls.SubGunButtonName).GetComponent<TouchButton>();
            Assert.AreSame(controls.SubGunButton, subButton);
            Assert.AreNotSame(weaponButton, subButton);
            Assert.AreEqual(TouchControls.SubGunLabel, subButton.transform.Find("Label").GetComponent<UnityEngine.UI.Text>().text);
            Assert.IsFalse(subButton.Icon.enabled, "no approved icon: a text label, not the swap icon");
            var rifle = gun.Definition;
            int id = 1;
            IEnumerator TapButton(TouchButton b)
            {
                Vector2 p = ScreenCenter((RectTransform)b.transform);
                BeginTouch(id, p, queueEventOnly: true, screen: touchscreen);
                yield return null;
                EndTouch(id, p, queueEventOnly: true, screen: touchscreen);
                yield return null;
                id++;
            }

            Assert.IsFalse(subButton.Interactable, "no sub gun: greyed out");
            yield return TapButton(subButton);
            yield return null;
            Assert.IsFalse(weapons.IsSwitching, "refused");
            Assert.AreSame(rifle, gun.Definition);

            Assert.IsTrue(gun.EquipGun(LoadoutSlot.Secondary, Handgun()));
            yield return null;
            Assert.IsTrue(subButton.Interactable);
            yield return TapButton(subButton);
            yield return WaitSwitch();
            Assert.AreEqual(LoadoutSlot.Secondary, gun.ActiveSlot);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);

            // The weapon button: knife and back to the gun last held (the sub gun), as before.
            yield return TapButton(weaponButton);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            yield return TapButton(weaponButton);
            yield return WaitSwitch();
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.AreEqual(LoadoutSlot.Secondary, gun.ActiveSlot);

            // Sub-gun button again: the main gun.
            yield return TapButton(subButton);
            yield return WaitSwitch();
            Assert.AreSame(rifle, gun.Definition);
            Assert.AreEqual(30, gun.Ammo.Loaded);
        }

        [UnityTest]
        public IEnumerator Touch_SubGunButton_WithoutALocalGun_FollowsTheGivenLoadout()
        {
            // The online client has no local gun (Stage 37F): its view says whether the server's loadout has a sub gun.
            yield return LoadTraining(InputMode.Touch);
            var controls = Object.FindAnyObjectByType<TouchControls>();
            var subButton = controls.SubGunButton;
            yield return null;
            Assert.IsFalse(subButton.Interactable, "offline, no sub gun");
            bool hasSub = true;
            controls.SubGunAvailable = () => hasSub;
            yield return null;
            Assert.IsTrue(subButton.Interactable, "the given loadout has a sub gun");
            hasSub = false;
            yield return null;
            Assert.IsFalse(subButton.Interactable);
            controls.SubGunAvailable = null;
            Assert.IsTrue(gun.EquipGun(LoadoutSlot.Secondary, Handgun()));
            yield return null;
            Assert.IsTrue(subButton.Interactable, "null: the local gun decides again");
        }

        [UnityTest]
        public IEnumerator SubGunButton_MovedInTheLayoutEditor_SavedAndRestored()
        {
            yield return LoadScene(InputMode.Touch);
            var menu = Object.FindAnyObjectByType<GameMenu>();
            var editor = menu.GetComponent<TouchLayoutEditor>();
            var applier = Object.FindAnyObjectByType<TouchLayoutApplier>();
            Assert.IsTrue(applier.HasWidget(TouchLayout.SubGun));
            Assert.AreEqual(TouchLayout.Defaults().Get(TouchLayout.SubGun).center, applier.Widget(TouchLayout.SubGun).anchorMin);
            menu.OpenLayoutEditor();
            yield return null;
            Assert.IsTrue(editor.MoveBy(TouchLayout.SubGun, new Vector2(-40f, 30f)), editor.Message);
            Assert.IsTrue(editor.Resize(TouchLayout.SubGun, +1));
            yield return null;
            Vector2 moved = applier.Widget(TouchLayout.SubGun).anchorMin;
            Assert.AreNotEqual(TouchLayout.Defaults().Get(TouchLayout.SubGun).center, moved);
            editor.ApplyAndClose();
            menu.Close();
            yield return null;
            StringAssert.Contains(TouchLayout.SubGun, PlayerPrefs.GetString(TouchLayout.PrefsKey, ""));

            yield return LoadScene(InputMode.Touch);
            applier = Object.FindAnyObjectByType<TouchLayoutApplier>();
            yield return null;
            Assert.AreEqual(moved.x, applier.Widget(TouchLayout.SubGun).anchorMin.x, 0.001f, "restored on the next start");
            Assert.AreEqual(moved.y, applier.Widget(TouchLayout.SubGun).anchorMin.y, 0.001f);
            Assert.AreEqual(TouchLayout.BaseDiameter(TouchLayout.SubGun) * 1.1f, applier.Widget(TouchLayout.SubGun).sizeDelta.x, 0.5f);

            // Back to the defaults (applied): the button's default place again.
            menu = Object.FindAnyObjectByType<GameMenu>();
            editor = menu.GetComponent<TouchLayoutEditor>();
            menu.OpenLayoutEditor();
            yield return null;
            editor.ResetToDefaults();
            editor.ApplyAndClose();
            menu.Close();
            yield return null;
            Assert.AreEqual(TouchLayout.Defaults().Get(TouchLayout.SubGun).center, applier.Widget(TouchLayout.SubGun).anchorMin);
        }

        [UnityTest]
        public IEnumerator Hud_LoadoutStrip_SlotsRoundsHighlight_StandInMarked()
        {
            yield return LoadTraining(InputMode.Desktop);
            var hud = Object.FindAnyObjectByType<HudController>();
            yield return null;
            Assert.IsNotNull(hud.SlotStrip);
            Assert.IsTrue(hud.SlotStripBelow, "the default panel is at the top: the strip hangs under it");
            Assert.AreEqual(hud.AmmoPanel, hud.SlotStrip.parent, "moves, scales and fades with the ammo panel");

            // Rifle in hand, no sub gun, the knife.
            Assert.IsTrue(hud.SlotSelected(LoadoutSlot.Primary));
            Assert.IsNotNull(hud.SlotArt(LoadoutSlot.Primary), "the rifle's approved side view");
            Assert.AreEqual("", hud.SlotCaption(LoadoutSlot.Primary).text);
            Assert.AreEqual("30/60", hud.SlotAmmo(LoadoutSlot.Primary).text);
            Assert.IsTrue(hud.SlotEmpty(LoadoutSlot.Secondary));
            Assert.AreEqual(HudController.EmptySlotLabel, hud.SlotCaption(LoadoutSlot.Secondary).text);
            Assert.AreEqual("", hud.SlotAmmo(LoadoutSlot.Secondary).text);
            Assert.IsFalse(hud.SlotSelected(LoadoutSlot.Secondary));
            Assert.AreEqual(melee.Definition.displayName, hud.SlotCaption(LoadoutSlot.Knife).text);
            Assert.AreEqual("—", hud.SlotAmmo(LoadoutSlot.Knife).text);

            Assert.IsTrue(gun.EquipGun(LoadoutSlot.Secondary, Handgun()));
            yield return null;
            Assert.IsFalse(hud.SlotEmpty(LoadoutSlot.Secondary));
            Assert.IsNull(hud.SlotArt(LoadoutSlot.Secondary), "no approved side view");
            Assert.AreEqual("HANDGUN" + WeaponArtStatus.PlaceholderMark, hud.SlotCaption(LoadoutSlot.Secondary).text, "a stand-in, marked");
            Assert.AreEqual("12/24", hud.SlotAmmo(LoadoutSlot.Secondary).text);

            // Shots: only the gun in hand counts down; switching moves the highlight.
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual("29/60", hud.SlotAmmo(LoadoutSlot.Primary).text);
            Assert.AreEqual("12/24", hud.SlotAmmo(LoadoutSlot.Secondary).text);
            Assert.IsTrue(weapons.RequestGun(LoadoutSlot.Secondary));
            yield return WaitSwitch();
            Assert.IsTrue(hud.SlotSelected(LoadoutSlot.Secondary));
            Assert.IsFalse(hud.SlotSelected(LoadoutSlot.Primary));
            Assert.AreEqual("12 / 24", hud.AmmoText.text, "the panel's count follows the gun in hand");
            Assert.IsTrue(weapons.Request(WeaponSlot.Melee));
            yield return WaitSwitch();
            Assert.IsTrue(hud.SlotSelected(LoadoutSlot.Knife));
            Assert.AreEqual("29/60", hud.SlotAmmo(LoadoutSlot.Primary).text);

            // The panel moved to the lower half: the strip goes over it.
            var applier = Object.FindAnyObjectByType<TouchLayoutApplier>(FindObjectsInactive.Include);
            var layout = applier.Shown;
            var ammo = layout.Get(TouchLayout.Ammo);
            ammo.center = new Vector2(0.85f, 0.3f);
            layout.Set(ammo);
            applier.Apply(layout);
            yield return null;
            Assert.IsFalse(hud.SlotStripBelow);
            Assert.Greater(hud.SlotStrip.position.y, hud.AmmoPanel.position.y);
        }

        /// <summary>
        /// The strip and the sub-gun button on an iPhone 16 Pro (2622 × 1206, notch sides and the home indicator off the
        /// Safe Area) and an iPad (2048 × 1536, 4:3), in the ruins (with the floor map): inside the Safe Area, clear of
        /// the menu button, of every button, of the floor map; the sub-gun button clear of the other buttons (measured,
        /// not looked at).
        /// </summary>
        [UnityTest]
        public IEnumerator Hud_StripAndSubGunButton_IPhoneAndIPad_ClearOfMapMenuAndButtons()
        {
            yield return LoadScene(InputMode.Touch, GameScenes.Ruins);
            var hud = Object.FindAnyObjectByType<HudController>();
            var map = Object.FindAnyObjectByType<MiniMapView>();
            var menu = Object.FindAnyObjectByType<GameMenu>();
            var applier = Object.FindAnyObjectByType<TouchLayoutApplier>();
            Assert.IsNotNull(map, "the ruins have a floor map");
            var canvas = hud.AmmoPanel.GetComponentInParent<Canvas>().rootCanvas;
            var mode = canvas.renderMode;
            var camGo = new GameObject("AspectCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 0;
            var fitters = Object.FindObjectsByType<SafeAreaFitter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Rect Pixels(RectTransform r)
            {
                var w = new Vector3[4];
                r.GetWorldCorners(w);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, w[0]), b = RectTransformUtility.WorldToScreenPoint(cam, w[2]);
                return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            try
            {
                foreach (var (screen, sides, bottom) in new[] { (new Vector2Int(2622, 1206), 186f, 63f), (new Vector2Int(2048, 1536), 0f, 0f) })
                {
                    var rt = new RenderTexture(screen.x, screen.y, 16);
                    cam.targetTexture = rt;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    foreach (var fitter in fitters)
                    {
                        fitter.enabled = false;
                        var r = (RectTransform)fitter.transform;
                        r.anchorMin = new Vector2(sides / screen.x, bottom / screen.y);
                        r.anchorMax = new Vector2(1f - sides / screen.x, 1f);
                        r.offsetMin = r.offsetMax = Vector2.zero;
                    }
                    for (int i = 0; i < 4; i++)
                        yield return null;
                    Canvas.ForceUpdateCanvases();
                    string what = $"{screen.x}x{screen.y}";

                    Rect safe = Pixels(applier.SafeArea);
                    Rect strip = Pixels(hud.SlotStrip);
                    Rect panel = Pixels(hud.AmmoPanel);
                    float unit = safe.height / applier.SafeSize.y; // pixels per canvas unit
                    Assert.IsTrue(safe.Contains(strip.min) && safe.Contains(strip.max), $"{what}: strip {strip} inside the Safe Area {safe}");
                    Assert.Less(strip.yMax, panel.yMin + 0.5f, $"{what}: under the panel");
                    Assert.AreEqual(panel.width, strip.width, 1f, $"{what}: as wide as the panel");
                    var menuRect = Pixels((RectTransform)menu.MenuButton.transform);
                    float margin = TouchLayout.MenuGuardMargin * unit;
                    var guard = Rect.MinMaxRect(menuRect.xMin - margin, menuRect.yMin - margin, menuRect.xMax + margin, menuRect.yMax + margin);
                    Assert.IsFalse(strip.Overlaps(guard), $"{what}: clear of the menu button");
                    Assert.IsFalse(strip.Overlaps(Pixels(map.Frame)), $"{what}: clear of the floor map {Pixels(map.Frame)}");
                    foreach (string id in TouchLayout.Circles)
                    {
                        Rect c = Pixels(applier.Widget(id));
                        Vector2 nearest = new Vector2(Mathf.Clamp(c.center.x, strip.xMin, strip.xMax), Mathf.Clamp(c.center.y, strip.yMin, strip.yMax));
                        Assert.Greater(Vector2.Distance(nearest, c.center), c.width * 0.5f, $"{what}: strip clear of {id}");
                    }
                    Rect sub = Pixels(applier.Widget(TouchLayout.SubGun));
                    Assert.IsTrue(Pixels(applier.Widget(TouchLayout.Fire)).width > 1f);
                    Assert.IsFalse(sub.Overlaps(Pixels(map.Frame)), $"{what}: sub-gun button clear of the floor map");
                    Assert.IsFalse(sub.Overlaps(guard), $"{what}: sub-gun button clear of the menu");
                    foreach (string id in TouchLayout.Circles)
                    {
                        if (id == TouchLayout.SubGun)
                            continue;
                        Rect c = Pixels(applier.Widget(id));
                        Assert.GreaterOrEqual(Vector2.Distance(c.center, sub.center), (c.width + sub.width) * 0.5f * (1f - TouchLayout.AllowedOverlap),
                            $"{what}: sub-gun button clear of {id}");
                    }
                    Assert.IsTrue(new Rect(0f, 0f, screen.x, screen.y).Contains(sub.min) && new Rect(0f, 0f, screen.x, screen.y).Contains(sub.max), $"{what}: on screen");

                    cam.targetTexture = null;
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = null;
                Object.Destroy(camGo);
            }
        }
    }
}
