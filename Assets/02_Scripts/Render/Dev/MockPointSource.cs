using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;

namespace VisionToWall.Render.Dev
{
    /// <summary>
    /// Dev-only <see cref="IPointSource"/>. Left mouse drag = a blob of points under the cursor.
    /// With no drag, an optional auto pattern sweeps a "waving hand" for 5 s, then sends empty frames for 7 s
    /// (exercises the 5 s Idle timeout). Mimics real input traits: ~25 Hz packets, flickering point counts.
    /// </summary>
    public sealed class MockPointSource : MonoBehaviour, IPointSource
    {
        [SerializeField, Range(1f, 120f)] private float packetHz = 25f;

        [Header("Mouse")]
        [SerializeField] private bool mouseEnabled = true;
        [Tooltip("Blob radius in grid cells.")]
        [SerializeField, Range(0, 4)] private int blobRadius = 1;

        [Header("Auto pattern")]
        [SerializeField] private bool autoPattern = true;
        [SerializeField, Min(0f)] private float onSec = 5f;
        [SerializeField, Min(0f)] private float offSec = 7f;
        [SerializeField, Min(0f)] private float waveHz = 1.2f;

        [Header("Realism")]
        [SerializeField, Range(1, 64)] private int gridWidth = 16;
        [SerializeField, Range(1, 64)] private int gridHeight = 16;
        [Tooltip("Chance that each point is dropped from a frame (real counts swing frame to frame).")]
        [SerializeField, Range(0f, 0.9f)] private float flicker = 0.3f;

        private readonly MotionFrame _frame = new MotionFrame();
        private float _nextSend;
        private float _patternStart;
        private uint _seq;

        public event Action<MotionFrame> FrameReceived;

        /// <summary>True while points are being produced (mouse held or pattern ON).</summary>
        public bool IsProducing { get; private set; }

        private void OnEnable() => _patternStart = Time.unscaledTime;

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextSend) return;
            _nextSend = Mathf.Max(_nextSend + 1f / packetHz, now - 0.1f);

            _frame.Count = 0;

            Mouse mouse = mouseEnabled ? Mouse.current : null;
            if (mouse != null && mouse.leftButton.isPressed && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 pos = mouse.position.ReadValue();
                AddBlob(pos.x / Screen.width, pos.y / Screen.height, 1f);
            }
            else if (autoPattern && PatternOn(now))
            {
                float t = now - _patternStart;
                float x = 0.5f + 0.3f * Mathf.Sin(t * waveHz * Mathf.PI * 2f);
                float y = 0.45f + 0.08f * Mathf.Sin(t * waveHz * Mathf.PI * 4f);
                // Speed ~ |cos|: drops to ~0 at each direction reversal, like real frame differencing.
                float speed = Mathf.Abs(Mathf.Cos(t * waveHz * Mathf.PI * 2f));
                AddBlob(x, y, speed);
            }

            IsProducing = _frame.Count > 0;
            _frame.Seq = ++_seq;
            _frame.TimestampMs = (uint)(now * 1000f);
            FrameReceived?.Invoke(_frame);
        }

        private bool PatternOn(float now)
        {
            float period = onSec + offSec;
            if (period <= 0f) return true;
            return (now - _patternStart) % period < onSec;
        }

        private void AddBlob(float nx, float ny, float strength)
        {
            int cx = Mathf.FloorToInt(Mathf.Clamp01(nx) * gridWidth);
            int cy = Mathf.FloorToInt(Mathf.Clamp01(ny) * gridHeight);
            float maxD = blobRadius + 1f;

            for (int gy = cy - blobRadius; gy <= cy + blobRadius; gy++)
            {
                if (gy < 0 || gy >= gridHeight) continue;
                for (int gx = cx - blobRadius; gx <= cx + blobRadius; gx++)
                {
                    if (gx < 0 || gx >= gridWidth) continue;
                    if (UnityEngine.Random.value < flicker) continue;

                    float d = Mathf.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                    float v = Mathf.Clamp01(strength * (1f - d / maxD) * UnityEngine.Random.Range(0.7f, 1f));
                    if (v <= 0.02f) continue;
                    if (_frame.Count >= MotionProtocol.MaxPoints) return;

                    _frame.Points[_frame.Count++] = new MotionPoint(
                        (gx + 0.5f) / gridWidth, (gy + 0.5f) / gridHeight, v);
                }
            }
        }
    }
}
