using UnityEngine;

namespace Deadlock.Combat
{
    public enum DamageResult
    {
        Ignored,
        Damaged,
        Destroyed,
    }

    public interface IDamageable
    {
        DamageResult ApplyDamage(float amount, Vector3 point, Vector3 direction);
    }
}
