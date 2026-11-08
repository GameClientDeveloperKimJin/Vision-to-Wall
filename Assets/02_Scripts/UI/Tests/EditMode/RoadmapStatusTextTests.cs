using System;
using System.Text;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.UI.Tests
{
    public class RoadmapStatusTextTests
    {
        private sealed class FakeDevice : IDevicePanelSource
        {
            public bool IsActive { get; set; } = true;
            public int PacketsSent { get; set; }
            public byte[] Last;

            public bool TryGetLastPacket(Span<byte> destination)
            {
                if (Last == null || destination.Length < Last.Length) return false;
                Last.CopyTo(destination);
                return true;
            }
        }

        private sealed class FakeStats : INetworkStats
        {
            public int PacketsReceived { get; set; }
            public int PacketsDropped { get; set; }
            public uint LastSeq { get; set; }
            public float LastPacketTime { get; set; } = -1f;
        }

        private sealed class FakePose : IPoseSource
        {
            public event Action<PoseFrame> PoseReceived { add { } remove { } }
            public PoseFrame Latest { get; } = new PoseFrame();
            public float LastPoseTime => -1f;
        }

        private sealed class FakeWeather : IWeatherPanelSource
        {
            public WeatherKind Current { get; set; }
            public bool IsOverride { get; set; }
            public WeatherPanelState State { get; set; }
            public DateTime LastSuccessUtc { get; set; } = DateTime.MinValue;
        }

        private static readonly DateTime Now = new DateTime(2026, 11, 8, 5, 30, 0, DateTimeKind.Utc);
        private readonly byte[] _buf = new byte[DeviceProtocol.PacketSize];

        private string Device(IDevicePanelSource d)
        {
            var sb = new StringBuilder();
            RoadmapStatusText.AppendDevice(sb, d, _buf);
            return sb.ToString();
        }

        [Test]
        public void Device_ShowsCountAndLastPacketHex()
        {
            var d = new FakeDevice { PacketsSent = 1234, Last = new byte[] { 0x02, 0x01, 0x04, 0x33, 0xF1, 0xDA, 0x1D, 0x03 } };
            Assert.AreEqual("장치: 송신 1234  |  02 01 04 33 F1 DA 1D 03", Device(d));
        }

        [Test]
        public void Device_OffNullAndNoPacket()
        {
            Assert.AreEqual("장치: 연결 없음", Device(null));
            Assert.AreEqual("장치: 꺼짐 (deviceEnabled)", Device(new FakeDevice { IsActive = false }));
            Assert.AreEqual("장치: 송신 0  |  패킷 없음", Device(new FakeDevice()));
        }

        [Test]
        public void Pose_ShowsCountsAndPerson()
        {
            var stats = new FakeStats { PacketsReceived = 812, PacketsDropped = 3 };
            var pose = new FakePose();
            var sb = new StringBuilder();
            RoadmapStatusText.AppendPose(sb, stats, pose, 9002);
            Assert.AreEqual("관절 (9002)  수신 812  버림 3  |  사람 없음", sb.ToString());

            pose.Latest.HasPerson = true;
            sb.Length = 0;
            RoadmapStatusText.AppendPose(sb, stats, pose, 9002);
            StringAssert.EndsWith("사람 있음", sb.ToString());

            sb.Length = 0;
            RoadmapStatusText.AppendPose(sb, null, null, 9002);
            StringAssert.StartsWith("관절: 꺼짐", sb.ToString());
        }

        [Test]
        public void Weather_OverrideStateAndAge()
        {
            var w = new FakeWeather
            {
                Current = WeatherKind.Rain, IsOverride = true, State = WeatherPanelState.Ok,
                LastSuccessUtc = Now.AddSeconds(-185)
            };
            var sb = new StringBuilder();
            RoadmapStatusText.AppendWeather(sb, w, Now);
            DateTime local = w.LastSuccessUtc.ToLocalTime();
            string clock = local.Hour.ToString("00") + ":" + local.Minute.ToString("00");
            Assert.AreEqual("날씨: 비 (고정)  |  정상\n마지막 성공 " + clock + " (3분 전)", sb.ToString());
        }

        [TestCase(WeatherPanelState.NoKey, "키 없음")]
        [TestCase(WeatherPanelState.Offline, "오프라인")]
        [TestCase(WeatherPanelState.AuthError, "인증 오류")]
        [TestCase(WeatherPanelState.Off, "꺼짐")]
        public void Weather_ErrorStatesAndNeverSucceeded(WeatherPanelState state, string expected)
        {
            var sb = new StringBuilder();
            RoadmapStatusText.AppendWeather(sb, new FakeWeather { State = state }, Now);
            string text = sb.ToString();
            StringAssert.StartsWith("날씨: 없음 (기본 테마)  |  ", text);
            StringAssert.Contains(expected, text);
            StringAssert.EndsWith("\n마지막 성공 -", text);
        }

        [Test]
        public void Weather_AgeUnits()
        {
            var w = new FakeWeather { State = WeatherPanelState.Ok };
            var sb = new StringBuilder();
            w.LastSuccessUtc = Now.AddSeconds(-42);
            RoadmapStatusText.AppendWeather(sb, w, Now);
            StringAssert.EndsWith("(42초 전)", sb.ToString());
            sb.Length = 0;
            w.LastSuccessUtc = Now.AddHours(-2.5);
            RoadmapStatusText.AppendWeather(sb, w, Now);
            StringAssert.EndsWith("(2시간 전)", sb.ToString());
        }

        [Test]
        public void Append_FitsFourLines()
        {
            var sb = new StringBuilder();
            RoadmapStatusText.Append(sb, new FakeDevice(), new FakeStats(), new FakePose(), 9002,
                new FakeWeather { State = WeatherPanelState.Ok, LastSuccessUtc = Now }, Now, _buf);
            Assert.AreEqual(4, sb.ToString().Split('\n').Length);
        }

        [Test]
        public void Append_DoesNotAllocateAfterWarmup()
        {
            var d = new FakeDevice { PacketsSent = 99, Last = new byte[] { 2, 1, 4, 51, 241, 218, 29, 3 } };
            var stats = new FakeStats { PacketsReceived = 5 };
            var pose = new FakePose();
            var w = new FakeWeather { Current = WeatherKind.Cloudy, State = WeatherPanelState.Ok, LastSuccessUtc = Now.AddMinutes(-5) };
            var sb = new StringBuilder(512);
            for (int i = 0; i < 3; i++)
            {
                sb.Length = 0;
                RoadmapStatusText.Append(sb, d, stats, pose, 9002, w, Now, _buf);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                sb.Length = 0;
                RoadmapStatusText.Append(sb, d, stats, pose, 9002, w, Now, _buf);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated by 100 formats");
        }
    }
}
