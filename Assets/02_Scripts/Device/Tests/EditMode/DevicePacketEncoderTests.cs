using System;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Device.Tests
{
    public class DevicePacketEncoderTests
    {
        private static byte[] Encode(byte id, float pan, float tilt)
        {
            var buf = new byte[DeviceProtocol.PacketSize];
            Assert.IsTrue(DevicePacketEncoder.TryEncode(buf, id, pan, tilt));
            return buf;
        }

        [Test]
        public void ByteOrder_FixedExample()
        {
            // pan -28.00 = -2800 = 0xF510, tilt 12.34 = 1234 = 0x04D2, checksum 01^F5^10^04^D2 = 0x32
            byte[] packet = Encode(1, -28f, 12.34f);
            CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0xF5, 0x10, 0x04, 0xD2, 0x32, 0x03 }, packet);
            Assert.AreEqual("02 01 F5 10 04 D2 32 03", LogPacketSink.FormatHex(packet));
        }

        [Test]
        public void RoundTrip_ErrorWithinOneHundredthDegree()
        {
            var rng = new Random(1234);
            for (int i = 0; i < 2000; i++)
            {
                float pan = (float)(rng.NextDouble() * 360.0 - 180.0);
                float tilt = (float)(rng.NextDouble() * 180.0 - 90.0);
                byte id = (byte)rng.Next(256);
                byte[] packet = Encode(id, pan, tilt);

                Assert.IsTrue(DevicePacketEncoder.TryDecode(packet, out byte outId, out float outPan, out float outTilt));
                Assert.AreEqual(id, outId);
                Assert.AreEqual(pan, outPan, 0.01f);
                Assert.AreEqual(tilt, outTilt, 0.01f);
            }
        }

        [TestCase(180f, 90f, 180f, 90f)]
        [TestCase(-180f, -90f, -180f, -90f)]
        [TestCase(250f, 135f, 180f, 90f)]
        [TestCase(-999f, -91f, -180f, -90f)]
        [TestCase(float.PositiveInfinity, float.NegativeInfinity, 180f, -90f)]
        [TestCase(float.NaN, float.NaN, 0f, 0f)]
        public void Boundaries_AreClamped(float pan, float tilt, float expectedPan, float expectedTilt)
        {
            byte[] packet = Encode(1, pan, tilt);
            Assert.IsTrue(DevicePacketEncoder.TryDecode(packet, out _, out float outPan, out float outTilt));
            Assert.AreEqual(expectedPan, outPan, 0.0001f);
            Assert.AreEqual(expectedTilt, outTilt, 0.0001f);
        }

        [Test]
        public void Boundaries_ExactBytes()
        {
            byte[] max = Encode(1, 180f, 90f);   // 18000 = 0x4650, 9000 = 0x2328
            Assert.AreEqual(0x46, max[2]); Assert.AreEqual(0x50, max[3]);
            Assert.AreEqual(0x23, max[4]); Assert.AreEqual(0x28, max[5]);
            byte[] min = Encode(1, -180f, -90f); // -18000 = 0xB9B0, -9000 = 0xDCD8
            Assert.AreEqual(0xB9, min[2]); Assert.AreEqual(0xB0, min[3]);
            Assert.AreEqual(0xDC, min[4]); Assert.AreEqual(0xD8, min[5]);
        }

        [Test]
        public void Decode_RejectsBadChecksum()
        {
            for (int i = DeviceProtocol.IdIndex; i <= DeviceProtocol.ChecksumIndex; i++)
            {
                byte[] packet = Encode(1, 12.5f, -3f);
                packet[i] ^= 0x01;
                Assert.IsFalse(DevicePacketEncoder.TryDecode(packet, out _, out _, out _), $"byte {i}");
            }
        }

        [Test]
        public void Decode_RejectsFrameErrors()
        {
            byte[] good = Encode(1, 1f, 1f);

            byte[] badStx = (byte[])good.Clone(); badStx[0] = 0x00;
            byte[] badEtx = (byte[])good.Clone(); badEtx[7] = 0x00;
            Assert.IsFalse(DevicePacketEncoder.TryDecode(badStx, out _, out _, out _));
            Assert.IsFalse(DevicePacketEncoder.TryDecode(badEtx, out _, out _, out _));
            Assert.IsFalse(DevicePacketEncoder.TryDecode(new ReadOnlySpan<byte>(good, 0, 7), out _, out _, out _));
            Assert.IsFalse(DevicePacketEncoder.TryDecode(new byte[9], out _, out _, out _));
        }

        [Test]
        public void Decode_RejectsOutOfRangeAngleWithValidChecksum()
        {
            byte[] packet = Encode(1, 0f, 0f);
            packet[2] = 0x50; packet[3] = 0x00; // 20480 = 204.80 deg
            packet[6] = DevicePacketEncoder.Checksum(packet);
            Assert.IsFalse(DevicePacketEncoder.TryDecode(packet, out _, out _, out _));
        }

        [Test]
        public void Encode_RejectsShortBuffer()
        {
            Assert.IsFalse(DevicePacketEncoder.TryEncode(new byte[7], 1, 0f, 0f));
        }

        [Test]
        public void Sign_RightIsNegativePan()
        {
            // Measured in CombatDev: player at the right of the screen gives BossPanDeg about -28.
            byte[] packet = Encode(1, -28f, 0f);
            Assert.AreEqual(0xF5, packet[2]); // high bit set = negative
        }
    }

    public class LogPacketSinkTests
    {
        [Test]
        public void KeepsLastPacketAndCounts()
        {
            var sink = new LogPacketSink(false);
            var dst = new byte[DeviceProtocol.PacketSize];
            Assert.IsFalse(sink.TryGetLastPacket(dst));

            var a = new byte[DeviceProtocol.PacketSize];
            var b = new byte[DeviceProtocol.PacketSize];
            DevicePacketEncoder.TryEncode(a, 1, 10f, 0f);
            DevicePacketEncoder.TryEncode(b, 1, -10f, 5f);
            sink.Send(a);
            sink.Send(b);
            sink.Send(new byte[3]);

            Assert.AreEqual(2, sink.Count);
            Assert.AreEqual(1, sink.Rejected);
            Assert.IsTrue(sink.TryGetLastPacket(dst));
            CollectionAssert.AreEqual(b, dst);
        }
    }

    public class DeviceSendLoopTests
    {
        [Test]
        public void SendHz_IsClamped()
        {
            Assert.AreEqual(1, new DeviceSendLoop(0, 180f).SendHz);
            Assert.AreEqual(60, new DeviceSendLoop(500, 180f).SendHz);
            Assert.AreEqual(30, new DeviceSendLoop(30, 180f).SendHz);
        }

        [Test]
        public void SendsAtConfiguredRate()
        {
            var loop = new DeviceSendLoop(30, 180f);
            int sent = 0;
            const float dt = 1f / 144f;
            for (int i = 0; i < 144 * 10; i++)
                if (loop.Step(i * dt, dt, 0f, 0f)) sent++;
            Assert.AreEqual(300, sent, 2);
        }

        [Test]
        public void StallDoesNotBurst()
        {
            var loop = new DeviceSendLoop(30, 180f);
            Assert.IsTrue(loop.Step(0f, 0.016f, 0f, 0f));
            Assert.IsTrue(loop.Step(2f, 0.1f, 0f, 0f));   // after a 2 s stall: one packet
            Assert.IsFalse(loop.Step(2.01f, 0.01f, 0f, 0f));
        }

        [Test]
        public void TurnSpeedIsLimited()
        {
            var loop = new DeviceSendLoop(30, 180f);
            loop.Step(0f, 0.1f, -170f, 80f);
            Assert.AreEqual(-18f, loop.PanDeg, 1e-4f);
            Assert.AreEqual(18f, loop.TiltDeg, 1e-4f);

            float t = 0f;
            for (int i = 0; i < 100; i++) loop.Step(t += 0.01f, 0.01f, -170f, 80f);
            Assert.AreEqual(-170f, loop.PanDeg, 1e-3f);
            Assert.AreEqual(80f, loop.TiltDeg, 1e-3f);
        }

        [Test]
        public void ReturnsToZeroAtLimitedSpeed()
        {
            var loop = new DeviceSendLoop(30, 90f);
            float t = 0f;
            for (int i = 0; i < 100; i++) loop.Step(t += 0.01f, 0.01f, 45f, -30f);
            Assert.AreEqual(45f, loop.PanDeg, 1e-3f);

            loop.Step(t += 0.1f, 0.1f, 0f, 0f);
            Assert.AreEqual(36f, loop.PanDeg, 1e-3f);
            for (int i = 0; i < 100; i++) loop.Step(t += 0.01f, 0.01f, 0f, 0f);
            Assert.AreEqual(0f, loop.PanDeg, 1e-3f);
            Assert.AreEqual(0f, loop.TiltDeg, 1e-3f);
        }

        [Test]
        public void NaNTargetIsTreatedAsZero()
        {
            var loop = new DeviceSendLoop(30, 1000f);
            loop.Step(0f, 0.1f, float.NaN, float.NaN);
            Assert.AreEqual(0f, loop.PanDeg);
            Assert.AreEqual(0f, loop.TiltDeg);
        }
    }
}
