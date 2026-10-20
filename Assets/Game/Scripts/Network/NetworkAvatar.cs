using System;
using System.Collections.Generic;
using Deadlock.Combat;
using Deadlock.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deadlock.Network
{
    /// <summary>
    /// One player in the online prototype (spawned by the host for each connection, owned by that player).
    ///
    /// Owner: mirrors its local player (position, look, posture, sprint/jump/slide, weapon) into <see cref="State"/> and
    /// turns local shots / swings / reloads / switches into requests to the host. Its own model is hidden, and it never
    /// gets a camera, controls or audio listener (those exist only on the local player).
    ///
    /// Host: owns hit points, ammo, fire timing, damage, death and respawn. Every request is re-checked with an
    /// <see cref="AuthoritativeWeaponState"/>; clients never send hit or damage values. Projectiles keep their travel
    /// speed and are simulated by the host's projectile system against the posture hit volumes.
    /// </summary>
    public sealed class NetworkAvatar : NetworkBehaviour
    {
        /// <summary>Client and host must match (sent in the connection request).</summary>
        public const string ProtocolVersion = "deadlock-net-proto-1";
        const float MaxOriginOffset = 1.5f;

        [SerializeField] WeaponDefinition gunDefinition;
        [SerializeField] MeleeDefinition meleeDefinition;
        [SerializeField, Min(0.01f)] float switchTime = 0.35f;
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField] AvatarHitboxes hitboxes;
        [SerializeField] AvatarView view;

        public readonly NetworkVariable<AvatarState> State = new NetworkVariable<AvatarState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Health = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<int> Loaded = new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner);
        public readonly NetworkVariable<int> Reserve = new NetworkVariable<int>(0, NetworkVariableReadPermission.Owner);

        /// <summary>Avatars currently spawned on this machine.</summary>
        public static readonly List<NetworkAvatar> All = new List<NetworkAvatar>();
        /// <summary>How many avatars are bound to the local player right now (must never exceed 1).</summary>
        public static int LocalBindings { get; private set; }

        NetworkSession session;
        PlayerRig rig;
        AuthoritativeWeaponState weapons;
        float swingStart = -10f;
        bool swingPending;
        byte swingSeq;
        bool stateReceived;

        bool dummy;

        /// <summary>
        /// Host-owned stand-in player that does not drive the local player (tests; later the place for host-side bots).
        /// Call before spawning. Its <see cref="State"/> is written directly by the host.
        /// </summary>
        public void MarkAsDummy() => dummy = true;

        /// <summary>This avatar is the local player of this machine.</summary>
        public bool IsLocalPlayerAvatar => IsOwner && !dummy;

        public AvatarHitboxes Hitboxes => hitboxes;
        public AvatarView View => view;
        public float MaxHealth => maxHealth;
        public bool IsBoundToLocalPlayer => rig != null;
        /// <summary>Host only: the authoritative weapon state of this player.</summary>
        public AuthoritativeWeaponState ServerWeapons => weapons;

        // Diagnostics (owner side unless noted).
        public int ShotsRequested { get; private set; }
        public int ShotsRejected { get; private set; }
        public ShotRejection LastRejection { get; private set; }
        /// <summary>Host side: shots accepted for this player.</summary>
        public int ServerShotsAccepted { get; private set; }

        public event Action<NetworkAvatar> Respawned;

        public Vector3 EyePosition
        {
            get
            {
                var s = State.Value;
                return s.Position + Vector3.up * AvatarHitboxes.EyeHeight(s.PostureValue);
            }
        }

        public Vector3 AimDirection
        {
            get
            {
                var s = State.Value;
                return Quaternion.Euler(-s.Pitch, s.Yaw, 0f) * Vector3.forward;
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            session = NetworkSession.Instance;
            // Until the owner's first state arrives, stay at the spawn point (not at the default origin).
            stateReceived = IsOwner || dummy;
            State.OnValueChanged += OnStateChanged;
            if (IsServer)
            {
                weapons = new AuthoritativeWeaponState(gunDefinition, meleeDefinition, switchTime);
                Health.Value = maxHealth;
                Dead.Value = false;
                SyncAmmo();
            }
            hitboxes.SetLayer(IsLocalPlayerAvatar ? NetworkSession.OwnHitboxLayer : NetworkSession.HitboxLayer);
            view.Init(this, IsLocalPlayerAvatar);
            if (IsLocalPlayerAvatar)
                BindLocalPlayer();
        }

        void OnStateChanged(AvatarState previous, AvatarState current) => stateReceived = true;

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= OnStateChanged;
            UnbindLocalPlayer();
            All.Remove(this);
        }

        public override void OnDestroy()
        {
            // Destroyed without a despawn (shutdown): never leave the local player wired to a dead object.
            UnbindLocalPlayer();
            All.Remove(this);
            base.OnDestroy();
        }

        // ---------------------------------------------------------------- owner: mirror the local player

        void BindLocalPlayer()
        {
            if (rig != null)
                return;
            rig = session != null ? session.LocalRig : FindAnyObjectByType<PlayerRig>();
            if (rig == null)
                return;
            LocalBindings++;
            rig.ResetAt(transform.position, transform.eulerAngles.y);
            rig.Gun.SetAmmo(Loaded.Value, Reserve.Value);
            rig.Gun.ShotOverride = OnLocalShot;
            rig.Gun.ReloadStarted += OnLocalReload;
            rig.Melee.LocalHitDetection = false;
            rig.Melee.SwingStarted += OnLocalSwing;
            rig.Weapons.SwitchStarted += OnLocalSwitch;
            Health.OnValueChanged += OnHealthChanged;
            Loaded.OnValueChanged += OnAmmoChanged;
            Reserve.OnValueChanged += OnAmmoChanged;
            WriteState();
        }

        void UnbindLocalPlayer()
        {
            if (rig == null)
                return;
            LocalBindings--;
            rig.Gun.ShotOverride = null;
            rig.Gun.ReloadStarted -= OnLocalReload;
            rig.Melee.LocalHitDetection = true;
            rig.Melee.SwingStarted -= OnLocalSwing;
            rig.Weapons.SwitchStarted -= OnLocalSwitch;
            Health.OnValueChanged -= OnHealthChanged;
            Loaded.OnValueChanged -= OnAmmoChanged;
            Reserve.OnValueChanged -= OnAmmoChanged;
            rig = null;
        }

        void OnLocalShot(Vector3 origin, Vector3 direction)
        {
            ShotsRequested++;
            // Drawn at once on this machine (display only); the host decides whether it happened and what it hit.
            if (session != null)
                session.VisualProjectiles.Fire(origin, direction, gunDefinition.projectileSpeed, gunDefinition.maxRange, 0f, gameObject);
            FireRpc(origin, direction);
        }

        void OnLocalReload() => ReloadRpc();
        void OnLocalSwitch(WeaponSlot slot) => SwitchRpc((byte)slot);

        void OnLocalSwing()
        {
            swingSeq++;
            SwingRpc();
        }

        void OnHealthChanged(float previous, float current)
        {
            if (rig != null)
                rig.Health.SetAuthoritative(current, transform.position);
        }

        void OnAmmoChanged(int previous, int current)
        {
            if (rig != null)
                rig.Gun.SetAmmo(Loaded.Value, Reserve.Value);
        }

        void WriteState()
        {
            if (rig == null)
                return;
            var motor = rig.Motor;
            var flags = AvatarState.Flag.None;
            if (motor.IsSprinting) flags |= AvatarState.Flag.Sprinting;
            if (motor.IsGrounded) flags |= AvatarState.Flag.Grounded;
            if (rig.Weapons.IsSwitching) flags |= AvatarState.Flag.Switching;
            if (rig.Gun.AdsRequested) flags |= AvatarState.Flag.Aiming;
            var next = new AvatarState
            {
                Position = rig.transform.position,
                Yaw = motor.Yaw,
                Pitch = motor.Pitch,
                Posture = (byte)rig.Posture.Current,
                Slot = (byte)rig.Weapons.Current,
                Flags = (byte)flags,
                SwingSeq = swingSeq,
            };
            if (!next.Equals(State.Value))
                State.Value = next;
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            if (IsOwner)
                WriteState();
            var s = State.Value;
            if (stateReceived)
                transform.SetPositionAndRotation(s.Position, Quaternion.Euler(0f, s.Yaw, 0f));
            hitboxes.Apply(s.PostureValue, !Dead.Value);
            if (IsServer)
                ServerUpdate();
        }

        // ---------------------------------------------------------------- requests (owner → host)

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 origin, Vector3 direction) => ServerFire(origin, direction);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ReloadRpc()
        {
            if (weapons.TryStartReload(Time.time))
                SyncAmmo();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SwitchRpc(byte slot)
        {
            if (slot <= (byte)WeaponSlot.Melee)
                weapons.TrySwitch((WeaponSlot)slot, Time.time);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SwingRpc()
        {
            if (!weapons.TrySwing(Time.time))
                return;
            swingStart = Time.time;
            swingPending = true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RespawnRpc() => ServerRespawn();

        /// <summary>
        /// Test hook: sends a fire request straight to the host, skipping every local check (ammo, reload, death,
        /// fire rate), as a modified client could. The host must still refuse what is not allowed.
        /// </summary>
        public void SendUncheckedFireRequest(Vector3 origin, Vector3 direction)
        {
            if (IsOwner)
                FireRpc(origin, direction);
        }

        // ---------------------------------------------------------------- host

        /// <summary>Host: one shot request from this player. Damage comes from the host's weapon definition.</summary>
        public void ServerFire(Vector3 origin, Vector3 direction)
        {
            if (!IsServer)
                return;
            weapons.IsDead = Dead.Value;
            ShotRejection rejection = weapons.TryFire(Time.time);
            if (rejection != ShotRejection.None)
            {
                ShotRejectedRpc((byte)rejection);
                return;
            }
            ServerShotsAccepted++;
            SyncAmmo();

            // The shot starts at the eye the host knows about; a nearby reported origin is accepted (it is more
            // up to date), but never one far away or behind a wall.
            Vector3 eye = EyePosition;
            if (!IsFinite(origin) || (origin - eye).magnitude > MaxOriginOffset
                || Physics.Linecast(eye, origin, session.WorldMask, QueryTriggerInteraction.Ignore))
                origin = eye;
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
                direction = AimDirection;
            direction.Normalize();

            session.ServerProjectiles.Fire(origin, direction, gunDefinition.projectileSpeed, gunDefinition.maxRange, gunDefinition.damage, gameObject);
            ShotRpc(origin, direction);
        }

        /// <summary>Host: damage from a host-simulated projectile or swing.</summary>
        public DamageResult ServerTakeDamage(float amount, Vector3 point)
        {
            if (!IsServer || Dead.Value || amount <= 0f)
                return DamageResult.Ignored;
            float next = Mathf.Max(0f, Health.Value - amount);
            Health.Value = next;
            if (next > 0f)
                return DamageResult.Damaged;
            Dead.Value = true;
            weapons.IsDead = true;
            swingPending = false;
            return DamageResult.Destroyed;
        }

        public void ServerRespawn()
        {
            if (!IsServer || !Dead.Value)
                return;
            weapons.Reset();
            Health.Value = maxHealth;
            Dead.Value = false;
            swingPending = false;
            SyncAmmo();
            var (position, yaw) = session.PickRespawn(this);
            RespawnedRpc(position, yaw);
        }

        void ServerUpdate()
        {
            weapons.Tick(Time.time);
            if (Loaded.Value != weapons.Ammo.Loaded || Reserve.Value != weapons.Ammo.Reserve)
                SyncAmmo();
            if (swingPending)
                ServerSwingStep();
        }

        /// <summary>Host: runs the knife check during the active window, from where this player is looking now.</summary>
        void ServerSwingStep()
        {
            float elapsed = Time.time - swingStart;
            if (elapsed < meleeDefinition.windup)
                return;
            if (Dead.Value)
            {
                swingPending = false;
                return;
            }
            var result = MeleeSweep.Run(EyePosition, AimDirection, meleeDefinition, session.HitMask, transform);
            // One check per frame through the active window; a window that fell between frames still gets one.
            if (result.Resolved || elapsed >= meleeDefinition.windup + meleeDefinition.activeTime)
                swingPending = false;
        }

        void SyncAmmo()
        {
            Loaded.Value = weapons.Ammo.Loaded;
            Reserve.Value = weapons.Ammo.Reserve;
        }

        static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                                              || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // ---------------------------------------------------------------- host → clients

        /// <summary>An accepted shot: everyone but its owner (who drew it already) draws it; display only.</summary>
        [Rpc(SendTo.Everyone)]
        void ShotRpc(Vector3 origin, Vector3 direction)
        {
            view.MuzzleFlash();
            if (!IsOwner && session != null)
                session.VisualProjectiles.Fire(origin, direction, gunDefinition.projectileSpeed, gunDefinition.maxRange, 0f, gameObject);
        }

        [Rpc(SendTo.Owner)]
        void ShotRejectedRpc(byte reason)
        {
            ShotsRejected++;
            LastRejection = (ShotRejection)reason;
            Debug.Log($"[DEADLOCK][Net] Shot refused by host: {LastRejection}");
        }

        [Rpc(SendTo.Owner)]
        void RespawnedRpc(Vector3 position, float yaw)
        {
            if (rig != null)
            {
                rig.ResetAt(position, yaw);
                rig.Gun.SetAmmo(Loaded.Value, Reserve.Value);
                WriteState();
            }
            Respawned?.Invoke(this);
        }
    }
}
