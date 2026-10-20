using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Deadlock.Combat;
using Deadlock.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Deadlock.Network
{
    public enum SessionState
    {
        Offline,
        Connecting,
        Hosting,
        Connected,
    }

    /// <summary>
    /// LAN test session of the online scene: host (one player hosts, the host is also a player) or join by IP,
    /// two players at most, leave. Shows why a connection failed or ended and always returns to the offline lobby.
    /// Owns no gameplay state itself (see <see cref="NetworkAvatar"/>). Verification setup only, not the production
    /// server design. Netcode for GameObjects + Unity Transport (UDP), direct IP, no relay or cloud service.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class NetworkSession : MonoBehaviour
    {
        public const int MaxPlayers = 2;
        public const ushort DefaultPort = 7777;
        public const string HitboxLayerName = "Hitbox";
        public const string OwnHitboxLayerName = "OwnHitbox";

        [SerializeField] NetworkManager manager;
        [SerializeField] UnityTransport transport;
        [SerializeField] PlayerRig localRig;
        [Tooltip("Host-side simulation of everyone's shots (applies damage, draws nothing).")]
        [SerializeField] ProjectileSystem serverProjectiles;
        [Tooltip("Display-only shots on this machine (no damage).")]
        [SerializeField] ProjectileSystem visualProjectiles;
        [SerializeField] Transform[] spawnPoints;
        [Tooltip("What shots and swings can hit (world and hit volumes, not the local movement capsule).")]
        [SerializeField] LayerMask hitMask = ~0;
        [Tooltip("Solid world only (no players): used to stop shot origins behind walls.")]
        [SerializeField] LayerMask worldMask = ~0;
        [SerializeField] ushort port = DefaultPort;

        public static NetworkSession Instance { get; private set; }
        public static int HitboxLayer => LayerMask.NameToLayer(HitboxLayerName);
        public static int OwnHitboxLayer => LayerMask.NameToLayer(OwnHitboxLayerName);

        public SessionState State { get; private set; } = SessionState.Offline;
        /// <summary>Why the last attempt or session ended (shown in the lobby). Empty when nothing went wrong.</summary>
        public string LastMessage { get; private set; } = "";
        public string HostAddress { get; private set; } = "";
        public ushort Port => port;
        public PlayerRig LocalRig => localRig;
        public ProjectileSystem ServerProjectiles => serverProjectiles;
        public ProjectileSystem VisualProjectiles => visualProjectiles;
        public LayerMask HitMask => hitMask;
        public LayerMask WorldMask => worldMask;
        public NetworkManager Manager => manager;
        public bool IsActive => State != SessionState.Offline;
        /// <summary>A previous session is still shutting down (it finishes on the next network update).</summary>
        public bool IsBusy => manager != null && (manager.ShutdownInProgress || manager.IsListening) && State == SessionState.Offline;
        public int PlayerCount => manager != null && manager.IsServer ? manager.ConnectedClientsIds.Count : NetworkAvatar.All.Count;

        public event Action Changed;

        void Awake()
        {
            Instance = this;
            // When this scene replaces another online scene, the previous manager is destroyed only at the end of
            // the frame and would still be the global instance: make this scene's manager the one in use.
            manager.SetSingleton();
            int player = LayerMask.NameToLayer("Player");
            // The local movement capsule never collides with its own hit volumes.
            if (player >= 0 && OwnHitboxLayer >= 0)
                Physics.IgnoreLayerCollision(player, OwnHitboxLayer, true);
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
            manager.ConnectionApprovalCallback = ApproveConnection;
            // Fail a connection attempt after about 8 s instead of the default minute.
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 8;
        }

        void OnDestroy()
        {
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                manager.OnTransportFailure -= OnTransportFailure;
                manager.ConnectionApprovalCallback = null;
                // The NetworkManager moves itself to DontDestroyOnLoad: end it with this scene.
                if (manager.IsListening)
                    manager.Shutdown(true);
                Destroy(manager.gameObject);
            }
            if (Instance == this)
                Instance = null;
        }

        // ---------------------------------------------------------------- commands

        public bool StartHost()
        {
            if (IsActive || IsBusy)
                return false;
            LastMessage = "";
            transport.SetConnectionData(true, "127.0.0.1", port, "0.0.0.0");
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(NetworkAvatar.ProtocolVersion);
            if (!manager.StartHost())
            {
                Fail($"ホストを開始できませんでした（ポート{port}が使用中の可能性）");
                return false;
            }
            HostAddress = LocalAddresses().Count > 0 ? LocalAddresses()[0] : "127.0.0.1";
            SetState(SessionState.Hosting);
            return true;
        }

        public bool Join(string address)
        {
            if (IsActive || IsBusy)
                return false;
            LastMessage = "";
            address = (address ?? "").Trim();
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                Fail("IPアドレスの形式が正しくありません（例：192.168.0.10）");
                return false;
            }
            transport.SetConnectionData(true, address, port);
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(NetworkAvatar.ProtocolVersion);
            HostAddress = address;
            if (!manager.StartClient())
            {
                Fail("接続を開始できませんでした");
                return false;
            }
            SetState(SessionState.Connecting);
            return true;
        }

        /// <summary>Leaves (client) or ends the session for everyone (host). Always back to the lobby.</summary>
        public void Leave()
        {
            if (!IsActive)
                return;
            bool wasHost = State == SessionState.Hosting;
            manager.Shutdown();
            EndSession(wasHost ? "ホストを終了しました" : "退出しました");
        }

        // ---------------------------------------------------------------- host

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            string version = request.Payload != null ? Encoding.UTF8.GetString(request.Payload) : "";
            int connected = manager.ConnectedClientsIds.Count;
            response.Pending = false;
            if (version != NetworkAvatar.ProtocolVersion)
            {
                response.Approved = false;
                response.Reason = "ホストとバージョンが違います";
                return;
            }
            if (connected >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = $"満員です（{MaxPlayers}人まで）";
                return;
            }
            var (position, yaw) = SpawnPoint(connected);
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = position;
            response.Rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        (Vector3, float) SpawnPoint(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                return (Vector3.zero, 0f);
            var t = spawnPoints[Mathf.Abs(index) % spawnPoints.Length];
            return (t.position, t.eulerAngles.y);
        }

        /// <summary>Host: the spawn point farthest from the other living players.</summary>
        public (Vector3, float) PickRespawn(NetworkAvatar avatar)
        {
            int best = 0;
            float bestDistance = -1f;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                float nearest = float.MaxValue;
                foreach (var other in NetworkAvatar.All)
                    if (other != avatar && !other.Dead.Value)
                        nearest = Mathf.Min(nearest, Vector3.Distance(other.transform.position, spawnPoints[i].position));
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = i;
                }
            }
            return SpawnPoint(best);
        }

        // ---------------------------------------------------------------- callbacks

        void OnClientConnected(ulong clientId)
        {
            if (State == SessionState.Connecting && clientId == manager.LocalClientId)
                SetState(SessionState.Connected);
            else
                Changed?.Invoke();
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (State == SessionState.Offline)
                return; // we already ended it (left / host ended); the deferred shutdown reports it again
            if (manager.IsServer)
            {
                if (clientId != manager.LocalClientId)
                {
                    LastMessage = "相手が退出しました";
                    Changed?.Invoke();
                }
                return;
            }
            // This client: refused, failed to connect, or lost the host.
            string reason = manager.DisconnectReason ?? "";
            string message;
            if (State == SessionState.Connected)
                message = reason.Contains("shutting down") ? "ホストが終了しました" : "ホストとの接続が切れました（ホストの終了または通信の途絶）";
            else if (!string.IsNullOrEmpty(reason))
                message = $"接続を拒否されました：{reason}";
            else
                message = $"接続できませんでした（{HostAddress}:{port}）。ホストが開始済みか、同じWi-Fiか、IPアドレス、ローカルネットワークの許可を確認してください";
            if (manager.IsListening)
                manager.Shutdown();
            EndSession(message);
        }

        void OnTransportFailure()
        {
            if (State == SessionState.Offline)
                return;
            if (manager.IsListening)
                manager.Shutdown();
            EndSession("通信エラーが発生しました");
        }

        void EndSession(string message)
        {
            LastMessage = message;
            ResetLocalPlayer();
            SetState(SessionState.Offline);
        }

        void Fail(string message)
        {
            LastMessage = message;
            SetState(SessionState.Offline);
        }

        void ResetLocalPlayer()
        {
            if (localRig == null)
                return;
            var (position, yaw) = SpawnPoint(0);
            localRig.ResetAt(position, yaw);
        }

        void SetState(SessionState state)
        {
            State = state;
            Changed?.Invoke();
        }

        /// <summary>IPv4 addresses of this device on the local network (what the other player types in).</summary>
        public static List<string> LocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        string text = a.Address.ToString();
                        if (!text.StartsWith("169.254.") && !result.Contains(text))
                            result.Add(text);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DEADLOCK][Net] Could not list network interfaces: {e.Message}");
            }
            // Private LAN ranges first.
            result.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return result;
        }

        static int Rank(string address) =>
            address.StartsWith("192.168.") ? 0 : address.StartsWith("10.") ? 1 : address.StartsWith("172.") ? 2 : 3;
    }
}
