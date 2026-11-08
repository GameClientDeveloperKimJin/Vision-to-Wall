using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;
using VisionToWall.Network;
using VisionToWall.Render;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// Dev scene for <see cref="BossCombatController"/> driven by the real <see cref="GamePhaseMachine"/>.
    /// Tab switches between the hand sensor (Tools/HandSensor) and the keyboard; a scenario plays by itself so a
    /// full combat can be checked with no polling. IMGUI allocates; turn the overlay and logs off when profiling GC.
    /// </summary>
    public sealed class CombatDevBootstrap : MonoBehaviour
    {
        public enum Scenario { None, Victory, GameOverHits, GameOverTimeout, GameOverAbandon }

        private sealed class DevInput : IPlayerInputSource
        {
            public IPlayerInputSource Real;
            public bool UseReal;
            public PlayerSample Mock = new PlayerSample { position = new Vector2(0.5f, 0.5f), hasPosition = true };

            public PlayerSample Current => UseReal ? Real.Current : Mock;
        }

        [SerializeField] private BossCombatController combat;
        [SerializeField] private MediaWallController wall;
        [SerializeField, Tooltip("Treat the sensor as external without touching config.json.")]
        private bool forceExternal = true;
        [SerializeField, Tooltip("Off = start on the keyboard. Tab switches in Play.")]
        private bool startWithSensor = true;
        [SerializeField] private Scenario scenario = Scenario.None;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logEvents = true;
        [SerializeField, Min(0.01f)] private float keyboardSpeed = 0.7f;

        private const float Pad = 8f;
        private const float LineHeight = 22f;
        private const float BarWidth = 400f;
        private const float BarHeight = 18f;
        private const float GuideHeight = 90f;
        private const float MarkerSize = 14f;
        private const float RingDot = 4f;
        private const int RingDots = 48;
        private static readonly Color BarBg = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color BossBar = new Color(1f, 0.3f, 0.2f, 1f);
        private static readonly Color PlayerBar = new Color(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color AimRing = new Color(1f, 0.2f, 0.2f, 0.9f);
        private static readonly Color PlayerMarker = new Color(0.3f, 1f, 0.5f, 1f);
        private static readonly Color ShieldMarker = new Color(0.3f, 0.8f, 1f, 1f);

        private AppConfig _config;
        private DevInput _input;
        private GamePhaseMachine _machine;
        private ExhibitionPhaseGate _gate;
        private GUIStyle _guideStyle;
        private float _combatStart;
        private int _dodged;
        private int _parried;
        private int _hits;
        private int _combats;
        private int _lockedSide = 1;

        private void Awake()
        {
            _config = ConfigService.Load();
            if (forceExternal) _config.sensorMode = AppConfig.SensorModeExternal; // in memory only

            UdpMotionReceiver motion = CreateReceiver("MotionReceiver", _config.udpPort);
            UdpMotionReceiver body = CreateReceiver("BodyReceiver", _config.udpBodyPort);
            var poseObject = new GameObject("PoseReceiver");
            poseObject.transform.SetParent(transform, false);
            var pose = poseObject.AddComponent<UdpPoseReceiver>();
            pose.Initialize(_config); // udpPosePort: guard pose (arm raised) for the shield

            var tracker = gameObject.AddComponent<PlayerInputTracker>();
            tracker.Initialize(_config, motion, body, pose);
            _input = new DevInput { Real = tracker, UseReal = startWithSensor };

            var fsm = gameObject.AddComponent<ExhibitionStateMachine>();
            fsm.Initialize(_config, motion);

            combat.Initialize(_config, _input);
            combat.CombatEventRaised += OnCombatEvent;
            combat.Finished += OnFinished;

            _machine = gameObject.AddComponent<GamePhaseMachine>();
            _machine.PhaseChanged += OnPhaseChanged;
            _machine.Initialize(_config, _input, combat);

            _gate = new ExhibitionPhaseGate(fsm, _machine);
            if (wall != null) wall.Initialize(motion, _gate);
        }

        private void OnDestroy()
        {
            _gate?.Dispose();
            if (combat != null)
            {
                combat.CombatEventRaised -= OnCombatEvent;
                combat.Finished -= OnFinished;
            }
            if (_machine != null) _machine.PhaseChanged -= OnPhaseChanged;
        }

        private UdpMotionReceiver CreateReceiver(string objectName, int port)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var receiver = child.AddComponent<UdpMotionReceiver>();
            receiver.Initialize(_config, port);
            return receiver;
        }

        private void Update()
        {
            if (scenario != Scenario.None)
            {
                _input.UseReal = false;
                PlayScenario();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.tabKey.wasPressedThisFrame) _input.UseReal = !_input.UseReal;
            if (keyboard.nKey.wasPressedThisFrame) _input.Mock.hasPosition = !_input.Mock.hasPosition;

            var move = Vector2.zero;
            if (keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.upArrowKey.isPressed) move.y += 1f;

            Vector2 next = _input.Mock.position + move * (keyboardSpeed * Time.unscaledDeltaTime);
            _input.Mock.position = new Vector2(Mathf.Clamp01(next.x), Mathf.Clamp01(next.y));
            _input.Mock.motion = keyboard.spaceKey.isPressed ? 1f : 0f;
            _input.Mock.guardPose = keyboard.gKey.isPressed; // arm raised while held
        }

        /// <summary>Plays from the phase and aim state alone, like a person watching the screen.</summary>
        private void PlayScenario()
        {
            if (_machine.Phase != GamePhase.Combat)
            {
                _input.Mock.hasPosition = true;
                _input.Mock.position = new Vector2(0.5f, 0.5f);
                _input.Mock.motion = _machine.Phase == GamePhase.MediaWall ? 1f : 0f; // wakes the boss
                return;
            }

            switch (scenario)
            {
                case Scenario.Victory:
                    _input.Mock.motion = combat.AimProgress >= 0.9f ? 1f : 0f; // raise just before the shot
                    break;
                case Scenario.GameOverHits:
                    _input.Mock.motion = 0f;
                    break;
                case Scenario.GameOverTimeout:
                    _input.Mock.motion = 0f;
                    // Stand aside while aiming, then step across once it has locked.
                    if (combat.IsAiming) _lockedSide = combat.AimProgress >= 0.9f ? 1 : -1;
                    _input.Mock.position = new Vector2(0.5f + _lockedSide * 0.3f, 0.5f);
                    break;
                case Scenario.GameOverAbandon:
                    _input.Mock.motion = 0f;
                    _input.Mock.hasPosition = false;
                    break;
            }
        }

        private void OnPhaseChanged(GamePhase phase)
        {
            if (phase == GamePhase.Combat)
            {
                _combatStart = Time.unscaledTime;
                _dodged = 0;
                _parried = 0;
                _hits = 0;
                _combats++;
            }
            if (logEvents) Debug.Log($"[CombatDev] phase {phase} at {Time.unscaledTime:F2}s (frame {Time.frameCount}) result {_machine.Result}");
        }

        private void OnCombatEvent(CombatEvent e)
        {
            switch (e.type)
            {
                case CombatEventType.Dodged: _dodged++; break;
                case CombatEventType.Parried: _parried++; break;
                case CombatEventType.PlayerHit: _hits++; break;
            }
            if (logEvents) Debug.Log($"[CombatDev] {e.type} +{Time.unscaledTime - _combatStart:F2}s at ({e.position.x:F2},{e.position.y:F2}) boss {combat.BossHp01:F1} player {combat.PlayerHp01:F1}");
        }

        private void OnFinished(GameResult result)
        {
            Debug.Log($"[CombatDev] finished #{_combats} {result} ({scenario}) after {Time.unscaledTime - _combatStart:F2}s frame {Time.frameCount}: dodged {_dodged} parried {_parried} hits {_hits} shots in pool {PoolActive()}");
        }

        private int PoolActive()
        {
            var pool = combat.GetComponentInChildren<ProjectilePool>();
            return pool != null ? pool.ActiveCount : -1;
        }

        private void OnGUI()
        {
            if (!showOverlay || _machine == null) return;

            Color prev = GUI.color;
            float y = Pad;
            Bar(ref y, combat.BossHp01, BossBar);
            Bar(ref y, combat.PlayerHp01, PlayerBar);
            GUI.color = Color.white;

            PlayerSample sample = _input.Current;
            Line(ref y, $"phase {_machine.Phase}   result {_machine.Result}   gauge {_machine.AwakenCharge:F2}   left {combat.RemainingSec:F1}s");
            Line(ref y, $"input {(_input.UseReal ? "sensor" : "keyboard")} {sample.source}   pos ({sample.position.x:F2},{sample.position.y:F2}) has {sample.hasPosition}   motion {sample.motion:F2} / {_config.shieldThreshold:F2}   guard {sample.guardPose}");
            Line(ref y, $"shield {combat.ShieldActive}   aim {combat.AimProgress:F2}   pan {combat.BossPanDeg:F1}   tilt {combat.BossTiltDeg:F1}");
            Line(ref y, $"dodged {_dodged}   parried {_parried}   hits {_hits}   scenario {scenario}");

            if (_machine.Phase == GamePhase.Combat)
            {
                DrawAim();
                DrawPlayer(combat.PlayerPosition);
            }

            GUI.color = prev;
            if (_guideStyle == null) _guideStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, wordWrap = true };
            GUI.Label(new Rect(Pad, Screen.height - GuideHeight - Pad, Screen.width - Pad * 2f, GuideHeight), GuideText(), _guideStyle);
        }

        private string GuideText()
        {
            switch (_machine.Phase)
            {
                case GamePhase.MediaWall:
                    return "손을 계속 흔들면(키보드: Space 유지) 각성합니다. Tab: 센서/키보드 전환, 화살표: 위치, N: 입력 없음";
                case GamePhase.Awaken:
                    return "각성 중";
                case GamePhase.Combat:
                    return "조준선이 굵어지면 고정. 옆으로 피하면 무피해, 원 안에서 실드(팔 들기/G 또는 크게 흔들기/Space)면 패링";
                default:
                    return $"결과 {_machine.Result}";
            }
        }

        private void DrawAim()
        {
            if (!combat.IsAiming) return;

            float radius = _config.hitRadius * Screen.height;
            Vector2 center = ToGui(combat.AimPosition);
            GUI.color = AimRing;
            for (int i = 0; i < RingDots; i++)
            {
                float angle = i * Mathf.PI * 2f / RingDots;
                GUI.DrawTexture(new Rect(center.x + Mathf.Cos(angle) * radius - RingDot * 0.5f,
                    center.y + Mathf.Sin(angle) * radius - RingDot * 0.5f, RingDot, RingDot), Texture2D.whiteTexture);
            }
        }

        private void DrawPlayer(Vector2 position)
        {
            Vector2 center = ToGui(position);
            GUI.color = combat.ShieldActive ? ShieldMarker : PlayerMarker;
            float size = combat.ShieldActive ? MarkerSize * 2f : MarkerSize;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), Texture2D.whiteTexture);
        }

        private static Vector2 ToGui(Vector2 normalized) =>
            new Vector2(normalized.x * Screen.width, (1f - normalized.y) * Screen.height);

        private static void Bar(ref float y, float value, Color color)
        {
            GUI.color = BarBg;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth, BarHeight), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth * value, BarHeight), Texture2D.whiteTexture);
            y += BarHeight + Pad;
        }

        private static void Line(ref float y, string text)
        {
            GUI.Label(new Rect(Pad, y, BarWidth * 2f, LineHeight), text);
            y += LineHeight;
        }
    }
}
