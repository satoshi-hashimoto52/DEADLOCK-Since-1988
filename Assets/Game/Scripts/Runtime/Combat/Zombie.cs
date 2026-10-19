using System;
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
    /// Training zombie (prototype PvE enemy): chases the player on the NavMesh, attacks in melee with a windup,
    /// a short hit window and a recovery, staggers when hit during the windup, and dies at 0 HP.
    /// Hits are taken on its own collider (sprite display is separate). Values are provisional.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Zombie : MonoBehaviour, IDamageable
    {
        [SerializeField] PlayerHealth target;
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField, Min(0.1f)] float moveSpeed = 2.2f;
        [Tooltip("Horizontal centre-to-centre distance at which an attack starts.")]
        [SerializeField, Min(0.3f)] float attackRange = 1.4f;
        [Tooltip("Extra reach during the hit window (the player can still step out during the windup).")]
        [SerializeField, Min(0f)] float attackReachBonus = 0.3f;
        [SerializeField, Min(0f)] float attackDamage = 20f;
        [SerializeField, Min(0f)] float windup = 0.55f;
        [SerializeField, Min(0.01f)] float hitWindow = 0.15f;
        [SerializeField, Min(0f)] float recovery = 0.6f;
        [SerializeField, Min(0f)] float staggerTime = 0.3f;
        [SerializeField, Min(0.05f)] float repathInterval = 0.2f;
        [Tooltip("What blocks the zombie's line of sight to the player (not other zombies).")]
        [SerializeField] LayerMask sightMask = ~0;

        NavMeshAgent agent;
        CharacterController targetBody;
        Collider[] colliders;
        float stateStart;
        float nextRepath;
        bool hitApplied;

        public ZombieState State { get; private set; } = ZombieState.Chase;
        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead => State == ZombieState.Dead;
        /// <summary>0..1 through the attack (for the view).</summary>
        public float AttackProgress => State == ZombieState.Attack ? Mathf.Clamp01((Time.time - stateStart) / (windup + hitWindow + recovery)) : 0f;
        public bool InAttackWindup => State == ZombieState.Attack && Time.time - stateStart < windup;
        public float LastDamagedTime { get; private set; } = -10f;
        public Vector3 Velocity => agent != null && agent.enabled ? agent.velocity : Vector3.zero;

        public event Action<Zombie> Died;

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
            if (target == null)
                target = FindAnyObjectByType<PlayerHealth>();
            if (target != null)
                targetBody = target.GetComponent<CharacterController>();
            if (agent.isOnNavMesh == false && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            Enter(ZombieState.Chase);
        }

        void Update()
        {
            if (State == ZombieState.Dead)
                return;
            if (target == null || target.IsDead)
            {
                if (State != ZombieState.Idle)
                    Enter(ZombieState.Idle);
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
                        agent.SetDestination(target.transform.position);
                        nextRepath = Time.time + repathInterval;
                    }
                    if (HorizontalDistance() <= attackRange && CanSeeTarget())
                        Enter(ZombieState.Attack);
                    break;
                case ZombieState.Attack:
                    FaceTarget();
                    if (!hitApplied && elapsed >= windup && elapsed < windup + hitWindow)
                    {
                        if (HorizontalDistance() <= attackRange + attackReachBonus && CanSeeTarget())
                        {
                            hitApplied = true; // one hit per attack
                            target.TakeDamage(attackDamage, transform.position);
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
            if (!agent.enabled || !agent.isOnNavMesh)
                return;
            bool moving = state == ZombieState.Chase;
            agent.isStopped = !moving;
            if (!moving)
                agent.velocity = Vector3.zero;
            if (moving)
                nextRepath = 0f;
        }

        float HorizontalDistance()
        {
            Vector3 d = target.transform.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        void FaceTarget()
        {
            Vector3 d = target.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>Clear line from the zombie's head to the player's body (walls and cover block attacks).</summary>
        bool CanSeeTarget()
        {
            Vector3 from = transform.position + Vector3.up * 1.5f;
            // Aim at the middle of the player's current body (lower when crouched / prone).
            float bodyHeight = targetBody != null ? targetBody.height : 1.8f;
            Vector3 to = target.transform.position + Vector3.up * (bodyHeight * 0.55f);
            Vector3 delta = to - from;
            if (!Physics.Raycast(from, delta, out RaycastHit hit, delta.magnitude + 0.5f, sightMask, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.GetComponentInParent<PlayerHealth>() == target;
        }
    }
}
