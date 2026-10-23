using UnityEngine;

namespace Deadlock.Game
{
    /// <summary>
    /// Escape point of the ruins test field (test-only provisional rule, not a game mode: DECISIONS D-006 keeps
    /// extraction out of the real modes). Standing on it lets the player press "脱出する"; the
    /// <see cref="TrainingSession"/> decides the result. Only says whether the player is on it: no collider, so it never
    /// blocks shots, sight or footsteps.
    /// </summary>
    public sealed class ExtractionPoint : MonoBehaviour
    {
        [Tooltip("The player's feet.")]
        [SerializeField] Transform player;
        [Tooltip("Horizontal radius of the point (m).")]
        [SerializeField, Min(0.5f)] float radius = 1.8f;
        [Tooltip("The feet must be within this height of the point (m): the floor above / below does not count.")]
        [SerializeField, Min(0.1f)] float heightTolerance = 1f;

        public Vector3 Position => transform.position;
        public float Radius => radius;

        public bool PlayerInside => player != null && Contains(player.position);

        public bool Contains(Vector3 feet)
        {
            Vector3 d = feet - transform.position;
            return Mathf.Abs(d.y) <= heightTolerance && d.x * d.x + d.z * d.z <= radius * radius;
        }
    }
}
