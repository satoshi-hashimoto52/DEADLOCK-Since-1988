using System;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.View;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deadlock.Network
{
    /// <summary>
    /// A zombie in the online PvPvE check. The host runs the same <see cref="Zombie"/> as training (NavMesh movement,
    /// target choice, attacks, HP, stagger, death) with all living players as targets; clients never run the AI or
    /// any damage. Position, facing, behaviour state, hits and death are synced; each machine turns the billboard
    /// sprite to its own camera (the display rotation is not synced).
    /// </summary>
    public sealed class NetworkZombie : NetworkBehaviour, IZombieStatus
    {
        public struct Snapshot : INetworkSerializable, IEquatable<Snapshot>
        {
            public Vector3 Position;
            public float Yaw;
            public byte State;
            /// <summary>Counts hits taken (clients flash on change).</summary>
            public byte HitSeq;
            /// <summary>Owner client id of the current target (ulong.MaxValue: none). For checks and debugging.</summary>
            public ulong Target;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Position);
                serializer.SerializeValue(ref Yaw);
                serializer.SerializeValue(ref State);
                serializer.SerializeValue(ref HitSeq);
                serializer.SerializeValue(ref Target);
            }

            public bool Equals(Snapshot o) => (Position - o.Position).sqrMagnitude < 0.0001f && Mathf.Abs(Mathf.DeltaAngle(Yaw, o.Yaw)) < 0.5f
                                              && State == o.State && HitSeq == o.HitSeq && Target == o.Target;
            public override bool Equals(object obj) => obj is Snapshot o && Equals(o);
            public override int GetHashCode() => HashCode.Combine(State, HitSeq, Target);
        }

        [SerializeField] Zombie zombie;
        [SerializeField] NavMeshAgent agent;
        [SerializeField] ZombieView view;
        [SerializeField, Min(1f)] float smoothing = 15f;

        public readonly NetworkVariable<Snapshot> Synced = new NetworkVariable<Snapshot>();
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(100f);

        /// <summary>Zombies currently spawned on this machine.</summary>
        public static readonly List<NetworkZombie> All = new List<NetworkZombie>();

        Collider[] colliders;
        byte hitSeq;
        float lastDamaged = -10f;
        Vector3 velocity;
        Vector3 lastPosition;
        bool initialised;

        public Zombie Brain => zombie;
        public event Action<NetworkZombie> Died;

        public ZombieState State => (ZombieState)Synced.Value.State;
        public bool IsDead => State == ZombieState.Dead;
        public Vector3 Velocity => IsServer ? zombie.Velocity : velocity;
        public float LastDamagedTime => IsServer ? zombie.LastDamagedTime : lastDamaged;
        public ulong TargetClientId => Synced.Value.Target;

        void Awake() => colliders = GetComponentsInChildren<Collider>();

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            view.Source = this;
            if (IsServer)
            {
                // The AI and damage live on the host only.
                agent.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.Warp(hit.position);
                zombie.Targets = new AvatarTargets();
                zombie.Died += OnBrainDied;
                zombie.enabled = true;
                Health.Value = zombie.MaxHealth;
                Write();
            }
            else
            {
                zombie.enabled = false;
                agent.enabled = false;
                Synced.OnValueChanged += OnSnapshot;
                Apply(Synced.Value, snap: true);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (IsServer)
                zombie.Died -= OnBrainDied;
            else
                Synced.OnValueChanged -= OnSnapshot;
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        void OnBrainDied(Zombie z)
        {
            Write();
            Died?.Invoke(this);
        }

        sealed class AvatarTargets : IZombieTargetSource
        {
            public void GetTargets(List<IZombieTarget> into)
            {
                foreach (var a in NetworkAvatar.All)
                    if (a.IsSpawned)
                        into.Add(a.ZombieTarget);
            }
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            if (IsServer)
            {
                if (zombie.LastDamagedTime > lastDamaged)
                {
                    lastDamaged = zombie.LastDamagedTime;
                    hitSeq++;
                }
                Write();
                return;
            }
            // Client: follow the host's state smoothly.
            var s = Synced.Value;
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            Vector3 before = transform.position;
            transform.SetPositionAndRotation(Vector3.Lerp(before, s.Position, k), Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, s.Yaw, 0f), k));
            velocity = Time.deltaTime > 0f ? (transform.position - before) / Time.deltaTime : Vector3.zero;
        }

        void Write()
        {
            ulong target = ulong.MaxValue;
            foreach (var a in NetworkAvatar.All)
                if (zombie.CurrentTarget == a.ZombieTarget)
                    target = a.OwnerClientId;
            var next = new Snapshot
            {
                Position = transform.position,
                Yaw = transform.eulerAngles.y,
                State = (byte)zombie.State,
                HitSeq = hitSeq,
                Target = target,
            };
            if (!next.Equals(Synced.Value))
                Synced.Value = next;
            if (!Mathf.Approximately(Health.Value, zombie.Health))
                Health.Value = zombie.Health;
        }

        void OnSnapshot(Snapshot previous, Snapshot current) => Apply(current, snap: false);

        void Apply(Snapshot s, bool snap)
        {
            if (snap || !initialised)
            {
                transform.SetPositionAndRotation(s.Position, Quaternion.Euler(0f, s.Yaw, 0f));
                initialised = true;
            }
            if (s.HitSeq != hitSeq)
            {
                hitSeq = s.HitSeq;
                lastDamaged = Time.time;
            }
            // Dead zombies stop blocking and taking (display) hits.
            bool alive = (ZombieState)s.State != ZombieState.Dead;
            foreach (var c in colliders)
                c.enabled = alive;
        }

        /// <summary>Host: stop chasing / attacking (round over). Damage to it is still possible but irrelevant.</summary>
        public void ServerHalt()
        {
            if (IsServer)
                zombie.Active = false;
        }
    }
}
