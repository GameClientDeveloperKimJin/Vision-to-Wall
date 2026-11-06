using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VisionToWall.Core;
using VisionToWall.Network;
using VisionToWall.Render;

namespace VisionToWall.Game.Dev
{
    /// <summary>
    /// Dev scene for the presentation on the real phase machine and combat rules. Tab switches sensor and keyboard;
    /// a scenario plays by itself. Until the planning session adds bloom to Main, this scene makes its own Volume.
    /// The iris is attached here because BossEye.prefab stays untouched until the model arrives.
    /// </summary>
    public sealed class PresentationDevBootstrap : MonoBehaviour
    {
        public enum Scenario { None, Victory, GameOverHits }

        private sealed class DevInput : IPlayerInputSource
        {
            public IPlayerInputSource Real;
            public bool UseReal;
            public PlayerSample Mock = new PlayerSample { position = new Vector2(0.3f, 0.35f), hasPosition = true };

            public PlayerSample Current => UseReal ? Real.Current : Mock;
        }

        [SerializeField] private BossCombatController combat;
        [SerializeField] private MediaWallController wall;
        [SerializeField] private Camera wallCamera;
        [SerializeField] private PhasePresenter phasePresenter;
        [SerializeField] private CombatPresenter combatPresenter;
        [SerializeField] private BossIrisView irisPrefab;
        [SerializeField, Tooltip("Local position on the boss. The temporary ball has radius 1.")]
        private Vector3 irisPosition = new Vector3(0f, 0f, 1.01f);
        [SerializeField, Min(0.01f)] private float irisSize = 2f;
        [SerializeField] private bool forceExternal = true;
        [SerializeField] private bool startWithSensor;
        [SerializeField] private Scenario scenario = Scenario.None;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool useBloom = true;
        [SerializeField, Min(0f)] private float bloomThreshold = 1.1f;
        [SerializeField, Min(0f)] private float bloomIntensity = 0.8f;
        [SerializeField, Range(0f, 1f)] private float bloomScatter = 0.7f;
        [SerializeField, Min(0.01f)] private float keyboardSpeed = 0.7f;

        private AppConfig _config;
        private DevInput _input;
        private GamePhaseMachine _machine;
        private ExhibitionPhaseGate _gate;
        private VolumeProfile _profile;

        public GamePhaseMachine Machine => _machine;

        private void Awake()
        {
            _config = ConfigService.Load();
            if (forceExternal) _config.sensorMode = AppConfig.SensorModeExternal; // in memory only

            UdpMotionReceiver motion = CreateReceiver("MotionReceiver", _config.udpPort);
            UdpMotionReceiver body = CreateReceiver("BodyReceiver", _config.udpBodyPort);

            var tracker = gameObject.AddComponent<PlayerInputTracker>();
            tracker.Initialize(_config, motion, body);
            _input = new DevInput { Real = tracker, UseReal = startWithSensor };

            AttachIris();

            var fsm = gameObject.AddComponent<ExhibitionStateMachine>();
            fsm.Initialize(_config, motion);

            combat.Initialize(_config, _input);

            _machine = gameObject.AddComponent<GamePhaseMachine>();
            _machine.Initialize(_config, _input, combat);

            _gate = new ExhibitionPhaseGate(fsm, _machine);
            wall.Initialize(motion, _gate);

            phasePresenter.Initialize(_machine, combat, wall, wallCamera, _config.awakenTransitionSec);
            combatPresenter.Initialize(_machine, combat, _config.hitRadius);

            if (useBloom) AddBloom();
        }

        private void OnDestroy()
        {
            _gate?.Dispose();
            if (_profile != null) Destroy(_profile);
        }

        private UdpMotionReceiver CreateReceiver(string objectName, int port)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var receiver = child.AddComponent<UdpMotionReceiver>();
            receiver.Initialize(_config, port);
            return receiver;
        }

        private void AttachIris()
        {
            if (irisPrefab == null) return;

            Transform boss = combat.GetComponentInChildren<BossLookAt>(true).transform;
            Transform pupil = boss.Find("Visual/Pupil");
            if (pupil == null) pupil = boss.Find("Pupil");
            if (pupil != null) pupil.gameObject.SetActive(false);

            BossIrisView iris = Instantiate(irisPrefab, boss);
            iris.transform.localPosition = irisPosition;
            iris.transform.localRotation = Quaternion.identity;
            iris.transform.localScale = Vector3.one * irisSize;
        }

        /// <summary>Post processing runs on the overlay camera, the last of the stack, so it covers boss and wall.</summary>
        private void AddBloom()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = _profile.Add<Bloom>(true);
            bloom.threshold.Override(bloomThreshold);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(bloomScatter);

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = _profile;

            combat.OverlayCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            combat.OverlayCamera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
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

            var move = Vector2.zero;
            if (keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.upArrowKey.isPressed) move.y += 1f;

            Vector2 next = _input.Mock.position + move * (keyboardSpeed * Time.unscaledDeltaTime);
            _input.Mock.position = new Vector2(Mathf.Clamp01(next.x), Mathf.Clamp01(next.y));
            _input.Mock.motion = keyboard.spaceKey.isPressed ? 1f : 0f;
        }

        private void PlayScenario()
        {
            if (_machine.Phase != GamePhase.Combat)
            {
                _input.Mock.motion = _machine.Phase == GamePhase.MediaWall ? 1f : 0f; // wakes the boss
                return;
            }

            _input.Mock.motion = scenario == Scenario.Victory && combat.AimProgress >= 0.9f ? 1f : 0f;
        }

        private void OnGUI()
        {
            if (!showOverlay || _machine == null) return;

            PresentationLook look = phasePresenter.Look;
            GUI.Label(new Rect(8f, 8f, 900f, 22f),
                $"phase {_machine.Phase} {_machine.Result}  gauge {_machine.AwakenCharge:F2}  boss {combat.BossHp01:F1} player {combat.PlayerHp01:F1}  left {combat.RemainingSec:F1}");
            GUI.Label(new Rect(8f, 30f, 900f, 22f),
                $"crimson {look.crimson:F2} absorb {look.absorb:F2} iris {look.irisOpen:F2} glow {look.irisGlow:F2} arena {look.backdrop:F2} time {Time.timeScale:F2}");
            GUI.Label(new Rect(8f, 52f, 900f, 22f),
                $"input {(_input.UseReal ? "sensor" : "keyboard")}  Tab switch, arrows move, Space shield / wake");
        }
    }
}
