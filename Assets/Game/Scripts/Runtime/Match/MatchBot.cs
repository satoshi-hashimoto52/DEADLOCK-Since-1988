using System;
using Deadlock.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deadlock.Match
{
    /// <summary>
    /// The body of an NPC place in the offline normal match (Stage 35), also what a human's character becomes after a
    /// takeover (same place, position, facing, health, weapon, skin and rounds). A simple prototype opponent: it walks
    /// toward the nearest living participant on the NavMesh, stops when it sees one in range, turns to it and fires
    /// single shots with spread (a ray; the damage goes through the arena so the session keeps every record), reloads
    /// from its own reserve. Nothing while combat is not allowed (countdown, result). All values are provisional.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class MatchBot : MonoBehaviour, IDamageable
    {
        /// <summary>Seconds between shots as a multiple of the weapon's own (an NPC fires slower than a player). Provisional.</summary>
        public static float CadenceFactor = 5f;
        public static float SpreadDegrees = 4f;
        public static float Range = 45f;
        public static float EyeHeight = 1.5f;
        /// <summary>Checks only: NPCs never fire (outcomes decided by the check, not by random shots).</summary>
        public static bool HoldFire;

        MatchArena arena;
        NavMeshAgent agent;
        LineRenderer tracer;
        float nextFire, nextThink, reloadEnd, tracerOff;
        bool reloading;
        Transform target;
        int targetSlot = -1;

        public int SlotId { get; private set; }
        public WeaponDefinition Weapon { get; private set; }
        public string SkinId { get; private set; }
        public AmmoState Ammo { get; private set; }
        public bool TookOverHuman { get; private set; }
        /// <summary>Look up / down of the last aim (degrees, positive up), for the replay.</summary>
        public float AimPitch { get; private set; }
        /// <summary>A shot (origin, direction): recorded for the delayed spectating (sound and tracer).</summary>
        public event Action<MatchBot, Vector3, Vector3> Fired;
        public event Action<MatchBot> ReloadStarted;
        public Vector3 Eye => transform.position + Vector3.up * EyeHeight;

        public static MatchBot Create(MatchArena arena, int slotId, string name, Vector3 position, float yaw, WeaponDefinition weapon, AmmoState ammo,
            string skinId, int layer, Color color, bool tookOverHuman)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.4f;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.speed = 3f;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.enabled = false;
            // Placed after the agent is switched off: an agent snaps the body onto the NavMesh when it is added.
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var look = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            look.name = "Body";
            UnityEngine.Object.Destroy(look.GetComponent<Collider>());
            look.transform.SetParent(go.transform, false);
            look.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            look.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            look.layer = layer; // the spectator's camera leaves the live bodies out (it shows their past instead)
            var renderer = look.GetComponent<MeshRenderer>();
            renderer.material.color = color;
            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Facing";
            UnityEngine.Object.Destroy(visor.GetComponent<Collider>());
            visor.transform.SetParent(go.transform, false);
            visor.transform.localPosition = new Vector3(0f, 1.5f, 0.35f);
            visor.transform.localScale = new Vector3(0.5f, 0.15f, 0.15f);
            visor.layer = layer;
            visor.GetComponent<MeshRenderer>().material.color = Color.black;

            var bot = go.AddComponent<MatchBot>();
            bot.arena = arena;
            bot.agent = agent;
            bot.SlotId = slotId;
            bot.Weapon = weapon;
            bot.SkinId = skinId;
            bot.Ammo = ammo ?? new AmmoState(weapon.magazineSize, weapon.reserveMax, weapon.magazineSize, weapon.startReserve);
            bot.TookOverHuman = tookOverHuman;
            bot.tracer = go.AddComponent<LineRenderer>();
            bot.tracer.positionCount = 2;
            bot.tracer.startWidth = bot.tracer.endWidth = 0.03f;
            bot.tracer.material = new Material(renderer.material) { color = new Color(1f, 0.85f, 0.4f) };
            bot.tracer.enabled = false;
            return bot;
        }

        void Start()
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
            {
                agent.enabled = true;
                agent.Warp(hit.position);
            }
        }

        public DamageResult ApplyDamage(float amount, Vector3 point, Vector3 direction) =>
            arena != null ? arena.DamageFromLocalPlayer(this, amount) : DamageResult.Ignored;

        /// <summary>The place died: it stops and falls over (the record stays in the session).</summary>
        public void Die()
        {
            if (agent.enabled)
                agent.enabled = false;
            transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            foreach (var c in GetComponents<Collider>())
                c.enabled = false;
            tracer.enabled = false;
            enabled = false;
        }

        void Update()
        {
            if (tracer.enabled && Time.time >= tracerOff)
                tracer.enabled = false;
            if (arena == null || !arena.CombatAllowed || !arena.IsAlive(SlotId))
            {
                if (agent.enabled && agent.isOnNavMesh)
                    agent.isStopped = true;
                return;
            }
            if (reloading && Time.time >= reloadEnd)
            {
                reloading = false;
                Ammo.Reload();
            }
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.25f;
                target = arena.NearestOpponent(this, out targetSlot);
            }
            if (target == null)
                return;
            Vector3 aimPoint = target.position + Vector3.up * 1.2f;
            bool sees = arena.Sees(Eye, aimPoint, target) && Vector3.Distance(Eye, aimPoint) <= Range;
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = sees;
                if (!sees && Time.time >= nextThink - 0.2f)
                    agent.SetDestination(target.position);
            }
            if (!sees)
                return;
            Vector3 flat = aimPoint - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 540f * Time.deltaTime);
            if (Time.time < nextFire || reloading || HoldFire)
                return;
            if (Ammo.Loaded <= 0)
            {
                if (Ammo.Reserve > 0)
                {
                    reloading = true;
                    reloadEnd = Time.time + Weapon.reloadTime;
                    ReloadStarted?.Invoke(this);
                }
                return;
            }
            nextFire = Time.time + Weapon.fireInterval * CadenceFactor;
            Ammo.TryConsume();
            Vector3 aim = (aimPoint - Eye).normalized;
            AimPitch = Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 dir = Quaternion.Euler(UnityEngine.Random.Range(-SpreadDegrees, SpreadDegrees), UnityEngine.Random.Range(-SpreadDegrees, SpreadDegrees), 0f) * aim;
            Fired?.Invoke(this, Eye, dir);
            Vector3 end = Eye + dir * Range;
            if (Physics.Raycast(Eye, dir, out var hit, Range, arena.ShotMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                arena.ShotHit(this, hit.collider, Weapon.damage);
            }
            tracer.SetPosition(0, Eye + transform.forward * 0.4f);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerOff = Time.time + 0.05f;
        }
    }
}
