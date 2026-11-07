using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Device
{
    /// <summary>
    /// Reads <see cref="ICombatStateSource.BossPanDeg"/>/<see cref="ICombatStateSource.BossTiltDeg"/>, limits the
    /// turn speed, encodes at <c>deviceSendHz</c> and hands the packet to an <see cref="IDevicePacketSink"/>.
    /// With <c>deviceEnabled: false</c> it disables itself and does nothing. Timing uses <see cref="Time.unscaledTime"/>
    /// so the result slow motion does not change the send rate. No per-frame allocation.
    /// </summary>
    public sealed class DeviceController : MonoBehaviour
    {
        /// <summary>Same cap as GamePhaseMachine.MaxFrameDeltaSec (the Play entry frame once measured 3.8 s).</summary>
        public const float MaxFrameDeltaSec = 0.1f;

        [SerializeField, Min(0f), Tooltip("Device protection: the sent angle turns no faster than this.")]
        private float maxDegPerSec = 180f;

        private readonly byte[] _packet = new byte[DeviceProtocol.PacketSize];
        private ICombatStateSource _combat;
        private IDevicePacketSink _sink;
        private DeviceSendLoop _loop;
        private byte _deviceId;
        private bool _hasPacket;

        public bool IsActive => _loop != null && enabled;
        public int PacketsSent { get; private set; }
        /// <summary>Angles of the last sent packet before clamping, after the speed limit.</summary>
        public float SentPanDeg => _loop != null ? _loop.PanDeg : 0f;
        public float SentTiltDeg => _loop != null ? _loop.TiltDeg : 0f;

        /// <summary>
        /// False = outside combat: the device turns back to 0 at the speed limit. The boss also returns to its
        /// rest pose then, so the default (true) already gives 0; set it when the boss is hidden but not at rest.
        /// </summary>
        public bool Tracking { get; set; } = true;

        public void Initialize(AppConfig config, ICombatStateSource combat, IDevicePacketSink sink)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (!config.deviceEnabled)
            {
                _loop = null;
                enabled = false;
                return;
            }

            _combat = combat;
            _sink = sink;
            _deviceId = (byte)Mathf.Clamp(config.deviceId, 0, 255);
            _loop = new DeviceSendLoop(config.deviceSendHz, maxDegPerSec);
            _hasPacket = false;
            PacketsSent = 0;
            enabled = true;
        }

        /// <summary>Copies the last sent packet (for the F1 panel and the virtual light).</summary>
        /// <returns>False before the first packet or when <paramref name="destination"/> is too short.</returns>
        public bool TryGetLastPacket(Span<byte> destination)
        {
            if (!_hasPacket || destination.Length < DeviceProtocol.PacketSize) return false;
            _packet.CopyTo(destination);
            return true;
        }

        private void Update()
        {
            if (_loop == null) return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSec);
            float pan = 0f;
            float tilt = 0f;
            if (Tracking && _combat != null)
            {
                pan = _combat.BossPanDeg;
                tilt = _combat.BossTiltDeg;
            }

            _loop.MaxDegPerSec = maxDegPerSec;
            if (!_loop.Step(Time.unscaledTime, deltaTime, pan, tilt)) return;

            DevicePacketEncoder.TryEncode(_packet, _deviceId, _loop.PanDeg, _loop.TiltDeg);
            _hasPacket = true;
            PacketsSent++;
            _sink?.Send(_packet);
        }
    }
}
