using System.Collections;
using System.Linq;
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

        static SkinnedImage Preview(string box)
        {
            var go = GameObject.Find(box + "/Image");
            Assert.IsNotNull(go, $"{box}: preview image");
            return go.GetComponent<SkinnedImage>();
        }

        static void AssertTable(SkinnedImage image, int region, float? hue, string why)
        {
            var c = image.Colours[region];
            if (hue == null)
                Assert.AreEqual(0f, c.z, $"{image.transform.parent.name} {(SkinRegion)region}: drawn colour ({why})");
            else
            {
                Assert.AreEqual(1f, c.z, $"{image.transform.parent.name} {(SkinRegion)region}: recoloured ({why})");
                Assert.AreEqual(hue.Value / 360f, c.x, 1e-4f, why);
            }
            var shown = image.Instance.GetVectorArray(SpriteSkin.ColoursId);
            Assert.AreEqual(c, shown[region], $"{image.transform.parent.name}: the material draws this table");
        }

        [UnityTest]
        public IEnumerator EquipmentScreen_PlayerAndRifle_SameChoiceAsFirstPerson_KeptAndReset_NoMaterialGrowth()
        {
            yield return Load();
            menu.OpenSkinSettings();
            yield return null;
            var player = Preview("PlayerPreview");
            var rifle = Preview("WeaponPreview");
            foreach (var p in new[] { player, rifle })
            {
                var image = p.GetComponent<Image>();
                Assert.IsNotNull(image.sprite, $"{p.transform.parent.name}: image");
                Assert.IsTrue(image.preserveAspect, "aspect kept");
                Assert.AreSame(p.Instance, image.material, "own material instance");
                Assert.AreEqual("Deadlock/Skinned UI", p.Instance.shader.name);
                foreach (var c in p.Colours)
                    Assert.AreEqual(0f, c.z, "standard look");
            }

            // Rifle only: side view and first person change, the player does not.
            yield return Choose("WeaponSkin_proto_rifle_test_green");
            AssertTable(rifle, Weapon, 95f, "weapon skin");
            AssertRegion(hip, Weapon, 95f, "same choice in the first-person view");
            foreach (int r in new[] { Glove, Sleeve, Band, Weapon })
                AssertTable(player, r, null, "rifle only");

            // Hands / arms only: player front and first person change; the side view has no hands (player skin off).
            yield return Choose("WeaponSkin_proto_rifle_standard");
            yield return Choose("PlayerSkin_test_navy");
            AssertTable(player, Glove, 215f, "player skin");
            AssertTable(player, Sleeve, 210f, "player skin");
            AssertTable(player, Weapon, null, "no weapon on the player image");
            AssertTable(rifle, Weapon, null, "rifle standard");
            AssertTable(rifle, Glove, null, "no hands on the side view");
            AssertRegion(knife, Glove, 215f, "same choice in the first-person view");
            AssertRegion(hip, Glove, 215f, "same choice in the first-person view");

            // Open / close and recolour repeatedly: no new materials or textures.
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture>().Length;
            for (int i = 0; i < 16; i++)
            {
                menu.CloseSkinSettings();
                menu.Close();
                yield return null;
                menu.OpenSkinSettings();
                yield return null;
                yield return Choose(i % 2 == 0 ? "WeaponSkin_proto_rifle_test_green" : "WeaponSkin_proto_rifle_standard");
            }
            Assert.AreEqual(materials, Resources.FindObjectsOfTypeAll<Material>().Length, "no material growth");
            Assert.AreEqual(textures, Resources.FindObjectsOfTypeAll<Texture>().Length, "no texture growth");
            AssertTable(rifle, Weapon, null, "last choice was standard");
            yield return Choose("WeaponSkin_proto_rifle_test_green");

            // Scene load (same saved choices as a restart): restored on the screen and in first person.
            menu.CloseSkinSettings();
            menu.Close();
            yield return Load();
            menu.OpenSkinSettings();
            yield return null;
            player = Preview("PlayerPreview");
            rifle = Preview("WeaponPreview");
            AssertTable(rifle, Weapon, 95f, "restored");
            AssertTable(player, Glove, 215f, "restored");
            AssertRegion(hip, Weapon, 95f, "restored");

            yield return Choose("SkinResetButton");
            foreach (var p in new[] { player, rifle })
                foreach (var c in p.Colours)
                    Assert.AreEqual(0f, c.z, "reset: standard on the equipment screen");
            AssertStandard("reset: standard in first person");
            Assert.AreEqual(SkinCatalog.StandardId, PlayerPrefs.GetString(SkinSelection.PlayerKey));
            Assert.AreEqual(SkinCatalog.StandardId, PlayerPrefs.GetString(SkinSelection.WeaponKey("proto_rifle")));

            // A change made elsewhere (not with the buttons) also updates the labels and the previews.
            SkinSelection.PlayerSkinId = "test_navy";
            yield return null;
            StringAssert.Contains("色違い", GameObject.Find("PlayerSkinLabel").GetComponent<Text>().text, "label follows the saved choice");
            AssertTable(player, Glove, 215f, "preview follows the saved choice");
        }

        [UnityTest]
        public IEnumerator EquipmentScreen_OpenCloseEvents_InputBlockedOnlyWhileOpen_WeaponBack()
        {
            yield return Load();
            int opened = 0, closed = 0;
            menu.SkinSettingsOpened += () => opened++;
            menu.SkinSettingsClosed += () => closed++;
            Assert.IsFalse(EquipmentScreenDiagnostics.Enabled, "device diagnostics never run in the Editor");
            for (int i = 0; i < 3; i++)
            {
                menu.OpenSkinSettings();
                yield return null;
                Assert.IsTrue(router.IsBlocked, "input blocked while the equipment screen is open");
                if (i % 2 == 0)
                    menu.CloseSkinSettings(); // back to the menu, then close it
                menu.Close();
                yield return null;
                Assert.IsFalse(router.IsBlocked, "input back after closing");
                Assert.IsFalse(menu.IsSkinSettingsOpen);
                Assert.IsTrue(hip.enabled, "hip weapon shown again");
            }
            Assert.AreEqual(3, opened);
            Assert.AreEqual(3, closed, "closing straight from the equipment screen also counts");
        }

        [UnityTest]
        public IEnumerator EquipmentScreen_Layout_InsideScreen_NoOverlap()
        {
            yield return Load();
            menu.OpenSkinSettings();
            yield return null;
            Canvas.ForceUpdateCanvases();
            Rect Screen(string path)
            {
                var rect = (RectTransform)GameObject.Find(path).transform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners); // overlay canvas: world == screen pixels
                return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            }
            var screen = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            var window = Screen("SkinSettingsPanel/SafeArea/Window");
            Assert.IsTrue(screen.Contains(window.min) && screen.Contains(window.max), $"window {window} inside the screen {screen}");
            var parts = new[] { "PlayerPreview", "WeaponPreview", "Choices" }.Select(n => (n, r: Screen(n))).ToArray();
            foreach (var (n, r) in parts)
                Assert.IsTrue(window.Contains(r.min) && window.Contains(r.max), $"{n} inside the window");
            for (int i = 0; i < parts.Length; i++)
                for (int j = i + 1; j < parts.Length; j++)
                    Assert.IsFalse(parts[i].r.Overlaps(parts[j].r), $"{parts[i].n} and {parts[j].n} do not overlap");
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
