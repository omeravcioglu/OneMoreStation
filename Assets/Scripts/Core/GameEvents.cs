using System;

namespace Metro
{
    /// <summary>
    /// App-level events for systems that live outside a single city (achievements, platform services).
    /// Simulation events (lines built, tier reached...) stay on GameState; this hub only says which
    /// city is being played and what happened to the player around it.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>A city became the active game (new or loaded). Not raised for the menu's demo city.</summary>
        public static event Action<GameState> CityEntered;
        /// <summary>A brand-new city was founded.</summary>
        public static event Action<GameState> CityFounded;
        public static event Action<GameState, string> GameOver;
        public static event Action GuidedStartFinished;
        public static event Action<GameState> CityLeft;
        /// <summary>A challenge ended (won or lost); the best result is already recorded.</summary>
        public static event Action<ChallengeRun> ChallengeFinished;

        public static void RaiseCityEntered(GameState gs) => CityEntered?.Invoke(gs);
        public static void RaiseCityFounded(GameState gs) => CityFounded?.Invoke(gs);
        public static void RaiseGameOver(GameState gs, string reason) => GameOver?.Invoke(gs, reason);
        public static void RaiseGuidedStartFinished() => GuidedStartFinished?.Invoke();
        public static void RaiseCityLeft(GameState gs) => CityLeft?.Invoke(gs);
        public static void RaiseChallengeFinished(ChallengeRun run) => ChallengeFinished?.Invoke(run);
    }
}
