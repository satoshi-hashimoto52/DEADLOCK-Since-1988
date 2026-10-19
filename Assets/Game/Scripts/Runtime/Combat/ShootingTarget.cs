using System;
using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Static practice target. The hit collider lives on this object; the visible sprite is a separate child
    /// so display and hit detection stay independent (GAME_SPEC 11.2).
    /// </summary>
    public sealed class ShootingTarget : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [Tooltip("Seconds until the destroyed target comes back. 0 or less keeps it destroyed.")]
        [SerializeField] float respawnDelay = 3f;
        [SerializeField] SpriteRenderer sprite;
        [SerializeField] Color hitFlashColor = new Color(1f, 0.35f, 0.25f);

        Collider[] colliders;
        float health;
        float respawnAt = -1f;
        float flashUntil;

        public float Health => health;
        public float MaxHealth => maxHealth;
        public bool IsDestroyed => health <= 0f;

        public event Action<ShootingTarget> Destroyed;

        void Awake()
        {
            colliders = GetComponentsInChildren<Collider>();
            health = maxHealth;
        }

        public DamageResult ApplyDamage(float amount, Vector3 point, Vector3 direction)
        {
            if (IsDestroyed || amount <= 0f)
                return DamageResult.Ignored;

            health = Mathf.Max(0f, health - amount);
            flashUntil = Time.time + 0.08f;

            if (!IsDestroyed)
                return DamageResult.Damaged;

            SetAlive(false);
            if (respawnDelay > 0f)
                respawnAt = Time.time + respawnDelay;
            Destroyed?.Invoke(this);
            return DamageResult.Destroyed;
        }

        void Update()
        {
            if (respawnAt >= 0f && Time.time >= respawnAt)
            {
                respawnAt = -1f;
                health = maxHealth;
                SetAlive(true);
            }

            if (sprite != null)
                sprite.color = Time.time < flashUntil ? hitFlashColor : Color.white;
        }

        void SetAlive(bool alive)
        {
            foreach (var c in colliders)
                c.enabled = alive;
            if (sprite != null)
                sprite.enabled = alive;
        }
    }
}
