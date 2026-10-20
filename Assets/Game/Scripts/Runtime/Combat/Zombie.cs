using System;
using System.Collections.Generic;
using Deadlock.Player;
using UnityEngine;
using UnityEngine.AI;

namespace Deadlock.Combat
{
    public enum ZombieState
    {
        Idle,
        Chase,
        Attack,
        Stagger,
        Dead,
    }

    /// <summary>
    /// Prototype PvE zombie (training, and on the host in the online PvPvE check): chases a living target on the
    /// NavMesh, attacks in melee with a windup, a short hit window and a recovery, staggers when hit during the windup,
    /// and dies at 0 HP. Hits are taken on its own collider (sprite display is separate). Values are provisional.
    ///
    /// Targets: the nearest living one, kept unless another is clearly closer (no flip-flopping between two players at
    /// similar distances); a target that cannot be reached is skipped for a while instead of re-planning a path to it
    /// every few frames. The target of an attack is fixed when the windup starts, and at the hit moment that same
    /// target must still be alive, in reach and in sight (switching targets mid-swing never hits someone else).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Zombie : MonoBehaviour, IDamageable, IZombieStatus
    {
        [SerializeField] PlayerHealth target;
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField, Min(0.1f)] float moveSpeed = 2.2f;
        [Tooltip("Horizontal centre-to-centre distance at which an attack starts.")]
        [SerializeField, Min(0.3f)] float attackRange = 1.4f;
        [Tooltip("Extra reach during the hit window (the player can still step out during the windup).")]
        [SerializeField, Min(0f)] float attackReachBonus = 0.3f;
        [SerializeField, Min(0f)] float attackDamage = 10f;
        [Tooltip("Telegraph before the hit window. Stepping out of reach (or behind cover) during it avoids the hit.")]
        [SerializeField, Min(0f)] float windup = 0.5f;
        [SerializeField, Min(0.01f)] float hitWindow = 0.15f;
        [SerializeField, Min(0f)] float recovery = 0.6f;
        [Tooltip("Minimum time from one attack start to the next attack start.")]
        [SerializeField, Min(0f)] float attackInterval = 1.5f;
        [Tooltip("Provisional: each hit (outside a landed attack) restarts the stagger at this duration.")]
        [SerializeField, Min(0f)] float staggerTime = 0.3f;
        [SerializeField, Min(0.05f)] float repathInterval = 0.2f;
        [Tooltip("What blocks the zombie's line of sight to the player (not other zombies).")]
        [SerializeField] LayerMask sightMask = ~0;
        [Header("Targets (several players)")]
        [Tooltip("Seconds between target re-evaluations.")]
        [SerializeField, Min(0.05f)] float retargetInterval = 0.5f;
        [Tooltip("Switch to another target only if it is this much closer (metres).")]
        [SerializeField, Min(0f)] float switchMargin = 1.5f;
        [Tooltip("A target without a complete path is skipped for this long.")]
        [SerializeField, Min(0.1f)] float unreachableBackoff = 3f;

        NavMeshAgent agent;
        Collider[] colliders;
        readonly List<IZombieTarget> candidates = new List<IZombieTarget>();
        readonly Dictionary<IZombieTarget, float> unreachableUntil = new Dictionary<IZombieTarget, float>();
        IZombieTarget current;
        IZombieTarget attackTarget;
        float nextRetarget;
        bool pathCheckPending;
        float stateStart;
        float nextRepath;
        float nextAttackTime;
        bool hitApplied;

        public ZombieState State { get; private set; } = ZombieState.Chase;
        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public float AttackInterval => attackInterval;
        public bool IsDead => State == ZombieState.Dead;
        /// <summary>0..1 through the attack (for the view).</summary>
        public float AttackProgress => State == ZombieState.Attack ? Mathf.Clamp01((Time.time - stateStart) / (windup + hitWindow + recovery)) : 0f;
        public bool InAttackWindup => State == ZombieState.Attack && Time.time - stateStart < windup;
        public float LastDamagedTime { get; private set; } = -10f;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

        public event Action<Zombie> Died;

        /// <summary>Where targets come from. Unset: the scene's player (offline training).</summary>
        public IZombieTargetSource Targets { get; set; }
        /// <summary>The target being chased now (null when there is none).</summary>
        public IZombieTarget CurrentTarget => current;
        /// <summary>The target of the attack in progress (fixed at the windup start).</summary>
        public IZombieTarget AttackTarget => State == ZombieState.Attack ? attackTarget : null;
        /// <summary>False stops chasing and attacking (e.g. the round is over). Taking damage still works.</summary>
        public bool Active { get; set; } = true;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            colliders = GetComponentsInChildren<Collider>();
            Health = maxHealth;
            agent.speed = moveSpeed;
            agent.stoppingDistance = attackRange * 0.8f;
        }

        void Start()
        {
            if (Targets == null)
            {
                if (target == null)
                    target = FindAnyObjectByType<PlayerHealth>();
                if (target != null)
                    Targets = new SinglePlayer(new PlayerZombieTarget(target));
            }
            if (agent.isOnNavMesh == false && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            Enter(ZombieState.Chase);
        }

        sealed class SinglePlayer : IZombieTargetSource
        {
            readonly IZombieTarget player;
            public SinglePlayer(IZombieTarget player) => this.player = player;
            public void GetTargets(List<IZombieTarget> into) => into.Add(player);
        }

        void Update()
        {
            if (State == ZombieState.Dead)
                return;
            UpdateTarget();
            if (!Active || current == null)
            {
                if (State != ZombieState.Idle && State != ZombieState.Attack)
                    Enter(ZombieState.Idle);
                else if (State == ZombieState.Attack && (!Active || attackTarget == null || !attackTarget.IsAlive))
                    Enter(ZombieState.Idle); // the target died / left mid-swing, or the round ended: no hit
                if (State == ZombieState.Idle)
                    return;
            }

            float elapsed = Time.time - stateStart;
            switch (State)
            {
                case ZombieState.Idle:
                    Enter(ZombieState.Chase);
                    break;
                case ZombieState.Chase:
                    if (Time.time >= nextRepath && agent.isOnNavMesh)
                    {
                        agent.SetDestination(current.Position);
                        nextRepath = Time.time + repathInterval;
                        pathCheckPending = true;
                    }
                    CheckReachable();
                    if (Time.time >= nextAttackTime && HorizontalDistance(current) <= attackRange && CanSee(current))
                    {
                        attackTarget = current; // fixed for this swing
                        Enter(ZombieState.Attack);
                    }
                    break;
                case ZombieState.Attack:
                    Face(attackTarget);
                    if (!hitApplied && elapsed >= windup && elapsed < windup + hitWindow)
                    {
                        // Re-checked at the hit moment, on the target chosen at the windup start.
                        if (attackTarget.IsAlive && HorizontalDistance(attackTarget) <= attackRange + attackReachBonus && CanSee(attackTarget))
                        {
                            hitApplied = true; // one hit per attack
                            attackTarget.TakeHit(attackDamage, transform.position);
                        }
                    }
                    if (elapsed >= windup + hitWindow + recovery)
                        Enter(ZombieState.Chase);
                    break;
                case ZombieState.Stagger:
                    if (elapsed >= staggerTime)
                        Enter(ZombieState.Chase);
                    break;
            }
        }

        /// <summary>Nearest living, reachable target; keeps the current one unless another is clearly closer.</summary>
        void UpdateTarget()
        {
            bool currentValid = current != null && current.IsAlive && !IsSkipped(current);
            if (currentValid && Time.time < nextRetarget)
                return;
            nextRetarget = Time.time + retargetInterval;
            candidates.Clear();
            Targets?.GetTargets(candidates);
            IZombieTarget best = null;
            float bestDistance = float.MaxValue;
            foreach (var t in candidates)
            {
                if (t == null || !t.IsAlive || IsSkipped(t))
                    continue;
                float d = HorizontalDistance(t);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = t;
                }
            }
            if (!currentValid || (best != null && best != current && bestDistance < HorizontalDistance(current) - switchMargin))
            {
                if (best != current)
                    nextRepath = 0f;
                current = best;
            }
        }

        bool IsSkipped(IZombieTarget t) => unreachableUntil.TryGetValue(t, out float until) && Time.time < until;

        /// <summary>A target whose path cannot be completed is skipped for a while (no endless re-planning).</summary>
        void CheckReachable()
        {
            if (!pathCheckPending || agent.pathPending || !agent.isOnNavMesh)
                return;
            pathCheckPending = false;
            if (agent.pathStatus == NavMeshPathStatus.PathComplete || current == null)
                return;
            // Already in reach (e.g. the path ends just short of a player standing on an edge): keep it.
            if (HorizontalDistance(current) <= attackRange + attackReachBonus)
                return;
            unreachableUntil[current] = Time.time + unreachableBackoff;
            current = null;
            nextRetarget = 0f;
        }

        public DamageResult ApplyDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (IsDead || amount <= 0f)
                return DamageResult.Ignored;
            Health = Mathf.Max(0f, Health - amount);
            LastDamagedTime = Time.time;
            if (Health <= 0f)
            {
                Die();
                return DamageResult.Destroyed;
            }
            // Getting hit interrupts an attack that has not landed yet.
            if (State != ZombieState.Attack || InAttackWindup)
                Enter(ZombieState.Stagger);
            return DamageResult.Damaged;
        }

        void Die()
        {
            Enter(ZombieState.Dead);
            foreach (var c in colliders)
                c.enabled = false;
            if (agent.enabled)
            {
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                agent.enabled = false;
            }
            Died?.Invoke(this);
        }

        void Enter(ZombieState state)
        {
            State = state;
            stateStart = Time.time;
            hitApplied = false;
            if (state == ZombieState.Attack)
                nextAttackTime = Time.time + attackInterval;
            if (!agent.enabled || !agent.isOnNavMesh)
                return;
            bool moving = state == ZombieState.Chase;
            agent.isStopped = !moving;
            if (!moving)
                agent.velocity = Vector3.zero;
            if (moving)
                nextRepath = 0f;
        }

        float HorizontalDistance(IZombieTarget t)
        {
            Vector3 d = t.Position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        void Face(IZombieTarget t)
        {
            if (t == null)
                return;
            Vector3 d = t.Position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>Clear line from the zombie's head to the target's body (walls and cover block attacks).</summary>
        bool CanSee(IZombieTarget t)
        {
            Vector3 from = transform.position + Vector3.up * 1.5f;
            // Aim at the middle of the target's current body (lower when crouched / prone).
            Vector3 to = t.Position + Vector3.up * (t.BodyHeight * 0.55f);
            Vector3 delta = to - from;
            if (!Physics.Raycast(from, delta, out RaycastHit hit, delta.magnitude + 0.5f, sightMask, QueryTriggerInteraction.Ignore))
                return true;
            return t.Owns(hit.collider);
        }
    }
}
