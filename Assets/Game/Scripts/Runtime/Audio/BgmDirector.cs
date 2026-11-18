using Deadlock.Game;
using Deadlock.Match;

namespace Deadlock.Audio
{
    /// <summary>
    /// The music states of the game (Stage 39). <see cref="SpecialEvent"/> is kept for the special event mode of the
    /// spec (LAST STAND, events), which does not exist yet: nothing selects it.
    /// </summary>
    public enum BgmState
    {
        None,
        Home,
        Matchmaking,
        Training,
        /// <summary>Fighting and exploring (the match, the ruins, the online play). Kept low under the play's sounds.</summary>
        Combat,
        SpecialEvent,
        Victory,
        Defeat,
        Result,
    }

    /// <summary>Which music plays where (Stage 39): from the scene, the offline match's phase and the training's end.</summary>
    public static class BgmDirector
    {
        /// <summary>
        /// Combat music is played at this share of the BGM volume: the enemy's footsteps and shots are the information
        /// of play and must stay clear over it. Provisional.
        /// </summary>
        public const float CombatLevel = 0.5f;

        /// <summary>The music of a scene when no match or training result decides otherwise.</summary>
        public static BgmState ForScene(string scene) => scene switch
        {
            GameScenes.Home => BgmState.Home,
            GameScenes.FiringRange => BgmState.Training,
            GameScenes.Training => BgmState.Training,
            GameScenes.Ruins => BgmState.Combat,
            GameScenes.Online => BgmState.Combat,
            _ => BgmState.None,
        };

        public static BgmState ForMatch(MatchPhase phase) => phase switch
        {
            MatchPhase.Matchmaking => BgmState.Matchmaking,
            MatchPhase.Loading => BgmState.Matchmaking,
            MatchPhase.Countdown => BgmState.Combat,
            MatchPhase.Playing => BgmState.Combat,
            MatchPhase.Finished => BgmState.Result,
            _ => BgmState.Home,
        };

        public static BgmState ForTraining(TrainingResult result) => result switch
        {
            TrainingResult.Cleared => BgmState.Victory,
            TrainingResult.Failed => BgmState.Defeat,
            _ => BgmState.Training,
        };

        /// <summary>Victory and defeat are played once (a short piece), the others loop.</summary>
        public static bool Loops(BgmState state) => state != BgmState.Victory && state != BgmState.Defeat;

        public static float Level(BgmState state) => state == BgmState.Combat ? CombatLevel : 1f;
    }
}
