using System.Collections;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Game;
using Deadlock.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deadlock.Tests
{
    /// <summary>
    /// Stage 31 in a running scene (training ground: normal reserve, ammo boxes): a shotgun pull fires eight projectiles
    /// for one round and reloads its whole magazine at once; a handgun in Slot 2 keeps its own rounds next to the rifle;
    /// an ammo box gives each carried gun one magazine and stays when every reserve is full. The guns here are runtime
    /// clones of the rifle (its art) with the values under test — the provisional assets are checked in EditMode.
    /// </summary>
    public class WeaponFoundationPlayTests : FiringRangeTestBase
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

        IEnumerator LoadTraining()
        {
            yield return LoadScene(InputMode.Desktop, GameScenes.Training);
            foreach (var z in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
                z.Active = false;
        }

        WeaponDefinition CloneOfRifle(System.Action<WeaponDefinition> set)
        {
            var d = Object.Instantiate(gun.Definition);
            clones.Add(d);
            set(d);
            return d;
        }

        [UnityTest]
        public IEnumerator Shotgun_EightProjectilesOneRound_EachResolvedOnItsOwn_WholeMagazineReload()
        {
            yield return LoadTraining();
            var shotgun = CloneOfRifle(d =>
            {
                d.weaponId = "test_shotgun";
                d.category = WeaponCategory.Shotgun;
                d.projectilesPerShot = 8;
                d.automatic = false;
                d.fireInterval = 0.3f;
                d.magazineSize = 6;
                d.startReserve = 12;
                d.reserveMax = 18;
                d.reloadTime = 0.6f;
                d.hipSpreadDegrees = 6f;
            });
            gun.SetDefinition(shotgun);
            yield return null;
            Assert.AreEqual(6, gun.Ammo.Loaded);
            Assert.AreEqual(12, gun.Ammo.Reserve);

            var spawned = new HashSet<int>();
            var resolved = new HashSet<int>();
            projectiles.Spawned += (id, _) => spawned.Add(id);
            projectiles.Hit += h => resolved.Add(h.ProjectileId);
            projectiles.Expired += id => resolved.Add(id);

            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(8, spawned.Count, "eight projectiles for one pull");
            Assert.AreEqual(5, gun.Ammo.Loaded, "one round used");
            yield return Wait(1.5f);
            Assert.AreEqual(0, projectiles.ActiveCount);
            CollectionAssert.IsSubsetOf(spawned, resolved, "every projectile hit or ended on its own");

            yield return Wait(0.35f);
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(16, spawned.Count);
            Assert.AreEqual(4, gun.Ammo.Loaded);

            Assert.IsTrue(gun.TryStartReload());
            yield return Wait(0.3f);
            Assert.AreEqual(4, gun.Ammo.Loaded, "no shell-by-shell loading during the reload");
            yield return Wait(0.5f);
            Assert.IsFalse(gun.IsReloading);
            Assert.AreEqual(6, gun.Ammo.Loaded, "the whole magazine at once");
            Assert.AreEqual(10, gun.Ammo.Reserve);
        }

        [UnityTest]
        public IEnumerator HandgunInSlot2_OwnRounds_AmmoBoxGivesEachGunAMagazine_StaysWhenFull()
        {
            yield return LoadTraining();
            var rifle = gun.Definition;
            Assert.AreEqual(LoadoutSlot.Primary, gun.ActiveSlot);
            Assert.AreEqual(60, gun.Ammo.Reserve, "rifle: two magazines");
            var handgun = CloneOfRifle(d =>
            {
                d.weaponId = "test_handgun";
                d.category = WeaponCategory.Handgun;
                d.automatic = false;
                d.magazineSize = 12;
                d.startReserve = 24;
                d.reserveMax = 36;
            });
            Assert.IsFalse(gun.EquipGun(LoadoutSlot.Primary, handgun), "a handgun never goes in Slot 1");
            Assert.IsFalse(gun.EquipGun(LoadoutSlot.Secondary, rifle), "the rifle never goes in Slot 2");
            Assert.IsTrue(gun.EquipGun(LoadoutSlot.Secondary, handgun));
            Assert.AreSame(rifle, gun.Definition, "equipping Slot 2 does not change the gun in hand");

            Press(mouse.leftButton, queueEventOnly: true);
            yield return Wait(0.35f);
            Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            int rifleLoaded = gun.Ammo.Loaded;
            Assert.Less(rifleLoaded, 30);

            Assert.IsTrue(gun.SelectGun(LoadoutSlot.Secondary));
            yield return null;
            Assert.AreSame(handgun, gun.Definition);
            Assert.AreEqual(12, gun.Ammo.Loaded, "the handgun's own magazine");
            Assert.AreEqual(24, gun.Ammo.Reserve);
            yield return Tap(mouse.leftButton);
            yield return null;
            Assert.AreEqual(11, gun.Ammo.Loaded);

            Assert.IsTrue(gun.SelectGun(LoadoutSlot.Primary));
            yield return null;
            Assert.AreSame(rifle, gun.Definition);
            Assert.AreEqual(rifleLoaded, gun.Ammo.Loaded, "the rifle's rounds are as it left them");
            Assert.AreEqual(60, gun.Ammo.Reserve);

            // The knife switch still works around it.
            yield return Tap(keyboard.digit2Key);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(WeaponSlot.Melee, weapons.Current);
            yield return Tap(keyboard.digit1Key);
            yield return Wait(weapons.SwitchTime + 0.3f);
            Assert.AreEqual(WeaponSlot.Gun, weapons.Current);
            Assert.AreSame(rifle, gun.Definition);

            // One box: one magazine to each gun's reserve, capped; magazines untouched.
            var boxA = GameObject.Find("AmmoPickup_A").GetComponent<AmmoPickup>();
            Teleport(boxA.transform.position);
            yield return Wait(0.3f);
            Assert.IsTrue(boxA.IsConsumed);
            Assert.AreEqual(90, gun.Inventory.Ammo(LoadoutSlot.Primary).Reserve, "rifle 60 + 30 (cap 90)");
            Assert.AreEqual(36, gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve, "handgun 24 + 12 (cap 36)");
            Assert.AreEqual(rifleLoaded, gun.Inventory.Ammo(LoadoutSlot.Primary).Loaded, "not into the magazine");
            Assert.AreEqual(11, gun.Inventory.Ammo(LoadoutSlot.Secondary).Loaded);

            // Every reserve full: the next box stays.
            var boxB = GameObject.Find("AmmoPickup_B").GetComponent<AmmoPickup>();
            Teleport(boxB.transform.position);
            yield return Wait(0.3f);
            Assert.IsFalse(boxB.IsConsumed, "nothing to add: the box stays");
            Assert.AreEqual(90, gun.Inventory.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(36, gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve);

            // A respawn / retry gives every gun a fresh state.
            gun.ResetState();
            Assert.AreEqual(60, gun.Inventory.Ammo(LoadoutSlot.Primary).Reserve);
            Assert.AreEqual(24, gun.Inventory.Ammo(LoadoutSlot.Secondary).Reserve);
            Assert.AreEqual(12, gun.Inventory.Ammo(LoadoutSlot.Secondary).Loaded);
        }
    }
}
