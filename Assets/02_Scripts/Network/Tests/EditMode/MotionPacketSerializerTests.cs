using System;
using System.Buffers.Binary;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Network.Tests
{
    public class MotionPacketSerializerTests
    {
        private static MotionFrame MakeFrame(int count, uint timestamp = 1234u)
        {
            var f = new MotionFrame { Count = count, TimestampMs = timestamp };
            for (int i = 0; i < count; i++)
                f.Points[i] = new MotionPoint(i / (float)MotionProtocol.MaxPoints, 1f - i * 0.0001f, (i % 100) / 100f);
            return f;
        }

        private static byte[] NewBuffer() => new byte[MotionProtocol.MaxPacketSize];

        [Test]
        public void RoundTrip_PreservesAllFields()
        {
            MotionFrame src = MakeFrame(5);
            src.Points[2] = new MotionPoint(0.123456f, 0.987654f, float.Epsilon);
            byte[] buf = NewBuffer();

            int len = MotionPacketSerializer.Write(src, 42u, buf);
            var dst = new MotionFrame();

            Assert.AreEqual(MotionProtocol.HeaderSize + 5 * MotionProtocol.PointSize, len);
            Assert.IsTrue(MotionPacketSerializer.TryRead(buf, len, dst));
            Assert.AreEqual(42u, dst.Seq, "Wire seq comes from the Write argument.");
            Assert.AreEqual(src.TimestampMs, dst.TimestampMs);
            Assert.AreEqual(5, dst.Count);
            for (int i = 0; i < 5; i++)
            {
                // Bit-exact: floats are copied as raw bits.
                Assert.AreEqual(src.Points[i].x, dst.Points[i].x);
                Assert.AreEqual(src.Points[i].y, dst.Points[i].y);
                Assert.AreEqual(src.Points[i].v, dst.Points[i].v);
            }
        }

        [Test]
        public void Write_IsLittleEndianWithExpectedLayout()
        {
            var src = new MotionFrame { Count = 1, TimestampMs = 0x0A0B0C0Du };
            src.Points[0] = new MotionPoint(1f, 0f, 0.5f);
            byte[] buf = NewBuffer();

            MotionPacketSerializer.Write(src, 0x01020304u, buf);

            Assert.AreEqual(new byte[] { 0x01, 0x56, 0x32, 0x57 }, Slice(buf, 0, 4), "magic");
            Assert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01 }, Slice(buf, 4, 4), "seq");
            Assert.AreEqual(new byte[] { 0x0D, 0x0C, 0x0B, 0x0A }, Slice(buf, 8, 4), "timestamp");
            Assert.AreEqual(new byte[] { 0x01, 0x00 }, Slice(buf, 12, 2), "count");
            Assert.AreEqual(new byte[] { 0x00, 0x00, 0x80, 0x3F }, Slice(buf, 14, 4), "x = 1.0f");
        }

        [Test]
        public void RoundTrip_CountZero()
        {
            byte[] buf = NewBuffer();
            int len = MotionPacketSerializer.Write(MakeFrame(0), 1u, buf);
            var dst = new MotionFrame { Count = 77 };

            Assert.AreEqual(MotionProtocol.HeaderSize, len);
            Assert.IsTrue(MotionPacketSerializer.TryRead(buf, len, dst));
            Assert.AreEqual(0, dst.Count);
            Assert.AreEqual(1u, dst.Seq);
        }

        [Test]
        public void RoundTrip_CountMaxPoints()
        {
            MotionFrame src = MakeFrame(MotionProtocol.MaxPoints);
            byte[] buf = NewBuffer();

            int len = MotionPacketSerializer.Write(src, 7u, buf);
            var dst = new MotionFrame();

            Assert.AreEqual(MotionProtocol.MaxPacketSize, len);
            Assert.IsTrue(MotionPacketSerializer.TryRead(buf, len, dst));
            Assert.AreEqual(MotionProtocol.MaxPoints, dst.Count);
            int last = MotionProtocol.MaxPoints - 1;
            Assert.AreEqual(src.Points[last].x, dst.Points[last].x);
            Assert.AreEqual(src.Points[last].v, dst.Points[last].v);
        }

        [Test]
        public void Write_ClampsCountAboveMax()
        {
            MotionFrame src = MakeFrame(MotionProtocol.MaxPoints);
            src.Count = MotionProtocol.MaxPoints + 10;
            Assert.AreEqual(MotionProtocol.MaxPacketSize, MotionPacketSerializer.Write(src, 1u, NewBuffer()));
        }

        [Test]
        public void Write_ThrowsOnSmallBuffer()
        {
            Assert.Throws<ArgumentException>(() =>
                MotionPacketSerializer.Write(MakeFrame(2), 1u, new byte[MotionProtocol.HeaderSize]));
        }

        [Test]
        public void TryRead_RejectsBadMagic()
        {
            byte[] buf = NewBuffer();
            int len = MotionPacketSerializer.Write(MakeFrame(3), 1u, buf);
            buf[0] ^= 0xFF;
            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, len, new MotionFrame()));
        }

        [Test]
        public void TryRead_RejectsLengthMismatch()
        {
            byte[] buf = NewBuffer();
            int len = MotionPacketSerializer.Write(MakeFrame(3), 1u, buf);
            var dst = new MotionFrame();

            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, len - 1, dst), "truncated");
            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, len + 1, dst), "trailing bytes");
            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, MotionProtocol.HeaderSize - 1, dst), "shorter than header");
            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, buf.Length + 1, dst), "length beyond buffer");
        }

        [Test]
        public void TryRead_RejectsCountAboveMax()
        {
            byte[] buf = new byte[MotionProtocol.MaxPacketSize + MotionProtocol.PointSize];
            int len = MotionPacketSerializer.Write(MakeFrame(0), 1u, buf);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(buf, 12, 2), (ushort)(MotionProtocol.MaxPoints + 1));
            len += (MotionProtocol.MaxPoints + 1) * MotionProtocol.PointSize;

            Assert.IsFalse(MotionPacketSerializer.TryRead(buf, len, new MotionFrame()));
        }

        [Test]
        public void TryRead_RejectsNull()
        {
            Assert.IsFalse(MotionPacketSerializer.TryRead(null, 14, new MotionFrame()));
            Assert.IsFalse(MotionPacketSerializer.TryRead(NewBuffer(), 14, null));
        }

        [TestCase(1u, 0u, true, TestName = "Seq_FirstPacket")]
        [TestCase(5u, 4u, true, TestName = "Seq_Increasing")]
        [TestCase(10u, 4u, true, TestName = "Seq_GapAllowed")]
        [TestCase(3u, 4u, false, TestName = "Seq_RegressionDropped")]
        [TestCase(4u, 4u, false, TestName = "Seq_DuplicateDropped")]
        [TestCase(1u, 900u, true, TestName = "Seq_SenderRestartAllowed")]
        public void SeqFilter(uint seq, uint lastAccepted, bool expected)
        {
            Assert.AreEqual(expected, UdpMotionReceiver.IsSeqAccepted(seq, lastAccepted));
        }

        private static byte[] Slice(byte[] src, int offset, int count)
        {
            var r = new byte[count];
            Array.Copy(src, offset, r, 0, count);
            return r;
        }
    }
}
