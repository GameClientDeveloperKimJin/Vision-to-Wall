namespace VisionToWall.Core
{
    /// <summary>
    /// Reusable motion frame buffer. Receivers must not keep a reference
    /// outside the event callback; call <see cref="CopyFrom"/> into their own frame instead.
    /// </summary>
    public sealed class MotionFrame
    {
        public readonly MotionPoint[] Points = new MotionPoint[MotionProtocol.MaxPoints];
        public int Count;
        public uint Seq;
        public uint TimestampMs;

        public void Clear()
        {
            Count = 0;
            Seq = 0;
            TimestampMs = 0;
        }

        public void CopyFrom(MotionFrame other)
        {
            if (other == null)
            {
                Clear();
                return;
            }

            Count = other.Count;
            Seq = other.Seq;
            TimestampMs = other.TimestampMs;
            System.Array.Copy(other.Points, Points, other.Count);
        }
    }
}
