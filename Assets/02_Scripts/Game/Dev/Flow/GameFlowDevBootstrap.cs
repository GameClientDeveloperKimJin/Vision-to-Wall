using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;
using VisionToWall.Render;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// Dev scene for <see cref="GamePhaseMachine"/>: phase, gauge and gate overlay plus the real attract text.
    /// Pair with Tools/HandSensor (this scene never opens the webcam) or Tools/FakeSender/fake_sender.py.
    /// IMGUI allocates; turn the overlay off when profiling GC.
    /// </summary>
    public sealed class GameFlowDevBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Treat the sensor as external without touching config.json. Off = use config sensorMode.")]
        private bool forceExternal = true;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private AttractModeView attractView;
        [SerializeField] private MockCombatSession mockCombat;

        private const float Pad = 8f;
        private const float LineHeight = 22f;
        private const float BarWidth = 400f;
        private const float BarHeight = 18f;
        private const float GuideHeight = 90f;
        private static readonly Color BarBg = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color BarFill = new Color(1f, 0.7f, 0.1f, 1f);

        private AppConfig _config;
        private PlayerInputTracker _tracker;
        private ExhibitionStateMachine _fsm;
        private GamePhaseMachine _machine;
        private ExhibitionPhaseGate _gate;
        private UdpMotionReceiver _motion;
        private bool _motionAboveMin;
        private bool _gaugeFalling;
        private float _lastCharge;
        private GUIStyle _guideStyle;

        public GamePhaseMachine Machine => _machine;
        public ExhibitionPhaseGate Gate => _gate;

        private void Awake()
        {
            _config = ConfigService.Load();
            if (forceExternal) _config.sensorMode = AppConfig.SensorModeExternal; // in memory only

            _motion = CreateReceiver("MotionReceiver", _config.udpPort);
            UdpMotionReceiver body = CreateReceiver("BodyReceiver", _config.udpBodyPort);

            _tracker = gameObject.AddComponent<PlayerInputTracker>();
            _tracker.Initialize(_config, _motion, body);

            _fsm = gameObject.AddComponent<ExhibitionStateMachine>();
            _fsm.Initialize(_config, _motion);

            _machine = gameObject.AddComponent<GamePhaseMachine>();
            _machine.PhaseChanged += LogPhase;
            _machine.Initialize(_config, _tracker, mockCombat);

            _gate = new ExhibitionPhaseGate(_fsm, _machine);
            if (attractView != null) attractView.Bind(_gate);
        }

        private void OnDestroy()
        {
            _gate?.Dispose();
            if (_machine != null) _machine.PhaseChanged -= LogPhase;
        }

        private UdpMotionReceiver CreateReceiver(string objectName, int port)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var receiver = child.AddComponent<UdpMotionReceiver>();
            receiver.Initialize(_config, port);
            return receiver;
        }

        private void LogPhase(GamePhase phase)
        {
            Debug.Log($"[Flow] {phase} at {Time.unscaledTime:F2}s (frame {Time.frameCount}) result {_machine.Result}");
        }

        private void Update()
        {
            if (_machine.Phase != GamePhase.MediaWall)
            {
                _motionAboveMin = false;
                _gaugeFalling = false;
                _lastCharge = 0f; // the gauge is emptied on return; that is not a decay
                return;
            }

            float motion = _tracker.Current.motion;
            bool above = motion >= _config.awakenMinMotion;
            if (above != _motionAboveMin)
            {
                _motionAboveMin = above;
                Debug.Log($"[Flow] motion {(above ? "above" : "below")} min at {Time.unscaledTime:F2}s ({motion:F2})");
            }

            float charge = _machine.AwakenCharge;
            bool falling = charge < _lastCharge;
            if (falling && !_gaugeFalling) Debug.Log($"[Flow] gauge falling at {Time.unscaledTime:F2}s ({charge:F2})");
            if (charge != _lastCharge) _gaugeFalling = falling;
            _lastCharge = charge;
        }

        private void OnGUI()
        {
            if (!showOverlay || _machine == null) return;

            Color prev = GUI.color;
            float y = Pad;
            GUI.color = BarBg;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth, BarHeight), Texture2D.whiteTexture);
            GUI.color = BarFill;
            GUI.DrawTexture(new Rect(Pad, y, BarWidth * _machine.AwakenCharge, BarHeight), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += BarHeight + Pad;

            Line(ref y, $"phase {_machine.Phase}   elapsed {_machine.PhaseElapsedSec:F1}s   result {_machine.Result}");
            Line(ref y, $"gauge {_machine.AwakenCharge:F2}   cooldown {_machine.RearmCooldownLeftSec:F1}s   motion {_tracker.Current.motion:F2}");
            Line(ref y, $"exhibition {_fsm.Current} -> gate {_gate.Current}");
            Line(ref y, $"motion :{_config.udpPort} recv {_motion.PacketsReceived} drop {_motion.PacketsDropped}");
            GUI.color = prev;

            if (_guideStyle == null) _guideStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, wordWrap = true };
            GUI.Label(new Rect(Pad, Screen.height - GuideHeight - Pad, Screen.width - Pad * 2f, GuideHeight),
                GuideText(), _guideStyle);
        }

        /// <summary>What the tester should do and expect right now (33_세션G2_GameFlow.md, human checks).</summary>
        private string GuideText()
        {
            switch (_machine.Phase)
            {
                case GamePhase.Awaken:
                    return "각성 중. 손을 내리고 기다리세요";
                case GamePhase.Combat:
                    return "전투 중. 가만히 있으세요. 가운데 안내 문구가 나오면 안 됩니다";
                case GamePhase.Result:
                    return "결과 중. 가만히 있다가 미디어 월로 돌아오면 바로 계속 흔드세요";
                default:
                    return _machine.RearmCooldownLeftSec > 0f
                        ? "쿨다운 중. 계속 흔드세요. 0초가 될 때까지 게이지가 차면 안 됩니다"
                        : "손을 계속 흔들면 게이지가 찹니다";
            }
        }

        private static void Line(ref float y, string text)
        {
            GUI.Label(new Rect(Pad, y, BarWidth * 2f, LineHeight), text);
            y += LineHeight;
        }
    }
}
