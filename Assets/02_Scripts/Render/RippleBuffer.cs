using UnityEngine;

namespace VisionToWall.Render
{
    /// <summary>
    /// Fixed-size ripple ring buffer that feeds the RippleWall shader (<c>_Ripples[32]</c>).
    /// Each entry is (x, y, startTime, strength) in normalized wall coordinates.
    /// A new ripple close to a recent one (distance &lt; <see cref="MinDistance"/> within
    /// <see cref="SuppressSec"/>) is rejected so 25~60 Hz packet streams do not flood the wall.
    /// When full, the oldest entry is overwritten. Allocation free after construction.
    /// </summary>
    public sealed class RippleBuffer
    {
        public const int Capacity = 32;
        public const float DefaultMinDistance = 0.05f;
        public const float DefaultSuppressSec = 0.15f;

        /// <summary>Shader-ready data. Unused slots are zero. Do not resize.</summary>
        public readonly Vector4[] Data = new Vector4[Capacity];

        public float MinDistance = DefaultMinDistance;
        public float SuppressSec = DefaultSuppressSec;

        private int _next;
        private int _count;

        /// <summary>Number of valid entries (0..Capacity).</summary>
        public int Count => _count;

        /// <summary>Adds a ripple unless one exists nearby that started less than SuppressSec ago.</summary>
        /// <returns>True if added.</returns>
        public bool TryAdd(float x, float y, float time, float strength)
        {
            float minSqr = MinDistance * MinDistance;
            for (int i = 0; i < _count; i++)
            {
                Vector4 r = Data[i];
                float age = time - r.z;
                if (age < 0f || age >= SuppressSec) continue;

                float dx = x - r.x;
                float dy = y - r.y;
                if (dx * dx + dy * dy < minSqr) return false;
            }

            Data[_next] = new Vector4(x, y, time, strength);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            return true;
        }

        public void Clear()
        {
            System.Array.Clear(Data, 0, Capacity);
            _next = 0;
            _count = 0;
        }
    }
}
