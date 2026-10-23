using System;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Map;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Game
{
    public enum TrainingResult
    {
        Playing,
        Cleared,
        Failed,
        /// <summary>Escaped from the ruins test field's escape point (survived).</summary>
        Escaped,
    }

    /// <summary>
    /// Training round: all zombies dead = cleared, player dead = failed. The result stops player input; retrying
    /// reloads the scene so every zombie, the player, weapons, posture and UI start over.
    /// This is a prototype of the training mode, not the finished mode.
    /// With an <see cref="ExtractionPoint"/> (ruins test field, test-only provisional rule): defeating every zombie
    /// does not end the round; standing on the point and pressing "脱出する" (<see cref="RequestEscape"/>) is a
    /// survival. Death on the same frame wins over the escape. On any result the zombies stop (no more damage) and the
    /// floor map's detection is switched off (no marker or footstep cue stays).
    /// </summary>
    public sealed class TrainingSession : MonoBehaviour
    {
        [SerializeField] PlayerHealth player;
        [SerializeField] PlayerInputRouter input;
        [SerializeField] Zombie[] zombies = Array.Empty<Zombie>();
        [Tooltip("Optional (ruins test field): the escape point. With it, defeating every zombie does not end the round.")]
        [SerializeField] ExtractionPoint extraction;
        [Tooltip("Optional: the floor map's detection, switched off when the round ends.")]
        [SerializeField] EnemyDetector detector;

        bool escapeRequested;

        public TrainingResult Result { get; private set; } = TrainingResult.Playing;
        public float ElapsedTime { get; private set; }
        public int Remaining { get; private set; }
        public int Defeated => Total - Remaining;
        public int Total => zombies.Length;
        public ExtractionPoint Extraction => extraction;
        /// <summary>"脱出する" can be pressed now: on the escape point, alive, round going, input not blocked (menu).</summary>
        public bool CanEscape => extraction != null && Result == TrainingResult.Playing && !player.IsDead && !input.IsBlocked && extraction.PlayerInside;

        public event Action<TrainingResult> Finished;

        void OnEnable()
        {
            player.Died += OnPlayerDied;
            foreach (var z in zombies)
                z.Died += OnZombieDied;
        }

        void OnDisable()
        {
            player.Died -= OnPlayerDied;
            foreach (var z in zombies)
                if (z != null)
                    z.Died -= OnZombieDied;
        }

        void Start() => Remaining = CountAlive();

        void Update()
        {
            if (Result == TrainingResult.Playing)
                ElapsedTime += Time.deltaTime;
        }

        void LateUpdate()
        {
            if (Result != TrainingResult.Playing)
                return;
            // Resolve once per frame so simultaneous deaths cannot show two different outcomes.
            if (player.IsDead)
                Finish(TrainingResult.Failed);
            else if (escapeRequested)
                Finish(TrainingResult.Escaped);
            else if (extraction == null && Remaining == 0)
                Finish(TrainingResult.Cleared);
            escapeRequested = false;
        }

        int CountAlive()
        {
            int alive = 0;
            foreach (var z in zombies)
                if (z != null && !z.IsDead)
                    alive++;
            return alive;
        }

        void OnZombieDied(Zombie zombie)
        {
            Remaining = CountAlive();
        }

        void OnPlayerDied()
        {
            // LateUpdate resolves the result after all same-frame damage/death events have arrived.
        }

        /// <summary>
        /// "脱出する": accepted only while <see cref="CanEscape"/>; the result is decided at the end of the frame, so a
        /// death on the same frame wins.
        /// </summary>
        public bool RequestEscape()
        {
            if (!CanEscape)
                return false;
            escapeRequested = true;
            return true;
        }

        void Finish(TrainingResult result)
        {
            Result = result;
            input.Block("result");
            if (extraction != null)
                foreach (var z in zombies)
                    if (z != null)
                        z.Active = false; // no more attacks after the result
            if (detector != null)
                detector.enabled = false; // clears every marker and footstep cue
            Finished?.Invoke(result);
        }

        public void Retry() => GameScenes.ReloadCurrent();
    }
}
