using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadlock.Network
{
    public enum RoundPhase : byte
    {
        /// <summary>Normal online check (player vs player, free respawn).</summary>
        Off,
        Running,
        Ended,
    }

    public enum RoundResult : byte
    {
        None,
        /// <summary>All zombies defeated.</summary>
        Cleared,
        /// <summary>No player left alive (wins over Cleared when both happen in the same frame).</summary>
        AllDead,
    }

    /// <summary>
    /// PvPvE verification round (test rules only, not the DEADLINE / event rules): with both players connected the host
    /// starts it; players are reset to the spawn points, 3 host-run zombies and 2 shared ammo boxes appear and joining
    /// is closed. Players can still hurt each other. The round ends when all zombies are dead or no player is alive;
    /// if both happen in the same frame, "no player alive" wins. The host decides the result once and everyone sees
    /// the zombies left and who is alive. No rankings or rewards. Free respawn is off from the start until the host
    /// ends PvPvE; the host's restart resets players, zombies, ammo and boxes.
    /// </summary>
    public sealed class PvPvERound : NetworkBehaviour
    {
        public const int ZombieCount = 3;

        public static PvPvERound Instance { get; private set; }

        public readonly NetworkVariable<RoundPhase> PhaseVar = new NetworkVariable<RoundPhase>(RoundPhase.Off);
        public readonly NetworkVariable<RoundResult> ResultVar = new NetworkVariable<RoundResult>(RoundResult.None);
        public readonly NetworkVariable<int> ZombiesAlive = new NetworkVariable<int>();
        public readonly NetworkVariable<int> RoundNumber = new NetworkVariable<int>();
        public readonly NetworkVariable<FixedString512Bytes> Summary = new NetworkVariable<FixedString512Bytes>();

        readonly List<NetworkZombie> zombies = new List<NetworkZombie>();
        readonly List<NetworkAmmoBox> boxes = new List<NetworkAmmoBox>();
        NetworkSession session;

        public RoundPhase Phase => PhaseVar.Value;
        public RoundResult Result => ResultVar.Value;
        /// <summary>The decided result: zombies left and who is alive (empty while running).</summary>
        public string SummaryText => Summary.Value.ToString();
        /// <summary>After the round ended nobody takes damage any more.</summary>
        public bool DamageBlocked => Phase == RoundPhase.Ended;
        /// <summary>Host: how many times a result was decided (exactly once per round).</summary>
        public int ServerResultsDecided { get; private set; }

        public event Action<RoundResult> Finished;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            session = NetworkSession.Instance;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
                Instance = null;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        // ---------------------------------------------------------------- host commands

        /// <summary>Host: start (or restart) a round. Needs both players connected.</summary>
        public bool ServerStart(out string message)
        {
            message = "";
            if (!IsServer)
            {
                message = "ホストのみ操作できます";
                return false;
            }
            if (NetworkAvatar.All.Count < NetworkSession.MaxPlayers)
            {
                message = "2人そろってから開始してください";
                return false;
            }
            ClearZombies();
            // Players back to the spawn points, fresh.
            int i = 0;
            foreach (var avatar in NetworkAvatar.All)
            {
                var (position, yaw) = session.SpawnPointFor(i++);
                avatar.ServerResetForRound(position, yaw);
            }
            foreach (var spawn in session.ZombieSpawns)
            {
                var go = Instantiate(session.ZombiePrefab, spawn.position, spawn.rotation);
                go.GetComponent<NetworkObject>().Spawn(true);
                zombies.Add(go.GetComponent<NetworkZombie>());
            }
            if (boxes.Count == 0)
            {
                foreach (var spawn in session.AmmoBoxSpawns)
                {
                    var go = Instantiate(session.AmmoBoxPrefab, spawn.position, spawn.rotation);
                    go.GetComponent<NetworkObject>().Spawn(true);
                    boxes.Add(go.GetComponent<NetworkAmmoBox>());
                }
            }
            else
            {
                foreach (var box in boxes)
                    box.ServerRestore();
            }
            ZombiesAlive.Value = zombies.Count;
            ResultVar.Value = RoundResult.None;
            Summary.Value = default;
            RoundNumber.Value++;
            PhaseVar.Value = RoundPhase.Running;
            return true;
        }

        /// <summary>Host: back to the normal online check (zombies and boxes removed, free respawn again).</summary>
        public void ServerStop()
        {
            if (!IsServer)
                return;
            ClearZombies();
            foreach (var box in boxes)
                if (box != null && box.IsSpawned)
                    box.NetworkObject.Despawn();
            boxes.Clear();
            ResultVar.Value = RoundResult.None;
            Summary.Value = default;
            PhaseVar.Value = RoundPhase.Off;
        }

        void ClearZombies()
        {
            foreach (var z in zombies)
                if (z != null && z.IsSpawned)
                    z.NetworkObject.Despawn();
            zombies.Clear();
        }

        // ---------------------------------------------------------------- host: end of round

        // Once per frame, after everything that can kill (zombie attacks, projectiles) has run.
        void LateUpdate()
        {
            if (!IsSpawned || !IsServer || Phase != RoundPhase.Running)
                return;
            int zombiesAlive = 0;
            foreach (var z in zombies)
                if (z != null && !z.Brain.IsDead)
                    zombiesAlive++;
            int playersAlive = 0;
            foreach (var a in NetworkAvatar.All)
                if (a.IsSpawned && !a.Dead.Value)
                    playersAlive++;
            if (ZombiesAlive.Value != zombiesAlive)
                ZombiesAlive.Value = zombiesAlive;
            if (playersAlive == 0)
                End(RoundResult.AllDead, zombiesAlive);
            else if (zombiesAlive == 0)
                End(RoundResult.Cleared, zombiesAlive);
        }

        void End(RoundResult result, int zombiesAlive)
        {
            ServerResultsDecided++;
            foreach (var z in zombies)
                if (z != null)
                    z.ServerHalt();
            var text = new StringBuilder();
            text.Append(result == RoundResult.Cleared ? "クリア（ゾンビ全滅）" : "全滅（生存プレイヤー0人）");
            text.Append($"　ゾンビ残り {zombiesAlive} / {zombies.Count}");
            foreach (var a in NetworkAvatar.All)
                text.Append($"　{(a.OwnerClientId == NetworkManager.ServerClientId ? "ホスト" : "参加者")}：{(a.Dead.Value ? "死亡" : "生存")}");
            Summary.Value = new FixedString512Bytes(text.ToString());
            ResultVar.Value = result;
            PhaseVar.Value = RoundPhase.Ended;
            Finished?.Invoke(result);
        }
    }
}
