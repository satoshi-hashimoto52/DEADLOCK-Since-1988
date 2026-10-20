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

        /// <summary>
        /// Online: the host owns the hit points. Applies its value here so the HUD, damage flash and death handling
        /// behave as offline. A higher value than the current one (respawn) revives.
        /// </summary>
        public void SetAuthoritative(float health, Vector3 sourcePosition)
        {
            health = Mathf.Clamp(health, 0f, maxHealth);
            if (health > Health || (IsDead && health > 0f))
            {
                Revive(health);
                return;
            }
            if (health < Health)
                TakeDamage(Health - health, sourcePosition);
        }

        /// <summary>Back to life (respawn) with the given hit points.</summary>
        public void Revive(float health)
        {
            Health = Mathf.Clamp(health, 1f, maxHealth);
            if (!IsDead)
                return;
            IsDead = false;
            if (input != null)
                input.Unblock("dead");
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
