using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Manually placed ammo box. Adds rounds to the gun's reserve (never directly to the magazine) and is consumed once.
    /// Prototype rule (provisional): a full reserve leaves the box in place; otherwise the box is consumed
    /// and any excess over the reserve cap is discarded.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class AmmoPickup : MonoBehaviour
    {
        [SerializeField, Min(1)] int amount = 30;
        [SerializeField] Transform visual;
        [SerializeField] float spinDegreesPerSecond = 90f;

        bool consumed;

        public int Amount => amount;
        public bool IsConsumed => consumed;

        void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        void Update()
        {
            if (visual != null)
                visual.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
        }

        void OnTriggerEnter(Collider other) => TryCollect(other);

        void OnTriggerStay(Collider other) => TryCollect(other);

        void TryCollect(Collider other)
        {
            if (consumed)
                return;
            var gun = other.GetComponentInParent<GunController>();
            if (gun == null)
                return;
            if (gun.AddReserveAmmo(amount) <= 0)
                return;

            consumed = true;
            gameObject.SetActive(false);
        }
    }
}
