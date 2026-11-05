using System;
using Unity.Collections;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Sensor
{
    /// <summary>
    /// FR-03 frame differencing. Converts grid pixels to luminance, compares with the previous
    /// frame and writes cells whose luminance delta exceeds the threshold into a <see cref="MotionFrame"/>.
    /// Plain C# (no MonoBehaviour) so it can be unit tested. No allocations after construction.
    /// </summary>
    public sealed class FrameDiffDetector
    {
        private const float LumaR = 0.299f;
        private const float LumaG = 0.587f;
        private const float LumaB = 0.114f;
        private const float ByteToUnit = 1f / 255f;

        private float[] _prev;
        private float[] _curr;
        private bool _hasPrev;

        public int Width { get; }
        public int Height { get; }
        public int CellCount { get; }

        /// <summary>
        /// When true, input row 0 is treated as the top row. Output coordinates and <see cref="Diff"/>
        /// always use row 0 = bottom.
        /// </summary>
        public bool FlipY { get; set; }

        /// <summary>
        /// Absolute luminance delta per cell from the last processed frame (0..1),
        /// indexed by output row (row 0 = bottom) * Width + col. All zero until the second frame.
        /// </summary>
        public float[] Diff { get; }

        /// <summary>True once a previous frame is stored and the next call can produce points.</summary>
        public bool HasPrevious => _hasPrev;

        public FrameDiffDetector(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (width * height > MotionProtocol.MaxPoints)
                throw new ArgumentException($"Grid {width}x{height} exceeds MotionProtocol.MaxPoints.");

            Width = width;
            Height = height;
            CellCount = width * height;
            _prev = new float[CellCount];
            _curr = new float[CellCount];
            Diff = new float[CellCount];
        }

        /// <summary>Forgets the previous frame. The next call only primes the buffer.</summary>
        public void Reset()
        {
            _hasPrev = false;
            Array.Clear(Diff, 0, CellCount);
        }

        /// <summary>Processes one grid frame from GPU readback.</summary>
        /// <returns>False on the first frame (buffer primed, frame untouched); true when <paramref name="frame"/> was filled.</returns>
        public bool Process(NativeArray<Color32> pixels, float threshold, MotionFrame frame)
        {
            if (!pixels.IsCreated || pixels.Length < CellCount)
                throw new ArgumentException("Pixel buffer smaller than grid.", nameof(pixels));

            for (int i = 0; i < CellCount; i++)
                _curr[i] = Luminance(pixels[i]);

            return Detect(threshold, frame);
        }

        /// <summary>Managed-array overload (tests, CPU sources).</summary>
        public bool Process(Color32[] pixels, float threshold, MotionFrame frame)
        {
            if (pixels == null || pixels.Length < CellCount)
                throw new ArgumentException("Pixel buffer smaller than grid.", nameof(pixels));

            for (int i = 0; i < CellCount; i++)
                _curr[i] = Luminance(pixels[i]);

            return Detect(threshold, frame);
        }

        /// <summary>Rec. 601 luma, 0..1.</summary>
        public static float Luminance(Color32 c)
        {
            return (LumaR * c.r + LumaG * c.g + LumaB * c.b) * ByteToUnit;
        }

        /// <summary>Maps a delta above threshold to 0..1 intensity.</summary>
        public static float Intensity(float delta, float threshold)
        {
            float range = 1f - threshold;
            if (range <= 0f) return 1f;
            float v = (delta - threshold) / range;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private bool Detect(float threshold, MotionFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            if (!_hasPrev)
            {
                _hasPrev = true;
                Swap();
                return false;
            }

            MotionPoint[] points = frame.Points;
            int capacity = points.Length;
            int count = 0;
            float invW = 1f / Width;
            float invH = 1f / Height;

            for (int inRow = 0; inRow < Height; inRow++)
            {
                int row = FlipY ? Height - 1 - inRow : inRow;
                int inBase = inRow * Width;
                int outBase = row * Width;
                float y = (row + 0.5f) * invH;

                for (int col = 0; col < Width; col++)
                {
                    int i = inBase + col;
                    float d = _curr[i] - _prev[i];
                    if (d < 0f) d = -d;
                    Diff[outBase + col] = d;

                    if (d > threshold && count < capacity)
                    {
                        points[count].x = (col + 0.5f) * invW;
                        points[count].y = y;
                        points[count].v = Intensity(d, threshold);
                        count++;
                    }
                }
            }

            frame.Count = count;
            Swap();
            return true;
        }

        private void Swap()
        {
            float[] t = _prev;
            _prev = _curr;
            _curr = t;
        }
    }
}
