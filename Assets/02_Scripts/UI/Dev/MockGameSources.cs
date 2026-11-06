using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>
    /// Dev-only game state for the HUD and F1 panel, driven from the keyboard:
    /// 1 awaken, 2 combat, 0 media wall, P parry, H hit, S shield, V victory, G game over.
    /// </summary>
    public sealed class MockGameSources : MonoBehaviour, IGamePhaseSource, ICombatStateSource, IPlayerInputSource
    {
        private const float HpStep = 0.2f;

        private float _bossHp = 1f;
        private float _playerHp = 1f;
        private float _combatTime;
        private float _phaseStart;

        public GamePhase Phase { get; private set; } = GamePhase.MediaWall;
        public GameResult Result { get; private set; } = GameResult.None;
        public float PhaseElapsedSec => Time.unscaledTime - _phaseStart;
        public float AwakenCharge => Phase == GamePhase.MediaWall ? 0f : 1f;
        public event Action<GamePhase> PhaseChanged;

        public float BossHp01 => _bossHp;
        public float PlayerHp01 => _playerHp;
        public float RemainingSec => Mathf.Max(0f, ConfigService.Current.combatDurationSec - _combatTime);
        public bool ShieldActive { get; private set; }
        public bool IsAiming => false;
        public Vector2 AimPosition => default;
        public float AimProgress => 0f;
        public float BossPanDeg => 0f;
        public float BossTiltDeg => 0f;
        public event Action<CombatEvent> CombatEventRaised;

        public PlayerSample Current => new PlayerSample
        {
            position = new Vector2(0.5f, 0.5f),
            hasPosition = true,
            source = PlayerPositionSource.Body,
            motion = ShieldActive ? 0.9f : 0.2f,
        };

        private void Update()
        {
            if (Phase == GamePhase.Combat) _combatTime += Time.unscaledDeltaTime;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SetPhase(GamePhase.Awaken, GameResult.None);
            if (kb.digit2Key.wasPressedThisFrame) StartCombat();
            if (kb.digit0Key.wasPressedThisFrame) SetPhase(GamePhase.MediaWall, GameResult.None);
            if (kb.sKey.wasPressedThisFrame) ShieldActive = !ShieldActive;
            if (kb.vKey.wasPressedThisFrame) { _bossHp = 0f; SetPhase(GamePhase.Result, GameResult.Victory); }
            if (kb.gKey.wasPressedThisFrame) { _playerHp = 0f; SetPhase(GamePhase.Result, GameResult.GameOver); }
            if (kb.pKey.wasPressedThisFrame) Hit(ref _bossHp, CombatEventType.Parried);
            if (kb.hKey.wasPressedThisFrame) Hit(ref _playerHp, CombatEventType.PlayerHit);
        }

        private void StartCombat()
        {
            _bossHp = 1f;
            _playerHp = 1f;
            _combatTime = 0f;
            ShieldActive = false;
            SetPhase(GamePhase.Combat, GameResult.None);
        }

        private void Hit(ref float hp, CombatEventType type)
        {
            hp = Mathf.Max(0f, hp - HpStep);
            CombatEventRaised?.Invoke(new CombatEvent { type = type, position = new Vector2(0.5f, 0.5f) });
        }

        private void SetPhase(GamePhase phase, GameResult result)
        {
            Phase = phase;
            Result = result;
            _phaseStart = Time.unscaledTime;
            PhaseChanged?.Invoke(phase);
        }
    }
}
