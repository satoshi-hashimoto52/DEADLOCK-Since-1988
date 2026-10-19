using System;
using Deadlock.Input;
using UnityEngine;

namespace Deadlock.Player
{
    /// <summary>
    /// Player hit points (prototype). Damage comes from enemies only; the player's own projectiles never hit the
    /// Player layer. On death player input is blocked. Values are provisional.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [SerializeField] PlayerInputRouter input;

        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead { get; private set; }

        /// <summary>Amount and the attacker's position.</summary>
        public event Action<float, Vector3> Damaged;
        public event Action Died;

        void Awake()
        {
            Health = maxHealth;
        }

        public void TakeDamage(float amount, Vector3 sourcePosition)
        {
            if (IsDead || amount <= 0f)
                return;
            Health = Mathf.Max(0f, Health - amount);
            Damaged?.Invoke(amount, sourcePosition);
            if (Health > 0f)
                return;
            IsDead = true;
            if (input != null)
                input.Block("dead");
            Died?.Invoke();
        }
    }
}
