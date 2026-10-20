using Deadlock.Combat;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Network
{
    /// <summary>
    /// Provisional hit volumes of an online player, shaped per posture: a body capsule and a head sphere. Separate from
    /// the movement capsule; in particular prone is a body lying along the facing direction, not the short upright
    /// movement capsule. Sizes are prototype values (docs/PROGRESS.md), not the final hit model.
    /// Damage is forwarded to the owning <see cref="NetworkAvatar"/>, which only accepts it on the host.
    /// </summary>
    public sealed class AvatarHitboxes : MonoBehaviour, IDamageable
    {
        [SerializeField] NetworkAvatar avatar;
        [SerializeField] CapsuleCollider body;
        [SerializeField] SphereCollider head;

        Posture applied = (Posture)255;
        bool appliedAlive = true;

        public CapsuleCollider Body => body;
        public SphereCollider Head => head;

        /// <summary>Eye height for a posture (matches the local player's camera heights).</summary>
        public static float EyeHeight(Posture posture) => posture switch
        {
            Posture.Crouching => 1.05f,
            Posture.Prone => 0.45f,
            Posture.Sliding => 0.85f,
            _ => 1.6f,
        };

        public void SetLayer(int layer)
        {
            gameObject.layer = layer;
            body.gameObject.layer = layer;
            head.gameObject.layer = layer;
        }

        public void Apply(Posture posture, bool alive)
        {
            if (posture == applied && alive == appliedAlive)
                return;
            applied = posture;
            appliedAlive = alive;
            body.enabled = head.enabled = alive;
            switch (posture)
            {
                case Posture.Crouching:
                    Shape(new Vector3(0f, 0.5f, 0f), 1.0f, 0.3f, 1, new Vector3(0f, 1.05f, 0.05f));
                    break;
                case Posture.Prone:
                    // Lying along the facing direction: head forward, legs behind.
                    Shape(new Vector3(0f, 0.22f, -0.55f), 1.5f, 0.22f, 2, new Vector3(0f, 0.32f, 0.3f));
                    break;
                case Posture.Sliding:
                    // Leaning back, legs forward.
                    Shape(new Vector3(0f, 0.4f, 0.2f), 1.2f, 0.28f, 2, new Vector3(0f, 0.85f, -0.15f));
                    break;
                default:
                    Shape(new Vector3(0f, 0.75f, 0f), 1.5f, 0.3f, 1, new Vector3(0f, 1.62f, 0f));
                    break;
            }
        }

        void Shape(Vector3 bodyCenter, float bodyHeight, float bodyRadius, int direction, Vector3 headCenter)
        {
            body.center = bodyCenter;
            body.height = bodyHeight;
            body.radius = bodyRadius;
            body.direction = direction;
            head.center = headCenter;
            head.radius = 0.17f;
        }

        public DamageResult ApplyDamage(float amount, Vector3 point, Vector3 direction) =>
            avatar != null ? avatar.ServerTakeDamage(amount, point) : DamageResult.Ignored;
    }
}
