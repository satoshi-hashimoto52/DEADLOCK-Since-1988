using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deadlock.Combat;
using Deadlock.Editor;
using Deadlock.Input;
using Deadlock.UI;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 32 without a scene: the sub-gun button in the touch layout (default place clear of every button on iPhone
    /// and iPad sizes; a layout saved before it existed gets it at a free place without the save being rewritten), the
    /// loadout strip's side of the ammo panel, and approved art told apart from stand-ins.
    /// </summary>
    public class LoadoutControlsTests
    {
        // Safe Area sizes in canvas units (reference height 1080): 16:9, iPad 4:3, iPhone 16 Pro landscape (notch sides
        // and home indicator taken off), and the side margins beside the iPhone's Safe Area.
        static readonly Vector2 Wide = new Vector2(1920f, 1080f);
        static readonly Vector2 IPad = new Vector2(1440f, 1080f);
        static readonly Vector2 IPhone = new Vector2((2622f - 2f * 186f) * 1080f / 1206f, (1206f - 63f) * 1080f / 1206f);
        static readonly Vector2 IPhoneSides = new Vector2(186f * 1080f / 1206f, 186f * 1080f / 1206f);
        // The narrowest case the layout tests use: a 4:3 screen with an iPhone's notch-side share taken off.
        static readonly Vector2 NarrowNotched = new Vector2(1440f * (1f - 2f * 186f / 2622f), 1080f);

        [Test]
        public void SubGunButton_InTheDefaults_ClearOfEveryButton_OnEveryScreen()
        {
            var defaults = TouchLayout.Defaults();
            CollectionAssert.Contains(TouchLayout.Circles, TouchLayout.SubGun);
            Assert.GreaterOrEqual(defaults.IndexOf(TouchLayout.SubGun), 0);
            Assert.AreEqual("サブ銃切り替え", TouchLayout.DisplayName(TouchLayout.SubGun));
            Assert.AreEqual(TouchLayout.BaseDiameter(TouchLayout.Weapon), TouchLayout.BaseDiameter(TouchLayout.SubGun));
            foreach (var (safe, sides) in new[] { (Wide, Vector2.zero), (IPad, Vector2.zero), (IPhone, IPhoneSides), (NarrowNotched, Vector2.zero) })
                Assert.IsTrue(TouchLayout.IsUsable(defaults, safe, out string problem, sides), $"{safe}: {problem}");
            Assert.IsEmpty(defaults.added);
        }

        [Test]
        public void OldSave_WithoutTheSubGunButton_GetsIt_AtAFreePlace_SaveTextUntouched()
        {
            // A layout saved before Stage 32, with the weapon button moved onto the sub-gun button's default place.
            var old = TouchLayout.Defaults();
            old.items.RemoveAt(old.IndexOf(TouchLayout.SubGun));
            var weapon = old.Get(TouchLayout.Weapon);
            weapon.center = TouchLayout.Defaults().Get(TouchLayout.SubGun).center;
            old.Set(weapon);
            string json = JsonUtility.ToJson(old);
            StringAssert.DoesNotContain(TouchLayout.SubGun, json);

            var loaded = TouchLayout.FromJson(json);
            CollectionAssert.AreEqual(new[] { TouchLayout.SubGun }, loaded.added, "filled in from the defaults");
            Assert.AreEqual(weapon.center, loaded.Get(TouchLayout.Weapon).center, "the player's own places are kept");
            Assert.IsFalse(TouchLayout.IsUsable(loaded, Wide, out _), "on top of the moved weapon button");

            foreach (var (safe, sides) in new[] { (Wide, Vector2.zero), (IPad, Vector2.zero), (IPhone, IPhoneSides) })
            {
                var fitted = loaded.Clone();
                Assert.IsTrue(TouchLayout.FitAddedItems(fitted, safe, sides), $"{safe}: moved");
                Assert.IsTrue(TouchLayout.IsUsable(fitted, safe, out string problem, sides), $"{safe}: {problem}");
                foreach (var item in loaded.items)
                    if (item.id != TouchLayout.SubGun)
                        Assert.AreEqual(item.center, fitted.Get(item.id).center, $"{safe}: {item.id} not moved");
                Assert.GreaterOrEqual(fitted.Get(TouchLayout.SubGun).center.x, 0.5f, $"{safe}: right half (the hand on the buttons)");
            }

            // A usable old layout keeps the default place; a layout with nothing added is never changed.
            var plain = TouchLayout.Defaults();
            plain.items.RemoveAt(plain.IndexOf(TouchLayout.SubGun));
            var plainLoaded = TouchLayout.FromJson(JsonUtility.ToJson(plain));
            Assert.IsFalse(TouchLayout.FitAddedItems(plainLoaded, Wide));
            Assert.AreEqual(TouchLayout.Defaults().Get(TouchLayout.SubGun).center, plainLoaded.Get(TouchLayout.SubGun).center);
            var current = TouchLayout.FromJson(JsonUtility.ToJson(TouchLayout.Defaults()));
            Assert.IsEmpty(current.added);
            Assert.IsFalse(TouchLayout.FitAddedItems(current, Wide));
        }

        [Test]
        public void SlotStrip_UnderThePanel_OverItInTheLowerHalf()
        {
            Assert.IsTrue(HudController.StripBelow(TouchLayout.Defaults().Get(TouchLayout.Ammo).center), "the default panel is at the top");
            Assert.IsTrue(HudController.StripBelow(new Vector2(0.5f, 0.5f)));
            Assert.IsFalse(HudController.StripBelow(new Vector2(0.9f, 0.2f)));
        }

        [Test]
        public void Art_ApprovedForRifleAndSmg_NewGunsOnlyWhatIsRegistered_NothingUnapprovedRegistered()
        {
            var skins = AssetDatabase.LoadAssetAtPath<SkinCatalog>(SkinSetup.CatalogPath);
            Assert.IsNotNull(skins);
            foreach (var path in new[] { WeaponCatalogSetup.RiflePath, SmgCandidateSetup.WeaponPath })
            {
                var gun = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                foreach (WeaponArtView view in System.Enum.GetValues(typeof(WeaponArtView)))
                    Assert.AreEqual(WeaponArtState.Approved, WeaponArtStatus.Of(gun, skins, view), $"{gun.weaponId} {view}");
            }

            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            Assert.AreEqual(4, WeaponCatalogSetup.PlannedArts.Length);
            foreach (var plan in WeaponCatalogSetup.PlannedArts)
            {
                var gun = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(plan.DefinitionPath);
                Assert.IsNotNull(gun, plan.DefinitionPath);
                Assert.AreEqual(plan.WeaponId, gun.weaponId);
                // Stage 38: three separate images, each approved and registered on its own (AssetPipeline): a view is
                // approved exactly when its image is at the planned place; the others stay stand-ins.
                var files = new Dictionary<WeaponArtView, string>
                {
                    [WeaponArtView.Side] = plan.SidePath, [WeaponArtView.Hip] = plan.HipPath, [WeaponArtView.Ads] = plan.AdsPath,
                };
                foreach (var kv in files)
                    Assert.AreEqual(File.Exists(kv.Value) ? WeaponArtState.Approved : WeaponArtState.Placeholder,
                        WeaponArtStatus.Of(gun, skins, kv.Key), $"{gun.weaponId} {kv.Key}");
                StringAssert.EndsWith(WeaponArtStatus.PlaceholderMark, WeaponArtStatus.PlaceholderLabel(gun));
                StringAssert.DoesNotContain("PROVISIONAL", WeaponArtStatus.PlaceholderLabel(gun));
                Assert.AreEqual(File.Exists(plan.SidePath), skins.WeaponSet(gun.weaponId)?.sideView != null, $"{gun.weaponId}: side view in the catalog only when registered");
                Assert.AreNotEqual(plan.SidePath, plan.HipPath);
                Assert.AreNotEqual(plan.HipPath, plan.AdsPath);
                StringAssert.StartsWith("weapon.", plan.SideAssetId);
                bool firstPerson = File.Exists(plan.HipPath) || File.Exists(plan.AdsPath);
                Assert.AreEqual(firstPerson ? 1 : 0, registry.weapons.Count(a => a.weapon == gun), $"{gun.weaponId}: in the art registry only with a registered image");
                foreach (var art in registry.candidates)
                    Assert.AreNotEqual(gun, art.weapon, $"{gun.weaponId} is not a candidate");
            }
        }
    }
}
