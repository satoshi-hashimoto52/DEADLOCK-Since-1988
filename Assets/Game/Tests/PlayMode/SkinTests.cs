using System.Collections;
using Deadlock.Combat;
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
    /// Skins (test screen in the menu): weapon and hand / arm skins chosen separately, applied to the hip / ADS / knife
    /// images at once, kept across weapon switches and scene loads (saved on the device), back to standard on request,
    /// and never touching combat values or input.
    /// </summary>
    public class SkinTests : FiringRangeTestBase
    {
        const string ShaderName = "Deadlock/Skinned Sprite";
        const int Weapon = (int)SkinRegion.WeaponBody;
        const int Glove = (int)SkinRegion.GloveBody;
        const int Sleeve = (int)SkinRegion.SleeveBase;
        const int Band = (int)SkinRegion.WristBand;

        GameMenu menu;
        SpriteRenderer hip, ads, knife;

        IEnumerator Load()
        {
            yield return LoadScene(InputMode.Desktop);
            menu = Object.FindAnyObjectByType<GameMenu>();
            var gunView = player.GetComponentInChildren<WeaponViewModel>(true);
            var knifeView = player.GetComponentInChildren<MeleeViewModel>(true);
            hip = gunView.transform.Find("Hip").GetComponent<SpriteRenderer>();
            ads = gunView.transform.Find("Ads").GetComponent<SpriteRenderer>();
            knife = knifeView.transform.Find("Knife").GetComponent<SpriteRenderer>();
        }

        IEnumerator Choose(string button)
        {
            if (!menu.IsSkinSettingsOpen)
                menu.OpenSkinSettings();
            yield return null;
            var go = GameObject.Find(button);
            Assert.IsNotNull(go, $"skin screen button {button}");
            go.GetComponent<Button>().onClick.Invoke();
            yield return null;
        }

        static Vector4[] Colours(SpriteRenderer r)
        {
            var colours = SpriteSkin.CurrentColours(r);
            Assert.IsNotNull(colours, $"{r.name} has a skin colour table");
            return colours;
        }

        static void AssertRegion(SpriteRenderer r, int region, float? hue, string why)
        {
            var c = Colours(r)[region];
            if (hue == null)
                Assert.AreEqual(0f, c.z, $"{r.name} region {(SkinRegion)region} keeps its drawn colour: {why}");
            else
            {
                Assert.AreEqual(1f, c.z, $"{r.name} region {(SkinRegion)region} recoloured: {why}");
                Assert.AreEqual(hue.Value / 360f, c.x, 1e-4f, $"{r.name} region {(SkinRegion)region} hue: {why}");
            }
        }

        void AssertStandard(string why)
        {
            foreach (var r in new[] { hip, ads, knife })
                foreach (var c in Colours(r))
                    Assert.AreEqual(0f, c.z, $"{r.name}: standard look ({why})");
        }

        [UnityTest]
        public IEnumerator WeaponAndHands_ChosenSeparately_SameHandsOnHipAdsKnife_KeptAcrossSwitchAndReload_ResetToStandard()
        {
            yield return Load();
            foreach (var r in new[] { hip, ads, knife })
                Assert.AreEqual(ShaderName, r.sharedMaterial.shader.name, $"{r.name} draws with the skin shader");
            AssertStandard("nothing saved");
            string rifleBefore = JsonUtility.ToJson(gun.Definition);
            string knifeBefore = JsonUtility.ToJson(melee.Definition);

            // Hands only: the same hand / arm colour on hip, ADS and the knife; the rifle body stays as drawn.
            yield return Choose("PlayerSkin_test_navy");
            foreach (var r in new[] { hip, ads, knife })
            {
                AssertRegion(r, Glove, 215f, "player skin");
                AssertRegion(r, Sleeve, 210f, "player skin");
                AssertRegion(r, Band, null, "the test skin leaves the wrist band");
                AssertRegion(r, Weapon, null, "weapon skin still standard");
            }
            Assert.AreEqual("test_navy", PlayerPrefs.GetString(SkinSelection.PlayerKey), "saved on the device");

            // Weapon only: back to standard hands, green rifle body on hip and ADS; the knife has no body skin.
            yield return Choose("PlayerSkin_standard");
            yield return Choose("WeaponSkin_proto_rifle_test_green");
            AssertRegion(hip, Weapon, 95f, "weapon skin");
            AssertRegion(ads, Weapon, 95f, "weapon skin");
            AssertRegion(hip, Glove, null, "hands standard while the weapon is skinned");
            AssertRegion(knife, Weapon, null, "no knife body skin");
            AssertRegion(knife, Glove, null, "hands standard");

            // Both, then switch to the knife and back: the choices stay.
            yield return Choose("PlayerSkin_test_navy");
            menu.CloseSkinSettings();
            menu.Close();
            yield return null;
            weapons.Request(WeaponSlot.Melee);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            Assert.IsTrue(knife.enabled, "knife shown");
            AssertRegion(knife, Glove, 215f, "hands kept after switching");
            weapons.Request(WeaponSlot.Gun);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            AssertRegion(hip, Weapon, 95f, "weapon skin kept after switching back");
            AssertRegion(hip, Glove, 215f, "hands kept after switching back");

            // Looks only.
            Assert.AreEqual(rifleBefore, JsonUtility.ToJson(gun.Definition), "rifle values unchanged by skins");
            Assert.AreEqual(knifeBefore, JsonUtility.ToJson(melee.Definition), "knife values unchanged by skins");

            // New scene load (same as a restart for saved choices): restored.
            yield return Load();
            AssertRegion(hip, Weapon, 95f, "restored after loading the scene");
            AssertRegion(ads, Weapon, 95f, "restored after loading the scene");
            AssertRegion(hip, Glove, 215f, "restored after loading the scene");
            AssertRegion(knife, Glove, 215f, "restored after loading the scene");

            yield return Choose("SkinResetButton");
            AssertStandard("reset");
            Assert.AreEqual(SkinCatalog.StandardId, SkinSelection.PlayerSkinId);
            Assert.AreEqual(SkinCatalog.StandardId, SkinSelection.GetWeaponSkinId("proto_rifle"));
        }

        [UnityTest]
        public IEnumerator SkinChange_KeepsControlsAndShooting()
        {
            yield return Load();
            ControlSettings.TouchControlTransparency = 40;
            ControlSettings.SetAdsMode(InputMode.Desktop, AdsMode.Toggle);
            yield return Choose("PlayerSkin_test_navy");
            yield return Choose("WeaponSkin_proto_rifle_test_green");
            Assert.AreEqual(40, ControlSettings.TouchControlTransparency, "transparency kept");
            Assert.AreEqual(AdsMode.Toggle, ControlSettings.GetAdsMode(InputMode.Desktop), "ADS setting kept");
            Assert.IsTrue(router.IsBlocked, "menu open: input blocked as usual");
            menu.CloseSkinSettings();
            menu.Close();
            yield return null;
            Assert.IsFalse(router.IsBlocked, "menu closed: input back");

            int before = gun.Ammo.Loaded;
            yield return Tap(mouse.leftButton);
            yield return Wait(0.2f);
            Assert.Less(gun.Ammo.Loaded, before, "fires as before");
        }

        [UnityTest]
        public IEnumerator UnknownSavedSkin_FallsBackToStandard()
        {
            PlayerPrefs.SetString(SkinSelection.PlayerKey, "no_such_skin");
            PlayerPrefs.SetString(SkinSelection.WeaponKey("proto_rifle"), "no_such_skin");
            yield return Load();
            AssertStandard("a removed skin id shows the drawn colours");
        }
    }
}
