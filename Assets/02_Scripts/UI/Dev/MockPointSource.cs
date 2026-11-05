using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>
    /// Dev-only <see cref="IPointSource"/> for the UI scene. ~25 Hz like the real receiver.
    /// A blob sweeps for <see cref="onSec"/>, then empty frames for <see cref="offSec"/> (drives Idle/Active).
    /// <see cref="paused"/> stops sending (watch "Last seq ... s ago" grow); <see cref="dropChance"/> skips seq numbers.
    /// </summary>
    public sealed class MockPointSource : MonoBehaviour, IPointSource
    {
        [SerializeField, Range(1f, 120f)] private float packetHz = 25f;
        [SerializeField] private bool paused;
        [SerializeField, Range(1, 64)] private int gridWidth = 16;
        [SerializeField, Range(1, 64)] private int gridHeight = 16;
        [SerializeField, Min(0f)] private float onSec = 5f;
        [SerializeField, Min(0f)] private float offSec = 7f;
        [Tooltip("Chance per packet that it is 'lost' (seq skips, counted as a drop).")]
        [SerializeField, Range(0f, 0.5f)] private float dropChance = 0.02f;

        private readonly MotionFrame _frame = new MotionFrame();
        private float _nextSend;
        private float _start;
        private uint _seq;

        public event Action<MotionFrame> FrameReceived;

        public int SimulatedDrops { get; private set; }
        public int GridWidth => gridWidth;
        public int GridHeight => gridHeight;

        private void OnEnable() => _start = Time.unscaledTime;

        private void Update()
        {
            float now = Time.unscaledTime;
            if (paused || now < _nextSend) return;
            _nextSend = Mathf.Max(_nextSend + 1f / packetHz, now - 0.1f);

            _seq++;
            if (UnityEngine.Random.value < dropChance)
            {
                SimulatedDrops++;
                return;
            }

            _frame.Count = 0;
            float t = now - _start;
            float period = onSec + offSec;
            if (period <= 0f || t % period < onSec)
            {
                float x = 0.5f + 0.35f * Mathf.Sin(t * 2.4f);
                float y = 0.5f + 0.2f * Mathf.Sin(t * 3.7f);
                AddBlob(x, y);
            }

            _frame.Seq = _seq;
            _frame.TimestampMs = (uint)(now * 1000f);
            FrameReceived?.Invoke(_frame);
        }

        private void AddBlob(float nx, float ny)
        {
            int cx = Mathf.FloorToInt(Mathf.Clamp01(nx) * gridWidth);
            int cy = Mathf.FloorToInt(Mathf.Clamp01(ny) * gridHeight);
            for (int gy = cy - 1; gy <= cy + 1; gy++)
            {
                if (gy < 0 || gy >= gridHeight) continue;
                for (int gx = cx - 1; gx <= cx + 1; gx++)
                {
                    if (gx < 0 || gx >= gridWidth) continue;
                    if (_frame.Count >= MotionProtocol.MaxPoints) return;
                    float v = (gx == cx && gy == cy) ? 1f : UnityEngine.Random.Range(0.2f, 0.7f);
                    _frame.Points[_frame.Count++] = new MotionPoint(
                        (gx + 0.5f) / gridWidth, (gy + 0.5f) / gridHeight, v);
                }
            }
        }
    }
}
