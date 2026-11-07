using System;
using System.Buffers.Binary;
using VisionToWall.Core;

namespace VisionToWall.Network
{
    /// <summary>
    /// <see cref="PoseFrame"/> &lt;-&gt; little-endian byte layout defined in <see cref="PoseProtocol"/>.
    /// Allocation free: the caller owns every buffer.
    /// </summary>
    public static class PosePacketSerializer
    {
        private const int OffsetMagic = 0;
        private const int OffsetSeq = 4;
        private const int OffsetTimestamp = 8;
        private const int OffsetCount = 12;

        /// <summary>
        /// Writes <paramref name="frame"/> using <paramref name="seq"/> as the wire seq. Count is 0 when
        /// <see cref="PoseFrame.HasPerson"/> is false, else <see cref="PoseProtocol.JointCount"/>. Returns the length.
        /// </summary>
        public static int Write(PoseFrame frame, uint seq, byte[] buffer)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int count = frame.HasPerson ? PoseProtocol.JointCount : 0;
            int length = PoseProtocol.HeaderSize + count * PoseProtocol.JointSize;
            if (buffer.Length < length)
                throw new ArgumentException("Buffer smaller than packet; use PoseProtocol.PacketSize.", nameof(buffer));

            Span<byte> span = buffer;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetMagic), PoseProtocol.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetSeq), seq);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetTimestamp), frame.TimestampMs);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OffsetCount), (ushort)count);

            int o = PoseProtocol.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                WriteFloat(span, o, frame.Screen[i].x);
                WriteFloat(span, o + 4, frame.Screen[i].y);
                WriteFloat(span, o + 8, frame.World[i].x);
                WriteFloat(span, o + 12, frame.World[i].y);
                WriteFloat(span, o + 16, frame.World[i].z);
                WriteFloat(span, o + 20, frame.Visibility[i]);
                o += PoseProtocol.JointSize;
            }

            return length;
        }

        /// <summary>
        /// Parses a packet into <paramref name="dst"/>. Returns false on bad magic, length or count
        /// (count must be 0 or <see cref="PoseProtocol.JointCount"/>); <paramref name="dst"/> is undefined on failure.
        /// Count 0 sets <see cref="PoseFrame.HasPerson"/> false and zeroes the joints.
        /// </summary>
        public static bool TryRead(byte[] buffer, int length, PoseFrame dst)
        {
            if (buffer == null || dst == null) return false;
            if (length < PoseProtocol.HeaderSize || length > buffer.Length) return false;

            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer, 0, length);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetMagic)) != PoseProtocol.Magic) return false;

            int count = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(OffsetCount));
            if (count != 0 && count != PoseProtocol.JointCount) return false;
            if (length != PoseProtocol.HeaderSize + count * PoseProtocol.JointSize) return false;

            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetSeq));
            uint timestamp = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetTimestamp));

            if (count == 0)
            {
                dst.Clear();
                dst.Seq = seq;
                dst.TimestampMs = timestamp;
                return true;
            }

            int o = PoseProtocol.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                dst.Screen[i].x = ReadFloat(span, o);
                dst.Screen[i].y = ReadFloat(span, o + 4);
                dst.World[i].x = ReadFloat(span, o + 8);
                dst.World[i].y = ReadFloat(span, o + 12);
                dst.World[i].z = ReadFloat(span, o + 16);
                dst.Visibility[i] = ReadFloat(span, o + 20);
                o += PoseProtocol.JointSize;
            }

            dst.HasPerson = true;
            dst.Seq = seq;
            dst.TimestampMs = timestamp;
            return true;
        }

        private static void WriteFloat(Span<byte> span, int offset, float value) =>
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), BitConverter.SingleToInt32Bits(value));

        private static float ReadFloat(ReadOnlySpan<byte> span, int offset) =>
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset)));
    }
}
