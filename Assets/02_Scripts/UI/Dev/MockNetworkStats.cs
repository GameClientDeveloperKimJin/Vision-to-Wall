using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>Dev-only <see cref="INetworkStats"/> fed by <see cref="MockPointSource"/>. LastPacketTime is -1 before the first packet.</summary>
    public sealed class MockNetworkStats : MonoBehaviour, INetworkStats
    {
        [SerializeField] private MockPointSource source;

        private int _received;
        private uint _lastSeq;
        private float _lastTime = -1f;

        public int PacketsReceived => _received;
        public int PacketsDropped => source != null ? source.SimulatedDrops : 0;
        public uint LastSeq => _lastSeq;
        public float LastPacketTime => _lastTime;

        private void Awake()
        {
            if (source == null) source = GetComponent<MockPointSource>();
        }

        private void OnEnable()
        {
            if (source != null) source.FrameReceived += OnFrame;
        }

        private void OnDisable()
        {
            if (source != null) source.FrameReceived -= OnFrame;
        }

        private void OnFrame(MotionFrame frame)
        {
            _received++;
            _lastSeq = frame.Seq;
            _lastTime = Time.unscaledTime;
        }
    }
}
