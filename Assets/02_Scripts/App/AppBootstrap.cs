using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;
using VisionToWall.Render;
using VisionToWall.Sensor;

namespace VisionToWall.App
{
    /// <summary>
    /// Composition root. The only place where modules are wired together.
    /// Wired so far: Network (receiver, sender) -> Sensor, Render (fsm, wall).
    /// Pending: UI (panel).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("Network (Session B)")]
        [SerializeField] private UdpMotionReceiver receiver;
        [SerializeField] private UdpMotionSender sender;

        [Header("Sensor (Session A)")]
        [SerializeField] private WebcamMotionSensor sensor;

        [Header("Render (Session C) - MediaWall prefab root")]
        [SerializeField] private ExhibitionStateMachine fsm;
        [SerializeField] private MediaWallController wall;

        [Header("Integration log (until the F1 panel lands)")]
        [SerializeField] private bool logReceived = true;
        [SerializeField, Min(0.25f)] private float logIntervalSec = 1f;

        private int _framesSinceLog;
        private int _pointsSinceLog;
        private float _nextLogTime;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();

            if (!Validate()) return;

            // Receiver first so the port is open before the first packet.
            receiver.Initialize(config);
            sender.Initialize(config);
            sensor.Initialize(config, sender);
            fsm.Initialize(config, receiver);
            wall.Initialize(receiver, fsm);

            // Phase 4 (UI): panel.Initialize(sensor, receiver, receiver, fsm);
        }

        private void OnEnable()
        {
            if (receiver != null) receiver.FrameReceived += OnFrameReceived;
        }

        private void OnDisable()
        {
            if (receiver != null) receiver.FrameReceived -= OnFrameReceived;
        }

        private void OnFrameReceived(MotionFrame frame)
        {
            if (!logReceived) return;

            _framesSinceLog++;
            _pointsSinceLog += frame.Count;

            float now = Time.unscaledTime;
            if (now < _nextLogTime) return;
            _nextLogTime = now + logIntervalSec;

            Debug.Log($"[App] frames={_framesSinceLog} points={_pointsSinceLog} lastSeq={frame.Seq} " +
                      $"recv={receiver.PacketsReceived} drop={receiver.PacketsDropped} state={fsm.Current} " +
                      $"camera={(sensor.IsRunning ? sensor.DeviceName : "not running")}");
            _framesSinceLog = 0;
            _pointsSinceLog = 0;
        }

        private bool Validate()
        {
            bool ok = true;
            if (receiver == null) { Debug.LogError("[App] UdpMotionReceiver not assigned."); ok = false; }
            if (sender == null) { Debug.LogError("[App] UdpMotionSender not assigned."); ok = false; }
            if (sensor == null) { Debug.LogError("[App] WebcamMotionSensor not assigned."); ok = false; }
            if (fsm == null) { Debug.LogError("[App] ExhibitionStateMachine not assigned."); ok = false; }
            if (wall == null) { Debug.LogError("[App] MediaWallController not assigned."); ok = false; }
            return ok;
        }
    }
}
