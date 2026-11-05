namespace VisionToWall.Core
{
    /// <summary>
    /// UDP wire format constants. Layout (little-endian):
    /// [0] uint32 magic, [4] uint32 seq, [8] uint32 timestampMs, [12] uint16 count,
    /// [14] count * (float x, float y, float v).
    /// </summary>
    public static class MotionProtocol
    {
        public const uint Magic = 0x57325601; // "V2W" + version 1
        public const int HeaderSize = 14;
        public const int PointSize = 12;
        public const int MaxPoints = 4096;
        public const int MaxPacketSize = HeaderSize + PointSize * MaxPoints;
    }
}
