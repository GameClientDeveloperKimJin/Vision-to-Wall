using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Game
{
    /// <summary>
    /// Runs <see cref="CombatRules"/> in the scene and draws the placeholder aim line and projectiles.
    /// The boss stays visible outside combat and faces its rest rotation; only Presentation hides or dims it.
    /// Allocation free per frame.
    /// </summary>
    public sealed class BossCombatController : MonoBehaviour, ICombatSession, ICombatStateSource
    {
        [SerializeField, Tooltip("Perspective overlay camera that draws the boss and projectiles.")]
        private Camera overlayCamera;
        [SerializeField] private BossLookAt boss;
        [SerializeField] private ProjectilePool projectiles;
        [SerializeField, Tooltip("Placeholder aim line. Hidden when not aiming.")]
        private LineRenderer aimLine;
        [SerializeField, Min(0.01f), Tooltip("Camera distance of the plane the player stands on. Keep it in front of the boss.")]
        private float playerPlaneDistance = 5f;
        [SerializeField, Min(0f), Tooltip("Aim line width while tracking.")]
        private float trackingLineWidth = 0.03f;
        [SerializeField, Min(0f), Tooltip("Aim line width once locked.")]
        private float lockedLineWidth = 0.1f;
        [SerializeField, Min(0.01f)] private float reflectFlightSec = 0.25f;
        [SerializeField, Min(0.01f)] private float missFlightSec = 0.25f;

        private AppConfig _config;
        private IPlayerInputSource _input;
        private CombatRules _rules;
        private Action<CombatEvent> _onRulesEvent;
        private int _shot = -1;

        public event Action<GameResult> Finished;
        public event Action<CombatEvent> CombatEventRaised;

        public float BossHp01 => _rules != null ? _rules.BossHp01 : 1f;
        public float PlayerHp01 => _rules != null ? _rules.PlayerHp01 : 1f;
        public float RemainingSec => _rules != null ? _rules.RemainingSec : 0f;
        public bool ShieldActive => _rules != null && _rules.ShieldActive;
        public bool IsAiming => _rules != null && _rules.IsAiming;
        public Vector2 AimPosition => _rules != null ? _rules.AimPosition : default;
        public float AimProgress => _rules != null ? _rules.AimProgress : 0f;
        public float BossPanDeg => boss != null ? boss.PanDeg : 0f;
        public float BossTiltDeg => boss != null ? boss.TiltDeg : 0f;

        public bool IsRunning => _rules != null && _rules.IsRunning;
        public Vector2 PlayerPosition => _rules != null ? _rules.PlayerPosition : default;
        public float PlayerPlaneDistance => playerPlaneDistance;
        public Camera OverlayCamera => overlayCamera;

        public void Initialize(AppConfig config, IPlayerInputSource input)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (overlayCamera == null || boss == null || projectiles == null || aimLine == null)
                throw new InvalidOperationException("[Combat] BossCombatController has unassigned references.");

            if (_rules != null) _rules.EventRaised -= _onRulesEvent;

            _config = config;
            _input = input;
            _rules = new CombatRules(config);
            _onRulesEvent = OnRulesEvent;
            _rules.EventRaised += _onRulesEvent;
            _rules.BossPosition = WallSpace.ToNormalized(overlayCamera, boss.transform.position);

            CombatLayers.SetRecursively(boss.transform, CombatLayers.Overlay);
            CombatLayers.SetRecursively(aimLine.transform, CombatLayers.Overlay);
            projectiles.Prepare(CombatLayers.Overlay);

            // Rest pose: facing the camera along its axis, so pan and tilt are 0 at the boss's own screen spot.
            boss.SetRest(Quaternion.LookRotation(-overlayCamera.transform.forward, overlayCamera.transform.up));

            aimLine.positionCount = 2;
            aimLine.useWorldSpace = true;
            aimLine.enabled = false;
        }

        public void Begin()
        {
            if (_rules == null) throw new InvalidOperationException("[Combat] Begin before Initialize.");

            ClearVisuals();
            _rules.Begin(_input.Current);
        }

        /// <summary>Cleans up without raising <see cref="Finished"/> or combat events.</summary>
        public void Abort()
        {
            if (_rules == null) return;

            _rules.Reset();
            ClearVisuals();
            boss.ClearTarget();
        }

        private void OnDestroy()
        {
            if (_rules != null) _rules.EventRaised -= _onRulesEvent;
        }

        private void Update()
        {
            if (_rules == null) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, GamePhaseMachine.MaxFrameDeltaSec);
            if (_rules.IsRunning) TickCombat(deltaTime);

            projectiles.Tick(deltaTime);
            boss.Tick(deltaTime);
        }

        private void TickCombat(float deltaTime)
        {
            _rules.Tick(deltaTime, _input.Current, overlayCamera.aspect);

            if (!_rules.IsRunning)
            {
                // Rules are not running here only because they just finished, or a listener aborted them.
                boss.ClearTarget();
                aimLine.enabled = false;
                if (_rules.Result != GameResult.None) Finished?.Invoke(_rules.Result);
                return;
            }

            boss.SetTarget(WallSpace.ToWorld(overlayCamera, _rules.PlayerPosition, playerPlaneDistance));
            UpdateAimLine();
        }

        private void UpdateAimLine()
        {
            bool aiming = _rules.IsAiming;
            aimLine.enabled = aiming;
            if (!aiming) return;

            aimLine.widthMultiplier = _rules.Stage == AttackStage.Locked ? lockedLineWidth : trackingLineWidth;
            aimLine.SetPosition(0, boss.transform.position);
            aimLine.SetPosition(1, WallSpace.ToWorld(overlayCamera, _rules.AimPosition, playerPlaneDistance));
        }

        private void OnRulesEvent(CombatEvent e)
        {
            Vector3 impact = WallSpace.ToWorld(overlayCamera, e.position, playerPlaneDistance);
            switch (e.type)
            {
                case CombatEventType.Fired:
                    _shot = projectiles.Launch(boss.transform.position, impact, _config.projectileFlightSec);
                    break;
                case CombatEventType.Dodged:
                    EndShot();
                    // Keep going past the player toward the camera.
                    Vector3 onward = impact + (impact - boss.transform.position);
                    projectiles.Launch(impact, onward, missFlightSec);
                    break;
                case CombatEventType.Parried:
                    EndShot();
                    projectiles.Launch(impact, boss.transform.position, reflectFlightSec);
                    break;
                case CombatEventType.PlayerHit:
                    EndShot();
                    break;
            }
            CombatEventRaised?.Invoke(e);
        }

        private void EndShot()
        {
            projectiles.Release(_shot);
            _shot = -1;
        }

        private void ClearVisuals()
        {
            projectiles.ReleaseAll();
            _shot = -1;
            aimLine.enabled = false;
        }
    }
}
