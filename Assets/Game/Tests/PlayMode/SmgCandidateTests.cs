using System.Collections;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using Deadlock.UI;
using Deadlock.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Deadlock.Tests
{
    /// <summary>
    /// SMG candidate through the development test choice: rifle ⇄ SMG in the menu, the SMG's hip / ADS art, firing, ADS,
    /// reload, the knife switch and a retry keep working; the SMG's weapon skin and the shared hand / arm skin reach its
    /// images independently of the rifle; the equipment screen never shows the rifle's side view as the SMG; the rifle's
    /// definition is untouched.
    /// </summary>
    public class SmgCandidateTests : FiringRangeTestBase
    {
        const string Smg = "proto_smg", Rifle = "proto_rifle";

        DevGunSelection selection;
        GameMenu menu;
        SpriteRenderer hip, ads;

        public override void TearDown()
        {
            DevGunSelection.ClearSelection();
            base.TearDown();
        }

        IEnumerator Load(string scene = SceneName)
        {
            yield return LoadScene(InputMode.Desktop, scene);
            selection = player.GetComponent<DevGunSelection>();
            menu = Object.FindAnyObjectByType<GameMenu>();
            var view = player.GetComponentInChildren<WeaponViewModel>(true);
            hip = view.transform.Find("Hip").GetComponent<SpriteRenderer>();
            ads = view.transform.Find("Ads").GetComponent<SpriteRenderer>();
        }

        [UnityTest]
        public IEnumerator MenuSwitchesRifleAndSmg_SmgArt_FireAdsReloadKnife_RifleUntouched()
        {
            DevGunSelection.ClearSelection();
            yield return Load();
            Assert.IsNotNull(selection, "offline scenes offer the test choice");
            Assert.AreEqual(new[] { Rifle, Smg }, selection.Options.Select(o => o.weaponId).ToArray());
            Assert.AreEqual(Rifle, gun.Definition.weaponId, "the rifle by default");
            var rifle = gun.Definition;
            float rifleInterval = rifle.fireInterval, rifleDamage = rifle.damage;
            var rifleView = rifle.viewProfile;

            menu.Open();
            yield return null;
            var button = menu.transform.Find("MenuLayer/MenuPanel/Window/DevGunButton");
            Assert.IsNotNull(button, "menu entry");
            StringAssert.Contains("ライフル → SMG（候補）", button.GetComponentInChildren<Text>().text);
            button.GetComponent<Button>().onClick.Invoke();
            yield return null;
            StringAssert.Contains("SMG（候補） → ライフル", button.GetComponentInChildren<Text>().text);
            menu.Close();
            yield return null;

            var smg = gun.Definition;
            Assert.AreEqual(Smg, smg.weaponId);
            StringAssert.Contains("smg_hip_candidate_v2", hip.sprite.texture.name);
            StringAssert.Contains("smg_ads_front_sight_gap3", ads.sprite.texture.name);
            Assert.AreEqual(rifleInterval, smg.fireInterval, "performance: the rifle's values (for checking)");
            Assert.AreEqual(rifleDamage, smg.damage);
            Assert.AreSame(rifleView, rifle.viewProfile, "the rifle's own definition is untouched");
            Assert.AreEqual(Rifle, rifle.weaponId);
            Assert.AreEqual(smg.magazineSize, gun.Ammo.Loaded, "fresh gun after the switch");

            // Fire, ADS, reload.
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.5f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.Less(gun.Ammo.Loaded, smg.magazineSize, "fired");
            Press(mouse.rightButton, queueEventOnly: true);
            yield return Wait(smg.adsTime + 0.2f);
            Assert.AreEqual(1f, gun.AdsBlend, 1e-3f, "aimed");
            Assert.IsTrue(ads.enabled, "the SMG sight picture is shown");
            Release(mouse.rightButton, queueEventOnly: true);
            yield return Wait(smg.adsTime + 0.2f);
            Assert.IsTrue(gun.TryStartReload());
            yield return Wait(smg.reloadTime + 0.2f);
            Assert.IsFalse(gun.IsReloading);
            Assert.AreEqual(smg.magazineSize, gun.Ammo.Loaded, "reloaded");

            // Knife and back: still the SMG.
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.IsFalse(hip.enabled && ads.enabled);
            yield return Tap(keyboard.digit1Key);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(Smg, gun.Definition.weaponId);
            StringAssert.Contains("smg_hip_candidate_v2", hip.sprite.texture.name);
            Assert.IsTrue(hip.enabled, "SMG hip view back");

            // And back to the rifle.
            Assert.IsTrue(selection.Select(Rifle));
            yield return null;
            Assert.AreSame(rifle, gun.Definition);
            Assert.AreSame(rifleView.hipSprite, hip.sprite);
        }

        [UnityTest]
        public IEnumerator Retry_KeepsTheSmg_FreshState()
        {
            DevGunSelection.ClearSelection();
            yield return Load(GameScenes.Training);
            Assert.IsTrue(selection.Select(Smg));
            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.4f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Object.FindAnyObjectByType<TrainingSession>().Retry();
            yield return null;
            yield return null;
            yield return null;
            var fresh = GameObject.Find("Player").GetComponent<GunController>();
            Assert.AreEqual(Smg, fresh.Definition.weaponId, "the test choice lasts through a retry");
            Assert.AreEqual(fresh.Definition.magazineSize, fresh.Ammo.Loaded, "fresh magazine");
            var view = fresh.GetComponentInChildren<WeaponViewModel>(true);
            StringAssert.Contains("smg_hip_candidate_v2", view.transform.Find("Hip").GetComponent<SpriteRenderer>().sprite.texture.name);
        }

        static Vector4 Region(SpriteRenderer r, SkinRegion region)
        {
            var colours = SpriteSkin.CurrentColours(r);
            Assert.IsNotNull(colours, $"{r.name}: skinned");
            return colours[(int)region];
        }

        [UnityTest]
        public IEnumerator Skins_SmgWeaponSkinAndHandsSkin_Independent_EquipmentScreenShowsNoRifleAsSmg()
        {
            DevGunSelection.ClearSelection();
            yield return Load();
            selection.Select(Smg);
            yield return null;
            Assert.AreEqual("Deadlock/Skinned Sprite", hip.sharedMaterial.shader.name, "SMG hip has a region map");
            Assert.AreEqual("Deadlock/Skinned Sprite", ads.sharedMaterial.shader.name, "SMG ADS has a region map");
            Assert.AreEqual(0f, Region(hip, SkinRegion.WeaponBody).z, "standard look");

            SkinSelection.SetWeaponSkinId(Smg, "test_green");
            yield return null;
            Assert.AreEqual(1f, Region(hip, SkinRegion.WeaponBody).z, "SMG weapon skin on the hip");
            Assert.AreEqual(1f, Region(ads, SkinRegion.WeaponBody).z, "and on the ADS");
            Assert.AreEqual(0f, Region(hip, SkinRegion.GloveBody).z, "the weapon skin never reaches the hands");
            Assert.AreEqual("standard", SkinSelection.GetWeaponSkinId(Rifle), "the rifle's skin is its own");
            SkinSelection.PlayerSkinId = "test_navy";
            yield return null;
            Assert.AreEqual(1f, Region(hip, SkinRegion.GloveBody).z, "hand / arm skin reaches the SMG too");
            Assert.AreEqual(1f, Region(ads, SkinRegion.SleeveBase).z);

            selection.Select(Rifle);
            yield return null;
            Assert.AreEqual(0f, Region(hip, SkinRegion.WeaponBody).z, "the rifle keeps its standard look");
            Assert.AreEqual(1f, Region(hip, SkinRegion.GloveBody).z, "the hands keep theirs");

            // Equipment screen: the SMG has no side view yet, the rifle's is never shown in its place.
            selection.Select(Smg);
            menu.OpenSkinSettings();
            yield return null;
            var preview = menu.transform.Find("MenuLayer/SkinSettingsPanel/SafeArea/Window/WeaponPreview");
            StringAssert.Contains("SMG（候補）", preview.Find("Caption").GetComponent<Text>().text);
            StringAssert.Contains("画像なし", preview.Find("Caption").GetComponent<Text>().text);
            Assert.IsNull(preview.Find("Image"), "no side view image for the SMG");
            Assert.IsNotNull(GameObject.Find("WeaponSkin_proto_smg_test_green"), "SMG skin choice");
            menu.CloseSkinSettings();
            selection.Select(Rifle);
            menu.OpenSkinSettings();
            yield return null;
            preview = menu.transform.Find("MenuLayer/SkinSettingsPanel/SafeArea/Window/WeaponPreview");
            Assert.IsNotNull(preview.Find("Image"), "the rifle's side view for the rifle");
        }
    }
}
