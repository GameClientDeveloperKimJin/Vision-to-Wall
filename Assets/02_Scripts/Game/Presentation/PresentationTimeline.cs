using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>Values one frame of presentation applies. Plain data, so the look can be tested without a scene.</summary>
    public struct PresentationLook
    {
        public float crimson;    // 0 = calm blue, 1 = combat red
        public float absorb;     // 0~1, particle pull toward the boss
        public float irisOpen;   // 0 = closed, 1 = open
        public float irisGlow;   // emission multiplier, above 1 during the awaken flash
        public float backdrop;   // 0 = hidden, 1 = arena fully shown
        public float bossReveal; // 0 = not drawn, 1 = full size
        public float timeScale;  // Time.timeScale to apply
    }

    [Serializable]
    public sealed class PresentationTuning
    {
        [Min(0.01f), Tooltip("Crimson to blue after the result. Requirement 4: slowly, 5 s.")]
        public float returnSec = 5f;
        [Min(0.01f)] public float irisCloseSec = 2f;
        [Range(0f, 1f), Tooltip("Gauge value where the boss starts to form from the gathered particles.")]
        public float revealStartCharge = 0.25f;
        [Range(0.01f, 1f), Tooltip("Boss size when the gauge is full. The awaken phase finishes the growth.")]
        public float revealAtFullCharge = 0.6f;
        [Min(0.01f), Tooltip("Limits how fast the size follows the gauge, so a jumpy gauge stays smooth.")]
        public float revealFollowSec = 0.4f;
        [Range(0.01f, 1f), Tooltip("Fraction of the awaken phase in which the boss reaches full size.")]
        public float revealAwakenFraction = 0.35f;
        [Min(0f), Tooltip("Boss size stays until then after a victory; the explosion has hidden it by that time.")]
        public float revealVictoryHoldSec = 0.6f;
        [Min(0.01f), Tooltip("After a game over, the boss shrinks away once its eye has closed.")]
        public float revealRetreatSec = 1.5f;
        [Min(0f), Tooltip("Emission peak of the awaken flash.")]
        public float awakenFlashPeak = 4f;
        [Range(0.01f, 1f), Tooltip("Fraction of the awaken phase where the flash peaks.")]
        public float awakenFlashAt = 0.6f;
        [Range(0f, 1f), Tooltip("Fraction of the awaken phase when the arena starts to appear.")]
        public float backdropStartAt = 0.5f;
        [Min(0.01f)] public float backdropHideSec = 1.5f;
        [Range(0f, 0.95f), Tooltip("Gauge at which the particle pull toward the boss starts. Below it, particles follow the hand only.")]
        public float absorbStartCharge = 0.5f;
        [Min(0.01f), Tooltip("How fast the particle pull stops once the boss wakes.")]
        public float absorbReleaseSec = 0.4f;
        [Range(0.01f, 1f)] public float slowMotionScale = 0.25f;
        [Min(0f), Tooltip("Unscaled seconds of full slow motion after a victory.")]
        public float slowMotionHoldSec = 1.5f;
        [Min(0.01f), Tooltip("Unscaled seconds to ease back to normal speed.")]
        public float slowMotionEaseSec = 1f;
    }

    /// <summary>
    /// Turns the phase and elapsed unscaled time into a <see cref="PresentationLook"/>. It only reads game state.
    /// Values move from where they are, so an interrupted phase never jumps. Allocation free.
    /// </summary>
    public sealed class PresentationTimeline
    {
        private readonly PresentationTuning _tuning;
        private readonly float _awakenSec;
        private GamePhase _phase = GamePhase.MediaWall;
        private GameResult _result = GameResult.None;
        private float _elapsed;
        private PresentationLook _look;

        public PresentationTimeline(PresentationTuning tuning, float awakenSec)
        {
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            _awakenSec = Mathf.Max(awakenSec, 0.01f);
            Reset();
        }

        public PresentationLook Current => _look;

        /// <summary>Back to the calm media wall at once, with no fade.</summary>
        public void Reset()
        {
            _phase = GamePhase.MediaWall;
            _result = GameResult.None;
            _elapsed = 0f;
            _look = new PresentationLook { timeScale = 1f };
        }

        public void SetPhase(GamePhase phase, GameResult result)
        {
            _phase = phase;
            _result = result;
            _elapsed = 0f;
        }

        /// <param name="deltaTime">Unscaled and limited by the caller.</param>
        /// <param name="charge">Awaken gauge, 0~1.</param>
        public PresentationLook Tick(float deltaTime, float charge)
        {
            _elapsed += deltaTime;

            switch (_phase)
            {
                case GamePhase.MediaWall: TickMediaWall(deltaTime, charge); break;
                case GamePhase.Awaken: TickAwaken(deltaTime); break;
                case GamePhase.Combat: TickCombat(deltaTime); break;
                case GamePhase.Result: TickResult(deltaTime); break;
            }
            return _look;
        }

        private void TickMediaWall(float deltaTime, float charge)
        {
            _look.timeScale = 1f;
            _look.crimson = Mathf.MoveTowards(_look.crimson, 0f, deltaTime / _tuning.returnSec);
            // The first waves belong to the hand; the pull starts only once the visitor keeps going.
            _look.absorb = Mathf.InverseLerp(_tuning.absorbStartCharge, 1f, Mathf.Clamp01(charge));
            _look.irisOpen = Mathf.MoveTowards(_look.irisOpen, 0f, deltaTime / _tuning.irisCloseSec);
            _look.irisGlow = Mathf.MoveTowards(_look.irisGlow, 0f, deltaTime / _tuning.irisCloseSec);
            _look.backdrop = Mathf.MoveTowards(_look.backdrop, 0f, deltaTime / _tuning.backdropHideSec);

            float forming = Mathf.InverseLerp(_tuning.revealStartCharge, 1f, Mathf.Clamp01(charge));
            float target = Mathf.SmoothStep(0f, _tuning.revealAtFullCharge, forming);
            _look.bossReveal = Mathf.MoveTowards(_look.bossReveal, target, deltaTime / _tuning.revealFollowSec);
        }

        private void TickAwaken(float deltaTime)
        {
            float t = Mathf.Clamp01(_elapsed / _awakenSec);

            _look.timeScale = 1f;
            _look.crimson = Mathf.Max(_look.crimson, t);
            _look.absorb = Mathf.MoveTowards(_look.absorb, 0f, deltaTime / _tuning.absorbReleaseSec);
            _look.irisOpen = Mathf.Max(_look.irisOpen, Mathf.SmoothStep(0f, 1f, t / _tuning.awakenFlashAt));
            _look.irisGlow = Flash(t);
            float growth = Mathf.SmoothStep(0f, 1f, t / _tuning.revealAwakenFraction);
            _look.bossReveal = Mathf.Max(_look.bossReveal, growth);
            float backdropT = Mathf.InverseLerp(_tuning.backdropStartAt, 1f, t);
            _look.backdrop = Mathf.Max(_look.backdrop, backdropT);
        }

        /// <summary>Rises to the peak, then settles at the combat level 1 by the end of the phase.</summary>
        private float Flash(float t)
        {
            if (t < _tuning.awakenFlashAt)
                return Mathf.Lerp(0f, _tuning.awakenFlashPeak, t / _tuning.awakenFlashAt);

            float fall = (t - _tuning.awakenFlashAt) / Mathf.Max(1f - _tuning.awakenFlashAt, 0.01f);
            return Mathf.Lerp(_tuning.awakenFlashPeak, 1f, Mathf.Clamp01(fall));
        }

        private void TickCombat(float deltaTime)
        {
            _look.timeScale = 1f;
            _look.crimson = 1f;
            _look.absorb = 0f;
            _look.irisOpen = 1f;
            _look.irisGlow = Mathf.MoveTowards(_look.irisGlow, 1f, deltaTime * 2f);
            _look.backdrop = 1f;
            _look.bossReveal = 1f;
        }

        private void TickResult(float deltaTime)
        {
            _look.crimson = 1f;
            _look.absorb = 0f;
            _look.backdrop = Mathf.MoveTowards(_look.backdrop, 0f, deltaTime / _tuning.backdropHideSec);

            if (_result == GameResult.Victory)
            {
                float ease = Mathf.Clamp01((_elapsed - _tuning.slowMotionHoldSec) / _tuning.slowMotionEaseSec);
                _look.timeScale = Mathf.Lerp(_tuning.slowMotionScale, 1f, ease);
                // The boss is gone and must not come back with the wall.
                _look.bossReveal = _elapsed >= _tuning.revealVictoryHoldSec ? 0f : 1f;
                return;
            }

            // Game over: the boss draws its energy back in and the eye closes.
            _look.timeScale = 1f;
            _look.irisOpen = Mathf.MoveTowards(_look.irisOpen, 0f, deltaTime / _tuning.irisCloseSec);
            _look.irisGlow = Mathf.MoveTowards(_look.irisGlow, 0f, deltaTime / _tuning.irisCloseSec);
            if (_elapsed >= _tuning.irisCloseSec)
                _look.bossReveal = Mathf.MoveTowards(_look.bossReveal, 0f, deltaTime / _tuning.revealRetreatSec);
        }
    }
}
