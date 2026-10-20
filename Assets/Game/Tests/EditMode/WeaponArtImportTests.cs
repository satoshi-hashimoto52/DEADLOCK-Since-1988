using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Deadlock.Combat;
using Deadlock.Editor;
using Deadlock.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>
    /// Per-weapon art import (<see cref="WeaponArtDefinition"/> / <see cref="WeaponArtRegistry"/>): independent weapons,
    /// explicit errors instead of silent fallbacks, and the approved files are only read.
    /// </summary>
    public class WeaponArtImportTests
    {
        const string Rifle = "Assets/Game/Art/Weapons/Rifle";
        const string Temp = "Assets/TmpWeaponArtTest";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Temp))
                AssetDatabase.DeleteAsset(Temp);
        }

        [Test]
        public void Registry_ListsOnlyFinishedWeapons_TheRifle()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            Assert.IsNotNull(registry);
            CollectionAssert.AreEqual(new[] { "proto_rifle" }, registry.weapons.Select(w => w.WeaponId).ToArray());
            var rifle = registry.weapons[0];
            Assert.AreEqual("Assets/Game/Data/WeaponView_ProtoRifle_Pixel.asset", AssetDatabase.GetAssetPath(rifle.viewProfile), "Existing profile (and its GUID) kept");
            Assert.AreSame(rifle.viewProfile, rifle.weapon.viewProfile, "The weapon uses the profile written from its definition");
            Assert.AreEqual($"{Rifle}/rifle_hip_approved_opaque.png", AssetDatabase.GetAssetPath(rifle.hip.image));
            Assert.AreEqual($"{Rifle}/rifle_ads_approved_opaque.png", AssetDatabase.GetAssetPath(rifle.ads.image));
        }

        [Test]
        public void Import_LeavesSourcesImagesAndPlacementsUntouched()
        {
            string[] files =
            {
                $"{Rifle}/rifle_hip_approved.png", $"{Rifle}/rifle_ads_approved.png",
                $"{Rifle}/rifle_hip_approved_opaque.png", $"{Rifle}/rifle_ads_approved_opaque.png",
                $"{Rifle}/rifle_hip_approved.placement.json", $"{Rifle}/rifle_ads_approved.placement.json",
            };
            var before = files.ToDictionary(f => f, Hash);
            PixelWeaponArt.ImportRegistered();
            foreach (var f in files)
                Assert.AreEqual(before[f], Hash(f), $"{f} was written");
        }

        [Test]
        public void TwoWeapons_ImportIndependently()
        {
            var a = MakeDefinition("test_a", hipMuzzle: (900, 400), adsAim: (887, 390));
            var b = MakeDefinition("test_b", hipMuzzle: (1000, 450), adsAim: (880, 380));
            PixelWeaponArt.Import(new[] { a, b }, "test");

            Assert.IsNotNull(a.viewProfile);
            Assert.IsNotNull(b.viewProfile);
            Assert.AreNotSame(a.viewProfile, b.viewProfile);
            Assert.AreEqual($"{Temp}/WeaponView_test_a_Art.asset", AssetDatabase.GetAssetPath(a.viewProfile));
            Assert.AreSame(a.viewProfile, a.weapon.viewProfile);
            Assert.AreSame(b.viewProfile, b.weapon.viewProfile);
            Assert.AreEqual(AssetDatabase.GetAssetPath(a.hip.image), AssetDatabase.GetAssetPath(a.viewProfile.hipSprite));
            Assert.AreEqual(AssetDatabase.GetAssetPath(b.ads.image), AssetDatabase.GetAssetPath(b.viewProfile.adsSprite));
            // Each one's own placement: pivots from its own muzzle / aim.
            Assert.AreEqual(900f, a.viewProfile.hipSprite.pivot.x, 0.5f);
            Assert.AreEqual(1000f, b.viewProfile.hipSprite.pivot.x, 0.5f);
            Assert.AreEqual(887f - 390f, a.viewProfile.adsSprite.pivot.y, 0.5f);
            Assert.AreEqual(887f - 380f, b.viewProfile.adsSprite.pivot.y, 0.5f);
            // The real rifle is not touched by importing other weapons.
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Game/Data/Weapon_ProtoRifle.asset");
            Assert.AreEqual("Assets/Game/Data/WeaponView_ProtoRifle_Pixel.asset", AssetDatabase.GetAssetPath(rifle.viewProfile));
        }

        [Test]
        public void DuplicateWeaponId_IsAnError_NamingBothDefinitions_NothingImported()
        {
            var a = MakeDefinition("test_dup", (900, 400), (887, 390), "a");
            var b = MakeDefinition("test_dup", (900, 400), (887, 390), "b");
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { a, b }, "test"));
            StringAssert.Contains("'test_dup' is used twice", e.Message);
            StringAssert.Contains(AssetDatabase.GetAssetPath(a), e.Message);
            StringAssert.Contains(AssetDatabase.GetAssetPath(b), e.Message);
            Assert.IsNull(a.viewProfile);
            Assert.IsNull(b.viewProfile);
        }

        [Test]
        public void MissingArt_IsAnError_NamingWeaponAndField_NoFallback()
        {
            var a = MakeDefinition("test_missing", (900, 400), (887, 390));
            var b = MakeDefinition("test_ok", (900, 400), (887, 390));
            a.ads.image = null;
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { a, b }, "test"));
            StringAssert.Contains("[test_missing]", e.Message);
            StringAssert.Contains("'ads.image' is not set", e.Message);
            Assert.AreEqual(1, e.Problems.Count, "Only the broken definition is reported");
            Assert.IsNull(a.viewProfile, "No profile made from partial / other art");
            Assert.IsNull(b.viewProfile, "Nothing is imported while a definition is broken");
            Assert.IsNull(a.weapon.viewProfile);
        }

        [Test]
        public void ImageNotMatchingItsSource_AndPlacementWithoutAim_AreErrors()
        {
            var a = MakeDefinition("test_bad", (900, 400), (887, 390));
            a.hip.source = a.ads.image; // 1774x887 vs the hip image 1672x941
            File.WriteAllText(AssetDatabase.GetAssetPath(a.ads.placement), "{ \"muzzle\": [887, 436], \"screenHeightPixels\": 772 }");
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(a.ads.placement));
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { a }, "test"));
            StringAssert.Contains("[test_bad]", e.Message);
            StringAssert.Contains("'hip.image'", e.Message);
            StringAssert.Contains("needs 'aim'", e.Message);
        }

        // ---- helpers -------------------------------------------------------------------------------------------------

        WeaponArtDefinition MakeDefinition(string id, (int x, int y) hipMuzzle, (int x, int y) adsAim, string suffix = "")
        {
            if (!AssetDatabase.IsValidFolder(Temp))
                AssetDatabase.CreateFolder("Assets", "TmpWeaponArtTest");
            string name = id + suffix;
            string Copy(string file, string to)
            {
                string path = $"{Temp}/{name}_{to}";
                Assert.IsTrue(AssetDatabase.CopyAsset($"{Rifle}/{file}", path), path);
                return path;
            }
            string Json(string to, string text)
            {
                string path = $"{Temp}/{name}_{to}";
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path);
                return path;
            }
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.weaponId = id;
            AssetDatabase.CreateAsset(weapon, $"{Temp}/{name}_weapon.asset");
            var d = ScriptableObject.CreateInstance<WeaponArtDefinition>();
            d.weapon = weapon;
            d.baseProfile = AssetDatabase.LoadAssetAtPath<WeaponViewProfile>("Assets/Game/Data/WeaponView_ProtoRifle.asset");
            d.hip.source = AssetDatabase.LoadAssetAtPath<Texture2D>(Copy("rifle_hip_approved.png", "hip_source.png"));
            d.hip.image = AssetDatabase.LoadAssetAtPath<Texture2D>(Copy("rifle_hip_approved_opaque.png", "hip.png"));
            d.hip.placement = AssetDatabase.LoadAssetAtPath<TextAsset>(Json("hip.placement.json",
                $"{{ \"muzzle\": [{hipMuzzle.x}, {hipMuzzle.y}], \"hold\": [1260, 880], \"screenHeightPixels\": 1045, \"hipScaleAt4x3\": 1 }}"));
            d.ads.source = AssetDatabase.LoadAssetAtPath<Texture2D>(Copy("rifle_ads_approved.png", "ads_source.png"));
            d.ads.image = AssetDatabase.LoadAssetAtPath<Texture2D>(Copy("rifle_ads_approved_opaque.png", "ads.png"));
            d.ads.placement = AssetDatabase.LoadAssetAtPath<TextAsset>(Json("ads.placement.json",
                $"{{ \"aim\": [{adsAim.x}, {adsAim.y}], \"muzzle\": [887, 436], \"screenHeightPixels\": 772 }}"));
            AssetDatabase.CreateAsset(d, $"{Temp}/{name}_art.asset");
            return d;
        }

        static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}
