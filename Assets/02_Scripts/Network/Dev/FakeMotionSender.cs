using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network.Dev
{
    /// <summary>
    /// Test-only UDP source: rotating circle points at a fixed rate, 5 s ON / 7 s OFF by default.
    /// Drop it on any GameObject in a Dev scene; it adds and drives a <see cref="UdpMotionSender"/>,
    /// so packets use the real wire format and seq handling. Do not run it alongside the real Sensor
    /// sender on the same port (two seq streams make the receiver drop packets).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FakeMotionSender : MonoBehaviour
    {
        [SerializeField] private string ip = "127.0.0.1";
        [SerializeField] private int port = 9000;
        [SerializeField, Tooltip("Packets per second. Capped by the frame rate (one packet per Update at most).")]
        [Min(1f)] private float sendHz = 60f;
        [SerializeField] private FakeMotionGenerator generator = new FakeMotionGenerator();

        private UdpMotionSender _sender;
        private readonly MotionFrame _frame = new MotionFrame();
        private float _nextSendTime;

        public UdpMotionSender Sender => _sender;

        private void Awake()
        {
            _sender = gameObject.AddComponent<UdpMotionSender>();
            _sender.Initialize(new AppConfig { udpIp = ip, udpPort = port });
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextSendTime) return;

            float interval = 1f / sendHz;
            // Keep cadence; resync after a long hitch instead of bursting.
            _nextSendTime = now - _nextSendTime > interval ? now + interval : _nextSendTime + interval;

            generator.Fill(_frame, now);
            _sender.Submit(_frame);
        }
    }
}
