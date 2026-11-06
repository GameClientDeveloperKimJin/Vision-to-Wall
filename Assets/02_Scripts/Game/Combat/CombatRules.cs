using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    public enum AttackStage { Idle, Tracking, Locked, Flight, Recover }

    /// <summary>
    /// Combat rules with no scene dependency: attack cycle, arrival check, shield, HP and end conditions.
    /// Only one projectile exists at a time and each arrival changes one HP, so both sides cannot reach 0 together.
    /// Durations are read from the config every tick, so F1 changes apply at once. Allocation free per tick.
    /// </summary>
    public sealed class CombatRules
    {
        private static readonly Vector2 ScreenCenter = new Vector2(0.5f, 0.5f);

        private readonly AppConfig _config;

        private int _bossHp;
        private int _playerHp;
        private float _elapsed;
        private float _stageTime;
        private float _noInputTime;
        private float _shieldTime;
        private float _cooldownLeft;
        private Vector2 _lastPosition = ScreenCenter;

        public CombatRules(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Reset();
        }

        /// <summary>Raised during <see cref="Begin"/> and <see cref="Tick"/>.</summary>
        public event Action<CombatEvent> EventRaised;

        public bool IsRunning { get; private set; }

        /// <summary>None until the combat ends. Stays set until the next Begin or Reset.</summary>
        public GameResult Result { get; private set; }

        public AttackStage Stage { get; private set; }
        public bool ShieldActive { get; private set; }
        public Vector2 AimPosition { get; private set; }

        /// <summary>Last known player position, or the screen center before any is known.</summary>
        public Vector2 PlayerPosition => _lastPosition;

        /// <summary>Position reported with <see cref="CombatEventType.BossDefeated"/>.</summary>
        public Vector2 BossPosition { get; set; } = ScreenCenter;

        public float BossHp01 => (float)_bossHp / MaxBossHp;
        public float PlayerHp01 => (float)_playerHp / MaxPlayerHp;
        public float RemainingSec => Mathf.Max(0f, _config.combatDurationSec - _elapsed);
        public bool IsAiming => Stage == AttackStage.Tracking || Stage == AttackStage.Locked;

        /// <summary>0~1 over <c>aimSec</c>, including the locked part. 0 when not aiming.</summary>
        public float AimProgress
        {
            get
            {
                float aimSec = _config.aimSec;
                switch (Stage)
                {
                    case AttackStage.Tracking: return Mathf.Clamp01(_stageTime / aimSec);
                    case AttackStage.Locked: return Mathf.Clamp01((aimSec - _config.aimLockSec + _stageTime) / aimSec);
                    default: return 0f;
                }
            }
        }

        /// <summary>0~1 from firing to arrival. 0 outside the flight.</summary>
        public float FlightProgress => Stage == AttackStage.Flight
            ? Mathf.Clamp01(_stageTime / _config.projectileFlightSec)
            : 0f;

        private int MaxBossHp { get; set; }
        private int MaxPlayerHp { get; set; }

        /// <summary>Distance in screen heights. X is scaled by the aspect, so a circle stays round on screen.</summary>
        public static float Distance(Vector2 a, Vector2 b, float aspect)
        {
            float dx = (a.x - b.x) * aspect;
            float dy = a.y - b.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Clears the state without raising events or a result. Used by Abort.</summary>
        public void Reset()
        {
            // Max values are fixed here: HP fractions must not jump when F1 changes them mid-combat.
            MaxBossHp = Mathf.Max(1, _config.bossMaxHp);
            MaxPlayerHp = Mathf.Max(1, _config.playerMaxHp);
            _bossHp = MaxBossHp;
            _playerHp = MaxPlayerHp;
            _elapsed = 0f;
            _stageTime = 0f;
            _noInputTime = 0f;
            _shieldTime = 0f;
            _cooldownLeft = 0f;
            _lastPosition = ScreenCenter;
            AimPosition = ScreenCenter;
            ShieldActive = false;
            Stage = AttackStage.Idle;
            Result = GameResult.None;
            IsRunning = false;
        }

        /// <summary>Starts a combat at full HP with the first aim. The first sample's position is used if valid.</summary>
        public void Begin(in PlayerSample sample)
        {
            Reset();
            if (sample.hasPosition) _lastPosition = sample.position;
            IsRunning = true;
            AimPosition = _lastPosition;
            EnterStage(AttackStage.Tracking);
        }

        /// <param name="deltaTime">Unscaled delta. Limited to <see cref="GamePhaseMachine.MaxFrameDeltaSec"/>.</param>
        /// <param name="aspect">Screen width / height, used by the arrival check.</param>
        public void Tick(float deltaTime, in PlayerSample sample, float aspect)
        {
            if (!IsRunning) return;

            deltaTime = Mathf.Min(deltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            _elapsed += deltaTime;

            if (sample.hasPosition)
            {
                _lastPosition = sample.position;
                _noInputTime = 0f;
            }
            else
            {
                _noInputTime += deltaTime;
            }

            // Shield first, so a shield raised on the arrival frame counts.
            TickShield(deltaTime, sample.motion);
            TickAttack(deltaTime, aspect);
            if (!IsRunning) return; // a win or loss on the same frame beats the timers below

            if (_elapsed >= _config.combatDurationSec || _noInputTime >= _config.combatAbandonSec)
                Finish(GameResult.GameOver);
        }

        private void TickShield(float deltaTime, float motion)
        {
            if (ShieldActive)
            {
                _shieldTime += deltaTime;
                if (_shieldTime < _config.shieldDurationSec) return;

                ShieldActive = false;
                _cooldownLeft = _config.shieldCooldownSec;
                Raise(CombatEventType.ShieldLowered, _lastPosition);
                return;
            }

            if (_cooldownLeft > 0f) _cooldownLeft -= deltaTime;
            if (_cooldownLeft > 0f || motion < _config.shieldThreshold) return;

            ShieldActive = true;
            _shieldTime = 0f;
            Raise(CombatEventType.ShieldRaised, _lastPosition);
        }

        private void TickAttack(float deltaTime, float aspect)
        {
            _stageTime += deltaTime;

            // The remainder carries over, so frame length does not stretch the attack cycle.
            float duration = StageDuration(Stage);
            while (IsRunning && _stageTime >= duration)
            {
                _stageTime -= duration;
                AdvanceStage(aspect);
                duration = StageDuration(Stage);
            }

            if (IsRunning && Stage == AttackStage.Tracking) AimPosition = _lastPosition;
        }

        private float StageDuration(AttackStage stage)
        {
            switch (stage)
            {
                case AttackStage.Tracking: return _config.aimSec - _config.aimLockSec;
                case AttackStage.Locked: return _config.aimLockSec;
                case AttackStage.Flight: return _config.projectileFlightSec;
                case AttackStage.Recover: return _config.attackRecoverSec;
                default: return float.PositiveInfinity;
            }
        }

        private void AdvanceStage(float aspect)
        {
            switch (Stage)
            {
                case AttackStage.Tracking:
                    AimPosition = _lastPosition;
                    EnterStage(AttackStage.Locked);
                    break;
                case AttackStage.Locked:
                    EnterStage(AttackStage.Flight);
                    break;
                case AttackStage.Flight:
                    Stage = AttackStage.Recover; // before Arrive: a finish inside it must not be overwritten
                    Arrive(aspect);
                    break;
                case AttackStage.Recover:
                    AimPosition = _lastPosition; // AimStarted must not report the previous shot's spot
                    EnterStage(AttackStage.Tracking);
                    break;
            }
        }

        private void EnterStage(AttackStage stage)
        {
            Stage = stage;
            switch (stage)
            {
                case AttackStage.Tracking: Raise(CombatEventType.AimStarted, AimPosition); break;
                case AttackStage.Locked: Raise(CombatEventType.AimLocked, AimPosition); break;
                case AttackStage.Flight: Raise(CombatEventType.Fired, AimPosition); break;
            }
        }

        private void Arrive(float aspect)
        {
            if (Distance(_lastPosition, AimPosition, aspect) > _config.hitRadius)
            {
                Raise(CombatEventType.Dodged, AimPosition);
            }
            else if (ShieldActive)
            {
                _bossHp--;
                Raise(CombatEventType.Parried, AimPosition);
                if (_bossHp <= 0)
                {
                    Raise(CombatEventType.BossDefeated, BossPosition);
                    Finish(GameResult.Victory);
                }
            }
            else
            {
                _playerHp--;
                Raise(CombatEventType.PlayerHit, AimPosition);
                if (_playerHp <= 0) Finish(GameResult.GameOver);
            }
        }

        private void Finish(GameResult result)
        {
            if (!IsRunning) return;

            IsRunning = false;
            Result = result;
            Stage = AttackStage.Idle;
            if (ShieldActive)
            {
                ShieldActive = false;
                Raise(CombatEventType.ShieldLowered, _lastPosition);
            }
        }

        private void Raise(CombatEventType type, Vector2 position)
        {
            EventRaised?.Invoke(new CombatEvent { type = type, position = position });
        }
    }
}
