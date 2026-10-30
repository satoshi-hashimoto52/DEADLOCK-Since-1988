using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Firing range rule (placed in Proto_FiringRange only): the gun's reserve ammo never runs out, so shooting and
    /// reloading go on without ammo boxes. The magazine size, reloading and its time stay as the weapon defines them.
    /// Lives in the scene: leaving the range takes the rule away with it, and no other mode has it.
    /// </summary>
    public sealed class FiringRangeAmmo : MonoBehaviour
    {
        [SerializeField] GunController gun;

        public GunController Gun => gun;

        void OnEnable()
        {
            if (gun != null)
                gun.InfiniteReserve = true;
        }

        void OnDisable()
        {
            if (gun != null)
                gun.InfiniteReserve = false;
        }
    }
}
