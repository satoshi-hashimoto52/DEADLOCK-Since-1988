using UnityEngine;

namespace Deadlock.Combat
{
    /// <summary>
    /// Manually placed ammo box. Gives every carried gun one of its own magazines into reserve (D-054; never directly to
    /// the magazine) and is consumed once. Prototype rule (provisional): when every reserve is full the box stays in
    /// place; otherwise the box is consumed and any excess over each gun's reserve cap is discarded.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class AmmoPickup : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] float spinDegreesPerSecond = 90f;

        bool consumed;

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
            if (gun.ApplyAmmoBox() <= 0)
                return;

            consumed = true;
            gameObject.SetActive(false);
        }
    }
}
