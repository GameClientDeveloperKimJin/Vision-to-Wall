using System.Text;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.UI.Tests
{
    public class UiFormattingTests
    {
        [TestCase(0L, "0")]
        [TestCase(7L, "7")]
        [TestCase(1234L, "1234")]
        [TestCase(-42L, "-42")]
        [TestCase(4294967295L, "4294967295")]
        [TestCase(long.MinValue, "-9223372036854775808")]
        public void AppendInt_MatchesToString(long value, string expected)
        {
            Assert.AreEqual(expected, new StringBuilder().AppendInt(value).ToString());
        }

        [TestCase(0f, 2, "0.00")]
        [TestCase(0.5f, 2, "0.50")]
        [TestCase(0.125f, 2, "0.13")]
        [TestCase(0.02f, 2, "0.02")]
        [TestCase(12.3456f, 1, "12.3")]
        [TestCase(-0.004f, 2, "0.00")]
        [TestCase(-1.5f, 2, "-1.50")]
        [TestCase(3.7f, 0, "4")]
        [TestCase(float.NaN, 2, "0")]
        public void AppendFixed_Rounds(float value, int decimals, string expected)
        {
            Assert.AreEqual(expected, new StringBuilder().AppendFixed(value, decimals).ToString());
        }

        [Test]
        public void PacketLog_Empty_BuildsNothing()
        {
            var log = new PacketLog();
            var sb = new StringBuilder("stale");
            log.Build(sb);
            Assert.AreEqual(0, log.Size);
            Assert.AreEqual(string.Empty, sb.ToString());
        }

        [Test]
        public void PacketLog_FormatsJsonWithFirstThreePoints()
        {
            var frame = new MotionFrame { Seq = 1234, Count = 4 };
            frame.Points[0] = new MotionPoint(0.53f, 0.21f, 0.8f);
            frame.Points[1] = new MotionPoint(0.1f, 0.2f, 0.3f);
            frame.Points[2] = new MotionPoint(1f, 0f, 1f);
            frame.Points[3] = new MotionPoint(0.9f, 0.9f, 0.9f);

            var log = new PacketLog();
            log.Record(frame);
            var sb = new StringBuilder();
            log.Build(sb);

            Assert.AreEqual(
                "#1234 (4) {\"points\":[{\"x\":0.53,\"y\":0.21,\"v\":0.80},{\"x\":0.10,\"y\":0.20,\"v\":0.30}," +
                "{\"x\":1.00,\"y\":0.00,\"v\":1.00},...]}",
                sb.ToString());
        }

        [Test]
        public void PacketLog_EmptyFrame()
        {
            var log = new PacketLog();
            log.Record(new MotionFrame { Seq = 5, Count = 0 });
            var sb = new StringBuilder();
            log.Build(sb);
            Assert.AreEqual("#5 (0) {\"points\":[]}", sb.ToString());
        }

        [Test]
        public void PacketLog_KeepsLastTenOldestFirst()
        {
            var log = new PacketLog();
            var frame = new MotionFrame();
            for (uint s = 1; s <= 25; s++)
            {
                frame.Seq = s;
                log.Record(frame);
            }

            var sb = new StringBuilder();
            log.Build(sb);
            string[] lines = sb.ToString().Split('\n');

            Assert.AreEqual(PacketLog.Lines, log.Size);
            Assert.AreEqual(PacketLog.Lines, lines.Length);
            StringAssert.StartsWith("#16 ", lines[0]);
            StringAssert.StartsWith("#25 ", lines[9]);
        }

        [Test]
        public void FpsCounter_ConvergesToSteadyRate()
        {
            var fps = new FpsCounter(0.5f);
            fps.Sample(1f / 30f);
            Assert.AreEqual(30f, fps.Value, 0.01f);

            for (int i = 0; i < 300; i++) fps.Sample(1f / 60f); // 5 s at 60 fps
            Assert.AreEqual(60f, fps.Value, 0.5f);

            fps.Reset();
            Assert.AreEqual(0f, fps.Value);
        }

        [Test]
        public void FpsCounter_IgnoresZeroDelta()
        {
            var fps = new FpsCounter();
            fps.Sample(0f);
            Assert.AreEqual(0f, fps.Value);
        }
    }
}
