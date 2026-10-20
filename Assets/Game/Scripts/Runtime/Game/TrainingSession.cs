using System;
using Deadlock.Combat;
using Deadlock.Input;
using Deadlock.Player;
using UnityEngine;

namespace Deadlock.Game
{
    public enum TrainingResult
    {
        Playing,
        Cleared,
        Failed,
    }

    /// <summary>
    /// Training round: all zombies dead = cleared, player dead = failed. The result stops player input; retrying
    /// reloads the scene so every zombie, the player, weapons, posture and UI start over.
    /// This is a prototype of the training mode, not the finished mode.
    /// </summary>
    public sealed class TrainingSession : MonoBehaviour
    {
        [SerializeField] PlayerHealth player;
        [SerializeField] PlayerInputRouter input;
        [SerializeField] Zombie[] zombies = Array.Empty<Zombie>();

        public TrainingResult Result { get; private set; } = TrainingResult.Playing;
        public float ElapsedTime { get; private set; }
        public int Remaining { get; private set; }
        public int Defeated => Total - Remaining;
        public int Total => zombies.Length;

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
            else if (Remaining == 0)
                Finish(TrainingResult.Cleared);
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

        void Finish(TrainingResult result)
        {
            Result = result;
            input.Block("result");
            Finished?.Invoke(result);
        }

        public void Retry() => GameScenes.ReloadCurrent();
    }
}
