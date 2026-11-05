using System;
using System.Buffers.Binary;
using VisionToWall.Core;

namespace VisionToWall.Network
{
    /// <summary>
    /// FR-04. <see cref="MotionFrame"/> &lt;-&gt; little-endian byte layout defined in <see cref="MotionProtocol"/>.
    /// Allocation free: the caller owns every buffer.
    /// </summary>
    public static class MotionPacketSerializer
    {
        private const int OffsetMagic = 0;
        private const int OffsetSeq = 4;
        private const int OffsetTimestamp = 8;
        private const int OffsetCount = 12;

        /// <summary>
        /// Writes <paramref name="frame"/> into <paramref name="buffer"/> using <paramref name="seq"/> as the wire seq.
        /// Returns the packet length. Count is clamped to [0, MaxPoints].
        /// </summary>
        public static int Write(MotionFrame frame, uint seq, byte[] buffer)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int count = frame.Count;
            if (count < 0) count = 0;
            else if (count > MotionProtocol.MaxPoints) count = MotionProtocol.MaxPoints;

            int length = MotionProtocol.HeaderSize + count * MotionProtocol.PointSize;
            if (buffer.Length < length)
                throw new ArgumentException("Buffer smaller than packet; use MotionProtocol.MaxPacketSize.", nameof(buffer));

            Span<byte> span = buffer;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetMagic), MotionProtocol.Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetSeq), seq);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OffsetTimestamp), frame.TimestampMs);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(OffsetCount), (ushort)count);

            MotionPoint[] points = frame.Points;
            int o = MotionProtocol.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                MotionPoint p = points[i];
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(o), BitConverter.SingleToInt32Bits(p.x));
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(o + 4), BitConverter.SingleToInt32Bits(p.y));
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(o + 8), BitConverter.SingleToInt32Bits(p.v));
                o += MotionProtocol.PointSize;
            }

            return length;
        }

        /// <summary>
        /// Parses a packet into <paramref name="dst"/>. Returns false on bad magic, length or count;
        /// <paramref name="dst"/> contents are undefined on failure.
        /// </summary>
        public static bool TryRead(byte[] buffer, int length, MotionFrame dst)
        {
            if (buffer == null || dst == null) return false;
            if (length < MotionProtocol.HeaderSize || length > buffer.Length) return false;

            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer, 0, length);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetMagic)) != MotionProtocol.Magic) return false;

            int count = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(OffsetCount));
            if (count > MotionProtocol.MaxPoints) return false;
            if (length != MotionProtocol.HeaderSize + count * MotionProtocol.PointSize) return false;

            dst.Seq = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetSeq));
            dst.TimestampMs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(OffsetTimestamp));
            dst.Count = count;

            MotionPoint[] points = dst.Points;
            int o = MotionProtocol.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                points[i].x = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(o)));
                points[i].y = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(o + 4)));
                points[i].v = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(o + 8)));
                o += MotionProtocol.PointSize;
            }

            return true;
        }
    }
}
