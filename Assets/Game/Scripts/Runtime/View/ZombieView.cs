using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// 2D billboard display of a <see cref="Zombie"/>: two walk frames while moving, an attack frame during the
    /// attack, a short fading red tint when hit and a lying frame when dead. Hit detection stays on the zombie's collider.
    /// </summary>
    public sealed class ZombieView : MonoBehaviour
    {
        [SerializeField] Zombie zombie;
        [SerializeField] SpriteRenderer sprite;
        [SerializeField] Sprite walkA;
        [SerializeField] Sprite walkB;
        [SerializeField] Sprite attack;
        [SerializeField] Sprite dead;
        [SerializeField] float stepsPerMetre = 1.6f;
        [SerializeField] Color hitColor = new Color(1f, 0.55f, 0.5f);
        [Tooltip("Hit tint fades over this time. Rapid hits keep a steady tint instead of blinking.")]
        [SerializeField, Min(0.01f)] float hitFadeTime = 0.15f;

        float stride;

        void LateUpdate()
        {
            if (zombie.IsDead)
            {
                sprite.sprite = dead;
                sprite.color = new Color(0.75f, 0.75f, 0.75f);
                return;
            }
            Vector3 v = zombie.Velocity;
            stride += new Vector2(v.x, v.z).magnitude * Time.deltaTime * stepsPerMetre;
            sprite.sprite = zombie.State == ZombieState.Attack ? attack : (Mathf.FloorToInt(stride) % 2 == 0 ? walkA : walkB);
            float hit = 1f - Mathf.Clamp01((Time.time - zombie.LastDamagedTime) / hitFadeTime);
            sprite.color = Color.Lerp(Color.white, hitColor, hit);
        }
    }
}
