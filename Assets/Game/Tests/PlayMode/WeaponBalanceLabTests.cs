#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Deadlock.Combat;
using Deadlock.Dev;
using Deadlock.Input;
using Deadlock.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// Weapon balance lab in the firing range, driven by the mouse at the controls' screen positions (EventSystem, as a
    /// finger would): opened from the menu, the weapon table, recoil and falloff editing, slider, graph, test session on a
    /// clone (recoil direction / alternation / strength while firing full auto, reload, weapon switch), end of session,
    /// save / load / reset, input blocked while open and nothing left pressed after closing. Weapon assets stay as they were.
    /// </summary>
    public class WeaponBalanceLabTests : FiringRangeTestBase
    {
        string folder;
        WeaponBalanceLab lab;
        GameMenu menu;

        public override void Setup()
        {
            base.Setup();
            folder = Path.Combine(Path.GetTempPath(), "DeadlockWeaponLabPlay_" + System.Guid.NewGuid().ToString("N"));
        }

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            base.TearDown();
        }

        IEnumerator Click(Vector2 screen)
        {
            Move(mouse.position, screen);
            yield return null;
            Press(mouse.leftButton);
            yield return null;
            Release(mouse.leftButton);
            yield return null;
        }

        IEnumerator Click(string control)
        {
            var b = lab.ControlButton(control);
            Assert.IsNotNull(b, control);
            yield return Click(ScreenCenter((RectTransform)b.transform));
        }

        static GameObject TopUnder(Vector2 screen)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        IEnumerator OpenLab()
        {
            yield return LoadScene(InputMode.Desktop);
            lab = WeaponBalanceLab.Create(gun, new WeaponBalanceStore(folder));
            menu = Object.FindAnyObjectByType<GameMenu>();
            yield return null;
            menu.Open();
            yield return null;
            Assert.IsNotNull(lab.OpenButton, "the menu offers the lab");
            var logo = menu.transform.Find("MenuLayer/MenuPanel/Window/TitleLogo") as RectTransform;
            if (logo != null)
            {
                var a = new Vector3[4];
                var b = new Vector3[4];
                logo.GetWorldCorners(a);
                ((RectTransform)lab.OpenButton.transform).GetWorldCorners(b);
                Assert.IsFalse(Rect.MinMaxRect(a[0].x, a[0].y, a[2].x, a[2].y).Overlaps(Rect.MinMaxRect(b[0].x, b[0].y, b[2].x, b[2].y)), "clear of the title logo");
            }
            Assert.AreEqual(lab.OpenButton.gameObject, TopUnder(ScreenCenter((RectTransform)lab.OpenButton.transform)).GetComponentInParent<Button>().gameObject);
            yield return Click(ScreenCenter((RectTransform)lab.OpenButton.transform));
            Assert.IsTrue(lab.IsOpen);
            Assert.IsFalse(menu.IsOpen, "the menu closes for the lab");
            Assert.IsTrue(router.IsBlocked, "the player's input is blocked while the lab is open");
        }

        [UnityTest]
        public IEnumerator Lab_EditRecoilAndFalloff_ApplyToTest_FireReloadSwitch_EndTest_AssetsUntouched()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var rifle = gun.Definition;
            var smg = player.GetComponent<DevGunSelection>().Options[1];
            float smgDamage = smg.damage, smgInterval = smg.fireInterval;
            int smgMag = smg.magazineSize;

            // Inside the safe area.
            var c = new Vector3[4];
            lab.Window.GetWorldCorners(c);
            Assert.IsTrue(Screen.safeArea.Contains(c[0]) && Screen.safeArea.Contains(c[2]), "lab window inside the safe area");

            // Table: weapons are rows; a click on the SMG row selects it.
            yield return Click("Cell_proto_smg_0");
            Assert.AreEqual("proto_smg", lab.SelectedId);

            // Performance: +1 damage, +10 RPM.
            yield return Click("Plus_damage");
            yield return Click("Plus_rpm");
            Assert.AreEqual(smgDamage + 1f, lab.Edited("proto_smg").damage);
            Assert.AreEqual(60f / smgInterval + 10f, lab.Edited("proto_smg").rpm, 1e-3f);
            Assert.AreEqual(smgDamage, lab.Current("proto_smg").damage, "the current values stay for comparison");

            // Recoil: axis → Mixed (3 steps), sideways → alternating (3 steps), strength → strong (1 step).
            yield return Click("Tab_Recoil");
            Assert.AreEqual(WeaponBalanceLab.Tab.Recoil, lab.CurrentTab);
            for (int i = 0; i < 3; i++)
                yield return Click("Cycle_axis");
            for (int i = 0; i < 3; i++)
                yield return Click("Cycle_horizontal");
            yield return Click("Cycle_strength");
            var r = lab.Edited("proto_smg").recoil;
            Assert.IsTrue(r.enabled);
            Assert.AreEqual(RecoilAxis.Mixed, r.axis);
            Assert.AreEqual(RecoilVertical.Up, r.vertical);
            Assert.AreEqual(RecoilHorizontal.Alternating, r.horizontal);
            Assert.AreEqual(RecoilStrength.Strong, r.strength);
            Assert.IsTrue(r.IsValid(out _));
            // Slider: a click near the right end sets a large base angle.
            var slider = (RectTransform)lab.PitchSlider.transform;
            var sc = new Vector3[4];
            slider.GetWorldCorners(sc);
            yield return Click(new Vector2(Mathf.Lerp(sc[0].x, sc[2].x, 0.9f), (sc[0].y + sc[2].y) * 0.5f));
            Assert.Greater(lab.Edited("proto_smg").recoil.pitchDegrees, 2f, "slider moved the base angle");
            yield return Click("Plus_pitch");
            yield return Click("Minus_pitch");

            // Falloff: the example, select point 4, move it, add / remove, an invalid order is refused.
            yield return Click("Tab_Falloff");
            yield return Click("FalloffExample");
            Assert.AreEqual(6, lab.Edited("proto_smg").falloff.points.Count);
            Assert.Greater(lab.GraphSegmentCount, 3, "graph drawn from the data");
            yield return Click("Point_3");
            Assert.AreEqual(3, lab.SelectedPoint);
            yield return Click("DistPlus");
            yield return Click("MulMinus");
            Assert.AreEqual(new FalloffPoint(45f, 0.75f), lab.Edited("proto_smg").falloff.points[3]);
            yield return Click("PointAdd");
            Assert.AreEqual(7, lab.Edited("proto_smg").falloff.points.Count);
            yield return Click("PointRemove");
            Assert.AreEqual(6, lab.Edited("proto_smg").falloff.points.Count);
            var ms = (RectTransform)lab.MultiplierSlider.transform;
            ms.GetWorldCorners(sc);
            yield return Click("Point_5");
            yield return Click(new Vector2(Mathf.Lerp(sc[0].x, sc[2].x, 0.1f), (sc[0].y + sc[2].y) * 0.5f));
            Assert.Less(lab.Edited("proto_smg").falloff.points[5].multiplier, 0.3f, "multiplier slider");
            yield return Click("Point_1");
            for (int i = 0; i < 4; i++)
                yield return Click("DistPlus"); // 10 m → 30 m: past the next point (25 m)
            Assert.IsFalse(lab.Edited("proto_smg").falloff.IsValid(out _));
            yield return Click("Tab_Test");
            yield return Click("Apply");
            Assert.IsFalse(lab.Testing, "an invalid falloff is not applied");
            StringAssert.Contains("適用できません", lab.Message.text);
            yield return Click("Tab_Falloff");
            yield return Click("FalloffExample");

            // Apply: a clone with the edited values is equipped; the assets keep theirs.
            yield return Click("Tab_Test");
            yield return Click("Apply");
            Assert.IsTrue(lab.Testing);
            Assert.AreEqual("proto_smg", gun.Definition.weaponId);
            Assert.AreNotSame(smg, gun.Definition);
            Assert.AreEqual(smgDamage + 1f, gun.Definition.damage);
            Assert.AreEqual(smgDamage, smg.damage, "the SMG asset is not written");
            Assert.AreEqual(smgInterval, smg.fireInterval);
            Assert.IsFalse(smg.recoilProfile.enabled);
            Assert.IsTrue(smg.damageFalloff.IsEmpty);

            // Close: input back, nothing pressed.
            yield return Click("Close");
            Assert.IsFalse(lab.IsOpen);
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(router.Current.FireHeld, "the click on 閉じる does not become a shot");
            Assert.AreEqual(smgMag, gun.Ammo.Loaded, "no shot fired by the lab's clicks");

            // Full auto with the test recoil: up every shot, sideways alternating, strong.
            var kicks = new List<(int index, Vector2 kick)>();
            gun.Fired += () => kicks.Add((gun.ShotIndex - 1, gun.LastRecoil));
            Press(mouse.leftButton);
            yield return Wait(0.6f);
            Release(mouse.leftButton);
            yield return null;
            Assert.GreaterOrEqual(kicks.Count, 4, "full auto keeps firing");
            Assert.AreEqual(smgMag - kicks.Count, gun.Ammo.Loaded, "one round per shot");
            var profile = gun.Definition.recoilProfile;
            foreach (var (index, kick) in kicks)
            {
                Assert.IsTrue(RecoilCalculator.TryCompute(profile, index, gun.AdsBlend, out float p, out float y));
                Assert.AreEqual(p, kick.x, 1e-4f);
                Assert.AreEqual(y, kick.y, 1e-4f);
                Assert.Greater(kick.x, 0f, "up");
                Assert.AreEqual(index % 2 == 0 ? 1f : -1f, Mathf.Sign(kick.y), "right on even shots, left on odd");
            }
            Assert.AreEqual(0, kicks[0].index, "the pattern starts at the first shot");
            Assert.AreEqual(profile.pitchDegrees * RecoilProfile.StrongFactor, kicks[0].kick.x, 1e-4f, "strong");

            // Reload with the clone's values.
            yield return Tap(keyboard.rKey);
            Assert.IsTrue(gun.IsReloading);
            yield return Wait(gun.Definition.reloadTime + 0.2f);
            Assert.AreEqual(smgMag, gun.Ammo.Loaded);

            // End the session: back to the SMG asset.
            lab.Open();
            yield return null;
            yield return Click("Tab_Test");
            yield return Click("EndTest");
            Assert.IsFalse(lab.Testing);
            Assert.AreSame(smg, gun.Definition);

            // Apply again, then switch guns in the menu: the session ends, the official rifle is in hand.
            yield return Click("Apply");
            Assert.IsTrue(lab.Testing);
            yield return Click("Close");
            menu.Open();
            yield return null;
            var devButton = menu.transform.Find("MenuLayer/MenuPanel/Window/DevGunButton");
            yield return Click(ScreenCenter((RectTransform)devButton));
            Assert.AreSame(rifle, gun.Definition, "the menu switch goes to the official rifle");
            Assert.IsNull(lab.AppliedClone, "the test session ended");
            menu.Close();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Rifle_SemiAuto_DownWeak_Test_ThenNoProfileKeepsTheEarlierRecoil()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var rifle = gun.Definition;
            Assert.AreEqual("proto_rifle", lab.SelectedId);
            var e = lab.Edited("proto_rifle");
            e.automatic = false;
            e.recoil = new RecoilProfile { enabled = true, axis = RecoilAxis.Vertical, vertical = RecoilVertical.Down, horizontal = RecoilHorizontal.None,
                strength = RecoilStrength.Weak, pitchDegrees = 1f, yawDegrees = 0.3f, adsMultiplier = 0.5f };
            lab.ApplyToTest();
            Assert.IsTrue(lab.Testing);
            lab.Close();
            yield return null;
            int loaded = gun.Ammo.Loaded;
            Press(mouse.leftButton);
            yield return Wait(0.5f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(loaded - 1, gun.Ammo.Loaded, "semi-auto: one shot per press");
            Assert.AreEqual(-0.7f, gun.LastRecoil.x, 1e-4f, "down, weak (0.7)");
            Assert.AreEqual(0f, gun.LastRecoil.y);
            yield return Wait(0.15f);
            yield return Tap(mouse.leftButton);
            Assert.AreEqual(loaded - 2, gun.Ammo.Loaded);
            lab.EndTest();
            Assert.AreSame(rifle, gun.Definition);
            // The official rifle (no profile): the earlier recoil, up with a sideways kick within ±yaw.
            yield return Wait(0.6f);
            Press(mouse.leftButton);
            yield return Wait(0.35f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(rifle.recoilPitchDegrees, gun.LastRecoil.x, 1e-4f);
            Assert.LessOrEqual(Mathf.Abs(gun.LastRecoil.y), rifle.recoilYawDegrees + 1e-4f);
        }

        [UnityTest]
        public IEnumerator Lab_SaveLoadReset_InItsOwnFolder_SettingsUntouched()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            var prefsBefore = PlayerPrefs.GetString(TouchLayout.PrefsKey, "");
            yield return Click("Tab_Recoil");
            for (int i = 0; i < 2; i++)
                yield return Click("Cycle_axis"); // Vertical → Horizontal → Diagonal
            yield return Click("Cycle_horizontal"); // None → Left
            var r = lab.Edited("proto_rifle").recoil;
            Assert.AreEqual(RecoilAxis.Diagonal, r.axis);
            Assert.AreEqual(RecoilHorizontal.Left, r.horizontal);
            yield return Click("Tab_Test");
            yield return Click("Save");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "proto_rifle.json")), "saved to the lab's own folder");
            yield return Click("ResetEdit");
            Assert.IsFalse(lab.Edited("proto_rifle").recoil.enabled, "back to the current values");
            yield return Click("Load");
            Assert.AreEqual(RecoilAxis.Diagonal, lab.Edited("proto_rifle").recoil.axis, "restored from the file");
            Assert.AreEqual(RecoilHorizontal.Left, lab.Edited("proto_rifle").recoil.horizontal);
            Assert.IsTrue(lab.Store.TryLoad("proto_rifle", out var saved, out _));
            Assert.AreEqual(WeaponBalanceProfile.CurrentVersion, saved.profileVersion);
            Assert.IsNotEmpty(saved.baseCommit);
            yield return Click("DeleteSaved");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "proto_rifle.json")));
            Assert.AreEqual(prefsBefore, PlayerPrefs.GetString(TouchLayout.PrefsKey, ""), "game settings untouched");
            Assert.IsFalse(gun.Definition.recoilProfile.enabled, "saving does not apply anything");
            yield return Click("Close");
        }

        [UnityTest]
        public IEnumerator Lab_TouchesAndClicks_DoNotReachTheGame_WhileOpen()
        {
            DevGunSelection.ClearSelection();
            yield return OpenLab();
            float yaw = motor.Yaw;
            int loaded = gun.Ammo.Loaded;
            // Clicks on empty parts of the lab window and outside it.
            var c = new Vector3[4];
            lab.Window.GetWorldCorners(c);
            yield return Click(new Vector2(c[2].x - 30f, c[0].y + 30f));
            yield return Click(new Vector2(2f, 2f));
            Press(mouse.leftButton);
            Move(mouse.delta, new Vector2(200f, 0f));
            yield return Wait(0.3f);
            Release(mouse.leftButton);
            yield return null;
            Assert.AreEqual(loaded, gun.Ammo.Loaded, "no shot while the lab is open");
            Assert.AreEqual(yaw, motor.Yaw, 1e-3f, "no look while the lab is open");
            Assert.IsTrue(lab.IsOpen);
            yield return Click("Close");
            Assert.IsFalse(router.IsBlocked);
            Assert.IsFalse(router.Current.FireHeld);
        }
    }
}
#endif
