using System;
using Deadlock.Combat;
using Deadlock.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deadlock.Network
{
    /// <summary>
    /// What the owner tells everyone about its body each network tick: where it is, where it looks and what it is
    /// doing. Movement is owner-authoritative in this prototype (not validated by the host; see docs/PROGRESS.md).
    /// </summary>
    public struct AvatarState : INetworkSerializable, IEquatable<AvatarState>
    {
        [Flags]
        public enum Flag : byte
        {
            None = 0,
            Sprinting = 1,
            Grounded = 2,
            Switching = 4,
        }

        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public byte Posture;
        public byte Slot;
        public byte Flags;
        /// <summary>Counts melee swings started by the owner (the others play a swing when it changes).</summary>
        public byte SwingSeq;

        public Posture PostureValue => (Posture)Posture;
        public WeaponSlot SlotValue => (WeaponSlot)Slot;
        public bool Has(Flag flag) => (Flags & (byte)flag) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Pitch);
            serializer.SerializeValue(ref Posture);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref SwingSeq);
        }

        public bool Equals(AvatarState other) =>
            (Position - other.Position).sqrMagnitude < 0.0001f && Mathf.Abs(Mathf.DeltaAngle(Yaw, other.Yaw)) < 0.1f
            && Mathf.Abs(Pitch - other.Pitch) < 0.1f && Posture == other.Posture && Slot == other.Slot && Flags == other.Flags
            && SwingSeq == other.SwingSeq;

        public override bool Equals(object obj) => obj is AvatarState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Posture, Slot, Flags, SwingSeq);
    }
}
