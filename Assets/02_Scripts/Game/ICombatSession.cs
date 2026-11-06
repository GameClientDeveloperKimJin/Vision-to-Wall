using System;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// One boss battle, driven by GamePhaseMachine.
    /// Implemented by MockCombatSession (G2) and BossCombatController (G3).
    /// </summary>
    public interface ICombatSession
    {
        void Begin();

        /// <summary>Stops the battle and cleans up without raising <see cref="Finished"/>.</summary>
        void Abort();

        /// <summary>Raised once when the battle ends by its own rules.</summary>
        event Action<GameResult> Finished;
    }
}
