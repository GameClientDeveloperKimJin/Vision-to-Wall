namespace VisionToWall.Core
{
    /// <summary>
    /// Pose joint stream (UDP, default port 9002), sent by Tools/HandSensor. Layout (little-endian):
    /// [0] uint32 magic, [4] uint32 seq, [8] uint32 timestampMs, [12] uint16 count (0 = nobody, else JointCount),
    /// [14] count * 6 float32: screenX, screenY, worldX, worldY, worldZ, visibility.
    /// Screen: 0..1, origin bottom-left, mirror applied (same as ports 9000/9001).
    /// World: meters, hip centered, Unity axes: +x right (mirror applied), +y up, +z away from the camera.
    /// Joint left/right is the player's own body side, not the screen side.
    /// See Assets/05_Docs/52_세션R2_PoseStream.md.
    /// </summary>
    public static class PoseProtocol
    {
        public const uint Magic = 0x57325602; // "V2W" + 2
        public const int HeaderSize = 14;
        public const int JointCount = 13;
        public const int FloatsPerJoint = 6;
        public const int JointSize = FloatsPerJoint * 4;
        public const int PacketSize = HeaderSize + JointSize * JointCount; // 326
        public const int EmptyPacketSize = HeaderSize;

        /// <summary>MediaPipe Pose landmark index of each joint, in packet order.</summary>
        public static readonly int[] MediaPipeIndex = { 0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28 };
    }

    /// <summary>Joint order in <see cref="PoseProtocol"/> packets.</summary>
    public enum PoseJoint
    {
        Nose = 0,
        LeftShoulder = 1,
        RightShoulder = 2,
        LeftElbow = 3,
        RightElbow = 4,
        LeftWrist = 5,
        RightWrist = 6,
        LeftHip = 7,
        RightHip = 8,
        LeftKnee = 9,
        RightKnee = 10,
        LeftAnkle = 11,
        RightAnkle = 12
    }
}
