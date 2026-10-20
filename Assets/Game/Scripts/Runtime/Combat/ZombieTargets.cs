using System.Collections.Generic;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>Something a zombie can chase and hit (the offline player, or an online player on the host).</summary>
    public interface IZombieTarget
    {
        Vector3 Position { get; }
        bool IsAlive { get; }
        /// <summary>Current body height (lower when crouched / prone); the zombie aims its sight at the middle.</summary>
        float BodyHeight { get; }
        /// <summary>The collider is part of this target (a sight line reaching it is not blocked).</summary>
        bool Owns(Collider collider);
        void TakeHit(float damage, Vector3 from);
    }

    /// <summary>Supplies the zombie's possible targets (living or not; the zombie filters).</summary>
    public interface IZombieTargetSource
    {
        void GetTargets(List<IZombieTarget> into);
    }

    /// <summary>Read-only zombie state for the display (the zombie itself offline / on the host, a mirror online).</summary>
    public interface IZombieStatus
    {
        ZombieState State { get; }
        bool IsDead { get; }
        Vector3 Velocity { get; }
        float LastDamagedTime { get; }
    }

    /// <summary>The offline player as a zombie target.</summary>
    public sealed class PlayerZombieTarget : IZombieTarget
    {
        readonly PlayerHealth health;
        readonly CharacterController body;

        public PlayerZombieTarget(PlayerHealth health)
        {
            this.health = health;
            body = health.GetComponent<CharacterController>();
        }

        public PlayerHealth Health => health;
        public Vector3 Position => health.transform.position;
        public bool IsAlive => health != null && !health.IsDead;
        public float BodyHeight => body != null ? body.height : 1.8f;
        public bool Owns(Collider collider) => collider.GetComponentInParent<PlayerHealth>() == health;
        public void TakeHit(float damage, Vector3 from) => health.TakeDamage(damage, from);
    }
}
