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
        public void Registry_Melee_TheKnifeCandidate_PointPivotOnTheGrip_MotionOverridesApplied()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            CollectionAssert.AreEqual(new[] { "proto_knife" }, registry.melee.Select(m => m.WeaponId).ToArray());
            var knife = registry.melee[0];
            Assert.AreSame(knife.viewProfile, knife.melee.viewProfile);
            var sprite = knife.viewProfile.sprite;
            Assert.AreEqual($"{Knife}/knife_candidate_opaque.png", AssetDatabase.GetAssetPath(sprite));
            var importer = (TextureImporter)AssetImporter.GetAtPath($"{Knife}/knife_candidate_opaque.png");
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.GreaterOrEqual(importer.maxTextureSize, 2048);
            Assert.AreEqual(560f, sprite.pivot.x, 0.5f, "pivot = grip (560, 730 from the top-left)");
            Assert.AreEqual(992f - 730f, sprite.pivot.y, 0.5f);
            // Presentation overrides for this art; the base profile keeps its values.
            Assert.AreEqual(0f, knife.viewProfile.strikeRoll);
            Assert.AreEqual(new Vector2(-0.46f, 0.04f), knife.viewProfile.strikeOffset);
            Assert.AreEqual(60f, knife.baseProfile.strikeRoll);
            Assert.AreEqual(knife.baseProfile.windupRoll, knife.viewProfile.windupRoll);
            // The provisional knife profile is still there (not deleted).
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<MeleeViewProfile>(PixelWeaponArt.KnifeProfilePath));
        }

        [Test]
        public void Melee_MissingImage_OrIdUsedByAGun_AreErrors_NothingImported()
        {
            var gun = MakeDefinition("test_shared", (900, 400), (887, 390));
            var m = MakeMelee("test_shared");
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new[] { gun }, new[] { m }, "test"));
            StringAssert.Contains("'test_shared' is used twice", e.Message);
            var m2 = MakeMelee("test_melee_missing");
            m2.art.image = null;
            e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new WeaponArtDefinition[0], new[] { m2 }, "test"));
            StringAssert.Contains("[test_melee_missing]", e.Message);
            StringAssert.Contains("'art.image' is not set", e.Message);
            Assert.IsNull(m2.viewProfile);
        }

        [Test]
        public void Melee_ImportsIndependently_UnknownOverrideIsAnError()
        {
            var m = MakeMelee("test_melee_ok");
            PixelWeaponArt.Import(new WeaponArtDefinition[0], new[] { m }, "test");
            Assert.AreEqual($"{Temp}/MeleeView_test_melee_ok_Art.asset", AssetDatabase.GetAssetPath(m.viewProfile));
            Assert.AreSame(m.viewProfile, m.melee.viewProfile);
            var real = AssetDatabase.LoadAssetAtPath<MeleeDefinition>("Assets/Game/Data/Melee_ProtoKnife.asset");
            Assert.AreEqual("MeleeView_ProtoKnife_Candidate", real.viewProfile.name, "The real knife is not touched");
            var bad = MakeMelee("test_melee_bad", "{ \"grip\": [560, 730], \"screenHeightPixels\": 1300, \"motionOverrides\": [{\"field\": \"noSuchField\", \"value\": [1]}] }");
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new WeaponArtDefinition[0], new[] { bad }, "test"));
            StringAssert.Contains("'noSuchField' is not a field", e.Message);
        }

        [Test]
        public void Registry_Knife_ThreeAttackPoses_GripPivots_SameDotSize_TimelineAndNarrowLayout()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponArtRegistry>(PixelWeaponArt.RegistryPath);
            var knife = registry.melee.Single(m => m.WeaponId == "proto_knife");
            var view = knife.viewProfile;
            var expected = new[]
            {
                ("knife_pose1_windup_v3_gloves_opaque.png", 302f, 722f, 1568f),
                ("knife_pose2_center_pass_extended_opaque.png", 610f, 638f, 1400f),
                ("knife_pose3_follow_through_gloves_opaque.png", 585f, 470f, 1116f),
            };
            Assert.AreEqual(expected.Length, view.poseSprites.Length);
            Assert.AreEqual(expected.Length, view.poseSkinRegions.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                var (file, gx, gy, height) = expected[i];
                var sprite = view.poseSprites[i];
                Assert.AreEqual($"{Knife}/Poses/{file}", AssetDatabase.GetAssetPath(sprite), $"pose {i + 1}: the displayed derivative");
                Assert.AreEqual(gx, sprite.pivot.x, 0.5f, $"pose {i + 1}: pivot = grip x");
                Assert.AreEqual(height - gy, sprite.pivot.y, 0.5f, $"pose {i + 1}: pivot = grip y");
                Assert.AreEqual(view.sprite.pixelsPerUnit, sprite.pixelsPerUnit, 1e-3f, $"pose {i + 1}: same dot size as the rest image");
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                Assert.AreEqual(FilterMode.Point, importer.filterMode);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
                Assert.IsFalse(importer.mipmapEnabled);
                Assert.GreaterOrEqual(importer.maxTextureSize, Mathf.Max(sprite.texture.width, sprite.texture.height), $"pose {i + 1}: never downscaled");
                Assert.IsNotNull(view.poseSkinRegions[i], $"pose {i + 1}: region map");
                Assert.AreEqual(AssetDatabase.GetAssetPath(knife.poses[i].skinRegions), AssetDatabase.GetAssetPath(view.poseSkinRegions[i]), $"pose {i + 1}: the definition's region map");
            }
            // The reviewed preview timeline (docs/review/player-parts/knife/slash/finishing).
            var keys = view.poseTimeline;
            CollectionAssert.AreEqual(new[] { -1, 0, 1, 2, 2, -1 }, keys.Select(k => k.pose).ToArray());
            CollectionAssert.AreEqual(new[] { MeleePhase.Windup, MeleePhase.Windup, MeleePhase.Active, MeleePhase.Active, MeleePhase.Recovery, MeleePhase.Recovery }, keys.Select(k => k.phase).ToArray());
            Assert.AreEqual(new Vector2(0.58f, -0.16f), keys[1].grip);
            Assert.AreEqual(new Vector2(0.20f, -0.20f), keys[2].grip);
            Assert.AreEqual(new Vector2(-0.38f, -0.30f), keys[3].grip);
            Assert.AreEqual(0.30f, view.narrowCentreX, 1e-6f);
            Assert.AreEqual(0.67f, view.narrowScaleAt4x3, 1e-6f);
            // The base profile (shared with the provisional knife) has no poses.
            Assert.AreEqual(0, knife.baseProfile.poseSprites.Length);
            Assert.AreEqual(1f, knife.baseProfile.narrowScaleAt4x3);
        }

        [Test]
        public void Melee_PosesWithoutTimeline_OrTimelineNamingAMissingPose_AreErrors()
        {
            var m = MakeMelee("test_melee_poses");
            m.poses = new[] { m.art };
            var e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new WeaponArtDefinition[0], new[] { m }, "test"));
            StringAssert.Contains("'poseTimeline' is not set", e.Message);
            string json = $"{Temp}/test_melee_poses.timeline.json";
            File.WriteAllText(json, "{ \"keys\": [ { \"phase\": \"Active\", \"from\": 0, \"to\": 1, \"pose\": 3, \"relativeToRest\": true, \"offsetFrom\": [0, 0], \"offsetTo\": [0, 0] } ] }");
            AssetDatabase.ImportAsset(json);
            m.poseTimeline = AssetDatabase.LoadAssetAtPath<TextAsset>(json);
            e = Assert.Throws<WeaponArtException>(() => PixelWeaponArt.Import(new WeaponArtDefinition[0], new[] { m }, "test"));
            StringAssert.Contains("keys[0].pose 3", e.Message);
            Assert.IsNull(m.viewProfile, "nothing imported");
        }

        const string Knife = "Assets/Game/Art/Weapons/Knife";

        MeleeArtDefinition MakeMelee(string id, string placement = null)
        {
            if (!AssetDatabase.IsValidFolder(Temp))
                AssetDatabase.CreateFolder("Assets", "TmpWeaponArtTest");
            var melee = ScriptableObject.CreateInstance<MeleeDefinition>();
            melee.weaponId = id;
            AssetDatabase.CreateAsset(melee, $"{Temp}/{id}_melee.asset");
            var d = ScriptableObject.CreateInstance<MeleeArtDefinition>();
            d.melee = melee;
            d.baseProfile = AssetDatabase.LoadAssetAtPath<MeleeViewProfile>("Assets/Game/Data/MeleeView_ProtoKnife.asset");
            Assert.IsTrue(AssetDatabase.CopyAsset($"{Knife}/knife_candidate.png", $"{Temp}/{id}_msource.png"));
            Assert.IsTrue(AssetDatabase.CopyAsset($"{Knife}/knife_candidate_opaque.png", $"{Temp}/{id}_mimage.png"));
            string json = $"{Temp}/{id}_m.placement.json";
            File.WriteAllText(json, placement ?? "{ \"grip\": [560, 730], \"screenHeightPixels\": 1300 }");
            AssetDatabase.ImportAsset(json);
            d.art.source = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Temp}/{id}_msource.png");
            d.art.image = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Temp}/{id}_mimage.png");
            d.art.placement = AssetDatabase.LoadAssetAtPath<TextAsset>(json);
            AssetDatabase.CreateAsset(d, $"{Temp}/{id}_meleeart.asset");
            return d;
        }

        [Test]
        public void Import_LeavesSourcesImagesAndPlacementsUntouched()
        {
            string[] files =
            {
                $"{Rifle}/rifle_hip_approved.png", $"{Rifle}/rifle_ads_approved.png",
                $"{Rifle}/rifle_hip_approved_opaque.png", $"{Rifle}/rifle_ads_approved_opaque.png",
                $"{Rifle}/rifle_hip_approved.placement.json", $"{Rifle}/rifle_ads_approved.placement.json",
                $"{Knife}/knife_candidate.png", $"{Knife}/knife_candidate_opaque.png", $"{Knife}/knife_candidate.placement.json",
                $"{Knife}/Poses/knife_pose1_windup_v3.png", $"{Knife}/Poses/knife_pose1_windup_v3_gloves_opaque.png", $"{Knife}/Poses/knife_pose1.placement.json",
                $"{Knife}/Poses/knife_pose2_center_pass_extended.png", $"{Knife}/Poses/knife_pose2_center_pass_extended_opaque.png", $"{Knife}/Poses/knife_pose2.placement.json",
                $"{Knife}/Poses/knife_pose3_follow_through.png", $"{Knife}/Poses/knife_pose3_follow_through_gloves_opaque.png", $"{Knife}/Poses/knife_pose3.placement.json",
                $"{Knife}/Poses/knife_poses.timeline.json",
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
