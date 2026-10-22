using System;
using Deadlock.Combat;
using UnityEngine;

namespace Deadlock.View
{
    /// <summary>
    /// 2D billboard display of a <see cref="Zombie"/>: two walk frames while moving, an attack frame during the
    /// attack, a short fading red tint when hit and a lying frame when dead. Hit detection stays on the zombie's collider.
    /// Each change of walk frame is one footstep (<see cref="Stepped"/>): there is no footstep sound yet, so the walk
    /// cycle is the footstep event (the floor map hears it).
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
        [Tooltip("Below this horizontal speed (m/s) a walk frame change is not a footstep (pushed, turning in place).")]
        [SerializeField, Min(0f)] float minStepSpeed = 0.3f;

        float stride;
        int step;

        /// <summary>A footstep: the walk frame changed while walking (not while attacking, standing or dead).</summary>
        public event Action<ZombieView> Stepped;

        /// <summary>Where the displayed state comes from (default: the zombie; online clients use a synced mirror).</summary>
        public IZombieStatus Source { get; set; }

        void LateUpdate()
        {
            var z = Source ?? zombie;
            if (z.IsDead)
            {
                sprite.sprite = dead;
                sprite.color = new Color(0.75f, 0.75f, 0.75f);
                return;
            }
            Vector3 v = z.Velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            stride += speed * Time.deltaTime * stepsPerMetre;
            int frame = Mathf.FloorToInt(stride);
            if (frame != step)
            {
                step = frame;
                if (speed >= minStepSpeed && z.State != ZombieState.Attack)
                    Stepped?.Invoke(this);
            }
            sprite.sprite = z.State == ZombieState.Attack ? attack : (Mathf.FloorToInt(stride) % 2 == 0 ? walkA : walkB);
            float hit = 1f - Mathf.Clamp01((Time.time - z.LastDamagedTime) / hitFadeTime);
            sprite.color = Color.Lerp(Color.white, hitColor, hit);
        }
    }
}
