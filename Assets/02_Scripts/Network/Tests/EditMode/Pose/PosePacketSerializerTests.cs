using System.Buffers.Binary;
using NUnit.Framework;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network.Tests
{
    public sealed class PosePacketSerializerTests
    {
        private static PoseFrame MakeFrame()
        {
            var f = new PoseFrame { HasPerson = true, TimestampMs = 123456 };
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                f.Screen[i] = new Vector2(0.01f * i, 1f - 0.02f * i);
                f.World[i] = new Vector3(-0.1f * i, 0.2f * i, 0.3f + i);
                f.Visibility[i] = i / 12f;
            }

            return f;
        }

        [Test]
        public void RoundTrip_FullFrame()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            PoseFrame src = MakeFrame();
            int length = PosePacketSerializer.Write(src, 42, buffer);
            Assert.AreEqual(PoseProtocol.PacketSize, length);
            Assert.AreEqual(326, length);

            var dst = new PoseFrame();
            Assert.IsTrue(PosePacketSerializer.TryRead(buffer, length, dst));
            Assert.IsTrue(dst.HasPerson);
            Assert.AreEqual(42u, dst.Seq);
            Assert.AreEqual(123456u, dst.TimestampMs);
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                Assert.AreEqual(src.Screen[i], dst.Screen[i]);
                Assert.AreEqual(src.World[i], dst.World[i]);
                Assert.AreEqual(src.Visibility[i], dst.Visibility[i]);
            }
        }

        [Test]
        public void Layout_MatchesProtocol()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            PoseFrame src = MakeFrame();
            PosePacketSerializer.Write(src, 7, buffer);

            Assert.AreEqual(PoseProtocol.Magic, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
            Assert.AreEqual(7u, BinaryPrimitives.ReadUInt32LittleEndian(new System.ReadOnlySpan<byte>(buffer, 4, 4)));
            Assert.AreEqual(13, BinaryPrimitives.ReadUInt16LittleEndian(new System.ReadOnlySpan<byte>(buffer, 12, 2)));

            // Joint 5 (LeftWrist) visibility is float 6 of its block.
            int offset = PoseProtocol.HeaderSize + 5 * PoseProtocol.JointSize + 20;
            float v = System.BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(new System.ReadOnlySpan<byte>(buffer, offset, 4)));
            Assert.AreEqual(src.Visibility[5], v);
        }

        [Test]
        public void CountZero_ClearsFrame()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            var empty = new PoseFrame { HasPerson = false, TimestampMs = 9 };
            int length = PosePacketSerializer.Write(empty, 3, buffer);
            Assert.AreEqual(PoseProtocol.EmptyPacketSize, length);

            PoseFrame dst = MakeFrame();
            Assert.IsTrue(PosePacketSerializer.TryRead(buffer, length, dst));
            Assert.IsFalse(dst.HasPerson);
            Assert.AreEqual(3u, dst.Seq);
            Assert.AreEqual(9u, dst.TimestampMs);
            Assert.AreEqual(0f, dst.Visibility[5]);
        }

        [Test]
        public void Truncated_Rejected()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            int length = PosePacketSerializer.Write(MakeFrame(), 1, buffer);
            var dst = new PoseFrame();
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, length - 1, dst));
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, PoseProtocol.HeaderSize - 1, dst));
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, PoseProtocol.HeaderSize, dst)); // header says 13 joints
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, 0, dst));
        }

        [Test]
        public void Oversized_Rejected()
        {
            var buffer = new byte[PoseProtocol.PacketSize + 8];
            int length = PosePacketSerializer.Write(MakeFrame(), 1, buffer);
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, length + 4, new PoseFrame()));
        }

        [Test]
        public void WrongMagic_Rejected()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            int length = PosePacketSerializer.Write(MakeFrame(), 1, buffer);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, MotionProtocol.Magic);
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, length, new PoseFrame()));
        }

        [Test]
        public void WrongCount_Rejected()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            int length = PosePacketSerializer.Write(MakeFrame(), 1, buffer);
            BinaryPrimitives.WriteUInt16LittleEndian(new System.Span<byte>(buffer, 12, 2), 12);
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, length, new PoseFrame()));
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, PoseProtocol.HeaderSize + 12 * PoseProtocol.JointSize,
                new PoseFrame()));
        }

        [Test]
        public void LengthBeyondBuffer_Rejected()
        {
            var buffer = new byte[PoseProtocol.PacketSize];
            PosePacketSerializer.Write(MakeFrame(), 1, buffer);
            Assert.IsFalse(PosePacketSerializer.TryRead(buffer, buffer.Length + 1, new PoseFrame()));
            Assert.IsFalse(PosePacketSerializer.TryRead(null, 10, new PoseFrame()));
        }

        [Test]
        public void SeqRule_SharedWithMotionReceiver()
        {
            Assert.IsTrue(UdpMotionReceiver.IsSeqAccepted(5, 4));
            Assert.IsFalse(UdpMotionReceiver.IsSeqAccepted(4, 4));
            Assert.IsFalse(UdpMotionReceiver.IsSeqAccepted(3, 4));
            Assert.IsTrue(UdpMotionReceiver.IsSeqAccepted(1, 100)); // sender restart
        }
    }
}
