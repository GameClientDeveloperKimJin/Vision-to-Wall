using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Sensor.Dev
{
    /// <summary>
    /// Dev-only <see cref="IMotionFrameSink"/> mock. Tracks per-frame stats and logs a summary
    /// at a fixed interval (logging allocates; disable it when profiling GC).
    /// </summary>
    public sealed class LogSink : IMotionFrameSink
    {
        public bool LogEnabled = true;
        public float LogIntervalSec = 0.5f;

        public int FramesSubmitted { get; private set; }
        public int LastCount { get; private set; }
        public float LastMeanX { get; private set; }
        public float LastMeanY { get; private set; }
        public float LastMaxV { get; private set; }
        public uint LastSeq { get; private set; }

        private float _nextLogTime;

        public void Submit(MotionFrame frame)
        {
            FramesSubmitted++;
            LastSeq = frame.Seq;
            LastCount = frame.Count;

            float sx = 0f, sy = 0f, maxV = 0f;
            for (int i = 0; i < frame.Count; i++)
            {
                MotionPoint p = frame.Points[i];
                sx += p.x;
                sy += p.y;
                if (p.v > maxV) maxV = p.v;
            }

            LastMeanX = frame.Count > 0 ? sx / frame.Count : 0f;
            LastMeanY = frame.Count > 0 ? sy / frame.Count : 0f;
            LastMaxV = maxV;

            if (!LogEnabled || Time.unscaledTime < _nextLogTime) return;
            _nextLogTime = Time.unscaledTime + LogIntervalSec;

            if (frame.Count > 0)
                Debug.Log($"[LogSink] seq={frame.Seq} count={frame.Count} meanX={LastMeanX:F2} meanY={LastMeanY:F2} maxV={maxV:F2}");
            else
                Debug.Log($"[LogSink] seq={frame.Seq} count=0");
        }
    }
}
