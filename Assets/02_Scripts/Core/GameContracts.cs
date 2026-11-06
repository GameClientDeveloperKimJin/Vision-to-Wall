using System;
using UnityEngine;

namespace VisionToWall.Core
{
    public enum GamePhase
    {
        /// <summary>Media wall: attract and energy charging.</summary>
        MediaWall,
        /// <summary>Boss awakening transition.</summary>
        Awaken,
        /// <summary>Arcade boss battle.</summary>
        Combat,
        /// <summary>Victory or game over presentation.</summary>
        Result
    }

    public enum GameResult
    {
        None,
        Victory,
        GameOver
    }

    public enum PlayerPositionSource
    {
        None,
        /// <summary>Dedicated body stream (udpBodyPort).</summary>
        Body,
        /// <summary>Intensity-weighted centroid of the motion stream (udpPort).</summary>
        MotionCentroid
    }

    /// <summary>Latest player input. Position origin is bottom-left; mirror already applied.</summary>
    public struct PlayerSample
    {
        /// <summary>Normalized screen position, 0 to 1 on each axis.</summary>
        public Vector2 position;
        public bool hasPosition;
        public PlayerPositionSource source;
        /// <summary>Smoothed motion intensity, 0 to 1.</summary>
        public float motion;
    }

    /// <summary>Implemented by Game.PlayerInputTracker.</summary>
    public interface IPlayerInputSource
    {
        PlayerSample Current { get; }
    }

    /// <summary>Implemented by Game.GamePhaseMachine.</summary>
    public interface IGamePhaseSource
    {
        GamePhase Phase { get; }
        /// <summary>Valid during <see cref="GamePhase.Result"/>; otherwise None.</summary>
        GameResult Result { get; }
        /// <summary>Unscaled seconds since the current phase began.</summary>
        float PhaseElapsedSec { get; }
        /// <summary>Awakening gauge, 0 to 1.</summary>
        float AwakenCharge { get; }
        /// <summary>Raised on the main thread on every transition.</summary>
        event Action<GamePhase> PhaseChanged;
    }

    public enum CombatEventType
    {
        AimStarted,
        AimLocked,
        Fired,
        Dodged,
        PlayerHit,
        ShieldRaised,
        ShieldLowered,
        Parried,
        BossDefeated
    }

    public struct CombatEvent
    {
        public CombatEventType type;
        /// <summary>Normalized screen position of the event.</summary>
        public Vector2 position;
    }

    /// <summary>Implemented by Game.BossCombatController.</summary>
    public interface ICombatStateSource
    {
        float BossHp01 { get; }
        float PlayerHp01 { get; }
        float RemainingSec { get; }
        bool ShieldActive { get; }
        bool IsAiming { get; }
        /// <summary>Normalized screen position of the aim point.</summary>
        Vector2 AimPosition { get; }
        /// <summary>Aim progress, 0 to 1.</summary>
        float AimProgress { get; }
        /// <summary>Boss rotation from its rest pose. Source for the v2.1 serial packet.</summary>
        float BossPanDeg { get; }
        float BossTiltDeg { get; }
        /// <summary>Raised on the main thread.</summary>
        event Action<CombatEvent> CombatEventRaised;
    }

    /// <summary>Implemented by Render.MediaWallController. Called by Game presentation code.</summary>
    public interface IWallPresentation
    {
        /// <param name="crimsonBlend">0 = calm blue, 1 = combat crimson.</param>
        void SetTheme(float crimsonBlend);

        /// <param name="strength">Particle attraction strength; 0 turns it off.</param>
        /// <param name="worldCenter">World position particles are drawn toward.</param>
        void SetAbsorb(float strength, Vector3 worldCenter);
    }
}
