using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// MediaWall -> Awaken -> Combat -> Result -> MediaWall. Timing uses unscaled time, so result slow motion does
    /// not affect it. Allocation free per frame.
    /// </summary>
    public sealed class GamePhaseMachine : MonoBehaviour, IGamePhaseSource
    {
        /// <summary>
        /// Longest time one frame can count for. Unscaled delta has no cap: a hitch (3.8 s measured on entering
        /// Play) would otherwise fill the gauge in one frame, without that much motion.
        /// </summary>
        public const float MaxFrameDeltaSec = 0.1f;

        private AppConfig _config;
        private IPlayerInputSource _input;
        private ICombatSession _combat;
        private AwakenGauge _gauge;

        public GamePhase Phase { get; private set; } = GamePhase.MediaWall;

        /// <summary>Outcome of the last combat. None outside the Result phase.</summary>
        public GameResult Result { get; private set; } = GameResult.None;

        public float PhaseElapsedSec { get; private set; }

        /// <summary>Stays at 1 from Awaken until the return to MediaWall.</summary>
        public float AwakenCharge => _gauge != null ? _gauge.Charge : 0f;

        /// <summary>Seconds left before the gauge can charge again.</summary>
        public float RearmCooldownLeftSec => _gauge != null ? _gauge.CooldownLeftSec : 0f;

        public event Action<GamePhase> PhaseChanged;

        public void Initialize(AppConfig config, IPlayerInputSource input, ICombatSession combat)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (combat == null) throw new ArgumentNullException(nameof(combat));

            Shutdown();
            if (_combat != null) _combat.Finished -= OnCombatFinished;

            _config = config;
            _input = input;
            _combat = combat;
            _gauge = new AwakenGauge(config);
            _combat.Finished += OnCombatFinished;
        }

        /// <summary>
        /// Aborts a running combat and returns to MediaWall with an empty gauge and no cooldown.
        /// Called when the component is disabled or the application quits.
        /// </summary>
        public void Shutdown()
        {
            GamePhase previous = Phase;
            // Leave Combat first: a session that raises Finished from Abort must be ignored.
            Phase = GamePhase.MediaWall;
            Result = GameResult.None;
            PhaseElapsedSec = 0f;
            _gauge?.Reset();

            if (previous == GamePhase.Combat) _combat.Abort();
            if (previous != GamePhase.MediaWall) PhaseChanged?.Invoke(GamePhase.MediaWall);
        }

        /// <summary>Advances the machine. Called from Update with unscaled time.</summary>
        public void Tick(float deltaTime)
        {
            if (_config == null) return;

            deltaTime = Mathf.Min(deltaTime, MaxFrameDeltaSec);
            PhaseElapsedSec += deltaTime;
            switch (Phase)
            {
                case GamePhase.MediaWall:
                    _gauge.Tick(_input.Current.motion, deltaTime);
                    if (_gauge.IsFull) SetPhase(GamePhase.Awaken);
                    break;

                case GamePhase.Awaken:
                    if (PhaseElapsedSec >= _config.awakenTransitionSec) EnterCombat();
                    break;

                case GamePhase.Result:
                    if (PhaseElapsedSec >= _config.resultDurationSec) ReturnToMediaWall();
                    break;
            }
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private void OnDisable() => Shutdown();

        private void OnDestroy()
        {
            if (_combat != null) _combat.Finished -= OnCombatFinished;
        }

        private void EnterCombat()
        {
            Phase = GamePhase.Combat;
            PhaseElapsedSec = 0f;
            // Begin before notifying, so listeners read the new session's state.
            _combat.Begin();
            // A session that finished inside Begin already announced Result.
            if (Phase == GamePhase.Combat) PhaseChanged?.Invoke(GamePhase.Combat);
        }

        private void OnCombatFinished(GameResult result)
        {
            if (Phase != GamePhase.Combat) return;

            Result = result;
            SetPhase(GamePhase.Result);
        }

        private void ReturnToMediaWall()
        {
            Result = GameResult.None;
            _gauge.Reset();
            _gauge.StartCooldown();
            SetPhase(GamePhase.MediaWall);
        }

        private void SetPhase(GamePhase next)
        {
            Phase = next;
            PhaseElapsedSec = 0f;
            PhaseChanged?.Invoke(next);
        }
    }
}
