using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network.Dev
{
    /// <summary>
    /// Fills a <see cref="MotionFrame"/> with points rotating on a circle, with an ON/OFF duty cycle
    /// (OFF = count 0). Shared by <see cref="FakeMotionSender"/> and <see cref="NetworkDevBootstrap"/>.
    /// </summary>
    [System.Serializable]
    public sealed class FakeMotionGenerator
    {
        [Range(0, 64)] public int pointCount = 4;
        [Tooltip("Degrees per second.")] public float rotationSpeed = 90f;
        [Range(0f, 0.5f)] public float radius = 0.3f;
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("ON/OFF cycle for Render Idle-transition tests. Disable for continuous motion.")]
        public bool dutyCycle = true;
        [Min(0f)] public float onSec = 5f;
        [Min(0f)] public float offSec = 7f;

        private const float CenterX = 0.5f;
        private const float CenterY = 0.5f;

        /// <summary>True while the generator is in the ON part of the cycle at <paramref name="time"/>.</summary>
        public bool IsOn(float time)
        {
            if (!dutyCycle) return true;
            float period = onSec + offSec;
            if (period <= 0f) return true;
            return time % period < onSec;
        }

        public void Fill(MotionFrame frame, float time)
        {
            int count = IsOn(time) ? Mathf.Clamp(pointCount, 0, MotionProtocol.MaxPoints) : 0;
            float baseRad = time * rotationSpeed * Mathf.Deg2Rad;
            float step = count > 0 ? Mathf.PI * 2f / count : 0f;

            MotionPoint[] points = frame.Points;
            for (int i = 0; i < count; i++)
            {
                float a = baseRad + step * i;
                points[i] = new MotionPoint(
                    CenterX + Mathf.Cos(a) * radius,
                    CenterY + Mathf.Sin(a) * radius,
                    intensity);
            }

            frame.Count = count;
            frame.Seq = 0;          // sender assigns wire seq
            frame.TimestampMs = 0;  // sender stamps send time
        }
    }
}
