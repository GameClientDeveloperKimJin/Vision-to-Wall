namespace VisionToWall.Core
{
    /// <summary>
    /// RS-232/485 style pan/tilt packet for a moving light or servo (v2.1). 8 bytes:
    /// [0] STX 0x02, [1] device id, [2..3] pan int16 big-endian (0.01 deg), [4..5] tilt int16 big-endian (0.01 deg),
    /// [6] checksum = XOR of bytes 1..5, [7] ETX 0x03.
    /// Pan: screen right is negative (same sign as ICombatStateSource.BossPanDeg). Tilt: up is positive.
    /// Out of range angles are clamped. See Assets/05_Docs/51_세션R1_DeviceControl.md.
    /// </summary>
    public static class DeviceProtocol
    {
        public const byte Stx = 0x02;
        public const byte Etx = 0x03;
        public const int PacketSize = 8;
        public const float CentiDegreesPerDegree = 100f;

        public const float PanMinDeg = -180f;
        public const float PanMaxDeg = 180f;
        public const float TiltMinDeg = -90f;
        public const float TiltMaxDeg = 90f;

        public const int IdIndex = 1;
        public const int PanIndex = 2;
        public const int TiltIndex = 4;
        public const int ChecksumIndex = 6;
        public const int EtxIndex = 7;
    }
}
