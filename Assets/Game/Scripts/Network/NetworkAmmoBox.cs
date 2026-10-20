using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Deadlock.Network
{
    /// <summary>
    /// Shared ammo box in the online PvPvE check. Only the host decides who takes it: once per frame it checks the
    /// living players in a fixed order, so two players touching it at the same moment never both get it. The rounds go
    /// to the taker's reserve on the host (same rule as offline: a full reserve leaves the box; otherwise the box is
    /// used up and any excess over the reserve cap is lost) and reach the taker through the synced ammo counts.
    /// Everyone sees the box disappear. The host's round restart puts it back.
    /// </summary>
    public sealed class NetworkAmmoBox : NetworkBehaviour
    {
        [SerializeField, Min(1)] int amount = 30;
        [SerializeField, Min(0.1f)] float pickupRadius = 0.9f;
        [SerializeField] GameObject visual;
        [SerializeField] float spinDegreesPerSecond = 90f;

        public readonly NetworkVariable<bool> Taken = new NetworkVariable<bool>();
        /// <summary>Owner client id of the player who took it (ulong.MaxValue: nobody).</summary>
        public readonly NetworkVariable<ulong> TakenBy = new NetworkVariable<ulong>(ulong.MaxValue);

        public static readonly List<NetworkAmmoBox> All = new List<NetworkAmmoBox>();

        // After a restart players are moved to their spawn points, but the host learns their new position from them a
        // moment later; a restored box waits a little so a player who stood on it is not handed it again.
        const float RestoreLockout = 1f;
        float availableAt;

        public int Amount => amount;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Taken.OnValueChanged += OnTakenChanged;
            visual.SetActive(!Taken.Value);
        }

        public override void OnNetworkDespawn()
        {
            Taken.OnValueChanged -= OnTakenChanged;
            All.Remove(this);
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        void OnTakenChanged(bool previous, bool current) => visual.SetActive(!current);

        /// <summary>Host: back on the shelf (round restart).</summary>
        public void ServerRestore()
        {
            if (!IsServer)
                return;
            Taken.Value = false;
            TakenBy.Value = ulong.MaxValue;
            availableAt = Time.time + RestoreLockout;
        }

        void Update()
        {
            if (visual.activeSelf)
                visual.transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            if (!IsSpawned || !IsServer || Taken.Value || Time.time < availableAt)
                return;
            foreach (var avatar in NetworkAvatar.All) // fixed order: one taker at most
            {
                if (!avatar.IsSpawned || avatar.Dead.Value)
                    continue;
                Vector3 d = avatar.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1.5f || new Vector2(d.x, d.z).magnitude > pickupRadius)
                    continue;
                if (avatar.ServerAddReserve(amount) <= 0)
                    continue; // reserve full: the box stays for someone else
                Taken.Value = true;
                TakenBy.Value = avatar.OwnerClientId;
                return;
            }
        }
    }
}
