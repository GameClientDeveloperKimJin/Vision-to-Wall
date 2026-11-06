using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>Stand-in for BossCombatController until G3: finishes with a fixed result after a fixed time.</summary>
    public sealed class MockCombatSession : MonoBehaviour, ICombatSession
    {
        [SerializeField, Min(0f)] private float durationSec = 10f;
        [SerializeField, Tooltip("Victory or GameOver.")] private GameResult result = GameResult.Victory;

        private bool _running;
        private float _elapsed;

        public event Action<GameResult> Finished;

        public void Configure(float duration, GameResult outcome)
        {
            durationSec = duration;
            result = outcome;
        }

        public void Begin()
        {
            _running = true;
            _elapsed = 0f;
        }

        public void Abort() => _running = false;

        /// <summary>Called from Update with unscaled time.</summary>
        public void Tick(float deltaTime)
        {
            if (!_running) return;

            _elapsed += deltaTime;
            if (_elapsed < durationSec) return;

            _running = false;
            Finished?.Invoke(result);
        }

        private void Update() => Tick(Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec));
    }
}
