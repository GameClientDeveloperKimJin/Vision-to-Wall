using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;

namespace VisionToWall.Device.Dev
{
    /// <summary>
    /// Dev scene for the device simulation. A keyboard-driven stand-in for <see cref="ICombatStateSource"/> feeds a
    /// <see cref="DeviceController"/>; the boss proxy turns by the raw angles and the virtual light by the packets,
    /// so both should point the same way.
    /// Keys: arrows = pan/tilt, R = center, 1/2 = jump to -90/+90 pan (speed limit check), S = auto sweep,
    /// T = tracking (off returns to 0), L = packet log, O = overlay. IMGUI allocates; turn the overlay off for GC checks.
    /// </summary>
    public sealed class DeviceDevBootstrap : MonoBehaviour
    {
        private sealed class MockCombat : ICombatStateSource
        {
            public float Pan;
            public float Tilt;

            public float BossHp01 => 1f;
            public float PlayerHp01 => 1f;
            public float RemainingSec => 0f;
            public bool ShieldActive => false;
            public bool IsAiming => false;
            public Vector2 AimPosition => new Vector2(0.5f, 0.5f);
            public float AimProgress => 0f;
            public float BossPanDeg => Pan;
            public float BossTiltDeg => Tilt;
            public event Action<CombatEvent> CombatEventRaised { add { } remove { } }
        }

        [SerializeField] private DeviceController controller;
        [SerializeField] private VirtualMovingLight movingLight;
        [SerializeField] private Camera viewCamera;
        [SerializeField, Tooltip("Turned by the raw angles from its rest pose facing the camera.")]
        private Transform bossProxy;
        [SerializeField, Tooltip("Turn the device on in memory without touching config.json.")]
        private bool forceEnabled = true;
        [SerializeField] private bool logPackets = true;
        [SerializeField] private bool sweep;
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(1f)] private float keyboardDegPerSec = 90f;
        [SerializeField, Min(0.01f)] private float sweepPeriodSec = 6f;

        private readonly MockCombat _combat = new MockCombat();
        private readonly byte[] _packet = new byte[DeviceProtocol.PacketSize];
        private LogPacketSink _sink;
        private Quaternion _proxyRest;
        private float _sweepStart;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();
            if (forceEnabled) config.deviceEnabled = true; // in memory only
            config.deviceLogPackets |= logPackets;

            _sink = new LogPacketSink(config.deviceLogPackets);
            controller.Initialize(config, _combat, _sink);
            movingLight.Initialize(controller, viewCamera);

            if (bossProxy != null)
            {
                _proxyRest = Quaternion.LookRotation(-viewCamera.transform.forward, viewCamera.transform.up);
                bossProxy.rotation = _proxyRest;
            }
            Debug.Log($"[DeviceDev] enabled {config.deviceEnabled} id {config.deviceId} hz {config.deviceSendHz} log {config.deviceLogPackets}");
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, DeviceController.MaxFrameDeltaSec);

            if (keyboard != null)
            {
                if (keyboard.sKey.wasPressedThisFrame) { sweep = !sweep; _sweepStart = Time.unscaledTime; }
                if (keyboard.tKey.wasPressedThisFrame) controller.Tracking = !controller.Tracking;
                if (keyboard.lKey.wasPressedThisFrame) _sink.LogPackets = !_sink.LogPackets;
                if (keyboard.oKey.wasPressedThisFrame) showOverlay = !showOverlay;
                if (keyboard.rKey.wasPressedThisFrame) { _combat.Pan = 0f; _combat.Tilt = 0f; }
                if (keyboard.digit1Key.wasPressedThisFrame) _combat.Pan = -90f;
                if (keyboard.digit2Key.wasPressedThisFrame) _combat.Pan = 90f;

                float step = keyboardDegPerSec * deltaTime;
                // Right arrow moves the target to the screen right, which is negative pan.
                if (keyboard.rightArrowKey.isPressed) _combat.Pan -= step;
                if (keyboard.leftArrowKey.isPressed) _combat.Pan += step;
                if (keyboard.upArrowKey.isPressed) _combat.Tilt += step;
                if (keyboard.downArrowKey.isPressed) _combat.Tilt -= step;
            }

            if (sweep)
            {
                float phase = (Time.unscaledTime - _sweepStart) / sweepPeriodSec * Mathf.PI * 2f;
                _combat.Pan = Mathf.Sin(phase) * 40f;
                _combat.Tilt = -20f + Mathf.Sin(phase * 0.5f) * 10f;
            }

            _combat.Pan = Mathf.Clamp(_combat.Pan, -200f, 200f); // past the protocol range to show the clamp
            _combat.Tilt = Mathf.Clamp(_combat.Tilt, -100f, 100f);

            if (bossProxy != null) bossProxy.rotation = _proxyRest * Quaternion.Euler(-_combat.Tilt, _combat.Pan, 0f);
        }

        private void OnGUI()
        {
            if (!showOverlay) return;
            float y = 8f;
            Line(ref y, $"target pan {_combat.Pan:F1}  tilt {_combat.Tilt:F1}   sent pan {controller.SentPanDeg:F1}  tilt {controller.SentTiltDeg:F1}");
            Line(ref y, $"light pan {movingLight.PanDeg:F2}  tilt {movingLight.TiltDeg:F2}   decoded {movingLight.DecodedPackets}  rejected {movingLight.RejectedPackets}");
            Line(ref y, $"sent {controller.PacketsSent}  sink {_sink.Count}  tracking {controller.Tracking}  sweep {sweep}  log {_sink.LogPackets}");
            string hex = controller.TryGetLastPacket(_packet) ? LogPacketSink.FormatHex(_packet) : "-";
            Line(ref y, $"last {hex}");
            Line(ref y, "arrows pan/tilt  R center  1/2 jump  S sweep  T tracking  L log  O overlay");
        }

        private static void Line(ref float y, string text)
        {
            GUI.Label(new Rect(8f, y, 900f, 22f), text);
            y += 22f;
        }
    }
}
