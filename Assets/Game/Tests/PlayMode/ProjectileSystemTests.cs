using System.Collections.Generic;
using Deadlock.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Deadlock.Tests
{
    /// <summary>Ballistics in isolation. Steps are driven manually so results do not depend on frame rate.</summary>
    public class ProjectileSystemTests
    {
        // Far away from anything a previously loaded test scene may have left behind.
        static readonly Vector3 Origin = new Vector3(0f, 5000f, 0f);

        readonly List<GameObject> spawned = new List<GameObject>();
        ProjectileSystem system;
        readonly List<ProjectileHit> hits = new List<ProjectileHit>();

        [SetUp]
        public void SetUp()
        {
            var go = Track(new GameObject("Projectiles"));
            go.SetActive(false); // no Update(): the test owns the clock
            system = go.AddComponent<ProjectileSystem>();
            system.Hit += hits.Add;
            hits.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void Hit_HappensAfterFlightTime_NotAtFireTime()
        {
            var target = CreateTarget(Origin + new Vector3(0f, 0f, 50f), thickness: 0.3f);
            system.Fire(Origin, Vector3.forward, speed: 100f, maxRange: 200f, damage: 10f, shooter: null);

            system.Step(0.25f); // 25 m travelled
            Assert.AreEqual(target.MaxHealth, target.Health);
            Assert.AreEqual(1, system.ActiveCount);

            system.Step(0.3f); // 55 m travelled
            Assert.Less(target.Health, target.MaxHealth);
            Assert.AreEqual(0, system.ActiveCount);
        }

        [Test]
        public void FastProjectile_DoesNotTunnelThroughThinTarget()
        {
            var target = CreateTarget(Origin + new Vector3(0f, 0f, 30f), thickness: 0.02f);
            system.Fire(Origin, Vector3.forward, speed: 3000f, maxRange: 500f, damage: 10f, shooter: null);

            system.Step(0.1f); // one 300 m step straight through the 2 cm target

            Assert.Less(target.Health, target.MaxHealth);
        }

        [Test]
        public void Wall_StopsProjectile_BeforeTarget()
        {
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = Origin + new Vector3(0f, 0f, 10f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
            var target = CreateTarget(Origin + new Vector3(0f, 0f, 20f), thickness: 0.3f);
            Physics.SyncTransforms();

            system.Fire(Origin, Vector3.forward, speed: 400f, maxRange: 200f, damage: 10f, shooter: null);
            for (int i = 0; i < 10; i++) system.Step(0.05f);

            Assert.AreEqual(target.MaxHealth, target.Health);
            Assert.AreEqual(1, hits.Count);
            Assert.AreSame(wall.GetComponent<Collider>(), hits[0].Collider);
        }

        [Test]
        public void Projectile_ExpiresAtMaxRange()
        {
            var target = CreateTarget(Origin + new Vector3(0f, 0f, 30f), thickness: 0.3f);
            system.Fire(Origin, Vector3.forward, speed: 100f, maxRange: 20f, damage: 10f, shooter: null);

            for (int i = 0; i < 10; i++) system.Step(0.1f);

            Assert.AreEqual(0, system.ActiveCount);
            Assert.AreEqual(target.MaxHealth, target.Health);
        }

        [Test]
        public void Target_IsDestroyedWhenHealthReachesZero()
        {
            var target = CreateTarget(Origin + new Vector3(0f, 0f, 5f), thickness: 0.3f);
            int shots = Mathf.CeilToInt(target.MaxHealth / 25f);
            for (int i = 0; i < shots; i++)
            {
                system.Fire(Origin, Vector3.forward, 500f, 100f, 25f, null);
                system.Step(0.05f);
            }

            Assert.IsTrue(target.IsDestroyed);
            Assert.AreEqual(DamageResult.Destroyed, hits[hits.Count - 1].Result);
            Assert.IsFalse(target.GetComponent<Collider>().enabled, "A destroyed target must not keep absorbing shots.");
        }

        ShootingTarget CreateTarget(Vector3 position, float thickness)
        {
            var go = Track(new GameObject("Target"));
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 2f, thickness);
            var target = go.AddComponent<ShootingTarget>();
            Physics.SyncTransforms();
            return target;
        }

        GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }
    }
}
