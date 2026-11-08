using System;
using System.Text;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// F1 panel view of the R1 device controller. UI only references Core, so the App wraps
    /// Device.DeviceController in a small adapter (same member names, see 59_세션OP_ExhibitionOps.md).
    /// </summary>
    public interface IDevicePanelSource
    {
        /// <summary>False when config.deviceEnabled is false (the controller disabled itself).</summary>
        bool IsActive { get; }
        int PacketsSent { get; }
        /// <summary>Copies the last sent packet; false before the first packet.</summary>
        bool TryGetLastPacket(Span<byte> destination);
    }

    /// <summary>Weather query state as the panel shows it. The App maps Weather.WeatherFetchStatus onto this.</summary>
    public enum WeatherPanelState
    {
        /// <summary>weatherEnabled false.</summary>
        Off = 0,
        /// <summary>Enabled, first query not answered yet.</summary>
        Waiting,
        Ok,
        /// <summary>secrets.json missing, empty or placeholder.</summary>
        NoKey,
        /// <summary>Network error or timeout.</summary>
        Offline,
        /// <summary>Key not active yet, expired or rejected (retried after an hour).</summary>
        AuthError,
        /// <summary>Service error, no data or a broken response.</summary>
        ServiceError
    }

    /// <summary>F1 panel view of the R5 weather service (wrapped by the App, like <see cref="IDevicePanelSource"/>).</summary>
    public interface IWeatherPanelSource
    {
        WeatherKind Current { get; }
        /// <summary>True when Current comes from config.weatherOverride.</summary>
        bool IsOverride { get; }
        /// <summary>State of the live query (kept while an override is shown).</summary>
        WeatherPanelState State { get; }
        /// <summary>UTC of the last successful query; DateTime.MinValue before the first.</summary>
        DateTime LastSuccessUtc { get; }
    }

    /// <summary>Allocation-free text for the F1 roadmap section (device, pose joints, weather).</summary>
    public static class RoadmapStatusText
    {
        private const string HexDigits = "0123456789ABCDEF";

        // 장치: 송신 1234  |  02 01 04 33 F1 DA 1D 03
        // 관절 (9002)  수신 1234  버림 0  |  사람 있음
        // 날씨: 흐림 (고정)  |  정상
        // 마지막 성공 14:32 (3분 전)
        /// <param name="packet">Scratch buffer of at least DeviceProtocol.PacketSize bytes (no allocation).</param>
        /// <param name="nowUtc">DateTime.UtcNow; passed in so tests are stable.</param>
        public static void Append(StringBuilder sb, IDevicePanelSource device, INetworkStats poseStats, IPoseSource pose,
            int posePort, IWeatherPanelSource weather, DateTime nowUtc, byte[] packet)
        {
            AppendDevice(sb, device, packet);
            sb.Append('\n');
            AppendPose(sb, poseStats, pose, posePort);
            sb.Append('\n');
            AppendWeather(sb, weather, nowUtc);
        }

        public static void AppendDevice(StringBuilder sb, IDevicePanelSource device, byte[] packet)
        {
            sb.Append("장치: ");
            if (device == null) { sb.Append("연결 없음"); return; }
            if (!device.IsActive) { sb.Append("꺼짐 (deviceEnabled)"); return; }
            sb.Append("송신 ").AppendInt(device.PacketsSent).Append("  |  ");
            if (packet == null || packet.Length < DeviceProtocol.PacketSize ||
                !device.TryGetLastPacket(packet.AsSpan(0, DeviceProtocol.PacketSize)))
            {
                sb.Append("패킷 없음");
                return;
            }
            AppendHex(sb, packet, DeviceProtocol.PacketSize);
        }

        public static void AppendPose(StringBuilder sb, INetworkStats poseStats, IPoseSource pose, int posePort)
        {
            if (poseStats == null) { sb.Append("관절: 꺼짐 (external 아님 또는 포트 0)"); return; }
            sb.Append("관절 (").AppendInt(posePort).Append(")  수신 ").AppendInt(poseStats.PacketsReceived)
              .Append("  버림 ").AppendInt(poseStats.PacketsDropped).Append("  |  ");
            bool person = pose != null && pose.Latest != null && pose.Latest.HasPerson;
            sb.Append(person ? "사람 있음" : "사람 없음");
        }

        public static void AppendWeather(StringBuilder sb, IWeatherPanelSource weather, DateTime nowUtc)
        {
            sb.Append("날씨: ");
            if (weather == null) { sb.Append("연결 없음\n마지막 성공 -"); return; }
            sb.Append(WeatherName(weather.Current));
            if (weather.IsOverride) sb.Append(" (고정)");
            sb.Append("  |  ").Append(StateName(weather.State));

            sb.Append("\n마지막 성공 ");
            DateTime last = weather.LastSuccessUtc;
            if (last == DateTime.MinValue) { sb.Append('-'); return; }
            DateTime local = last.ToLocalTime();
            AppendTwoDigits(sb, local.Hour);
            sb.Append(':');
            AppendTwoDigits(sb, local.Minute);
            double ago = (nowUtc - last).TotalSeconds;
            sb.Append(" (");
            if (ago < 60) sb.AppendInt((long)Math.Max(0, ago)).Append("초 전)");
            else if (ago < 3600) sb.AppendInt((long)(ago / 60)).Append("분 전)");
            else sb.AppendInt((long)(ago / 3600)).Append("시간 전)");
        }

        public static void AppendHex(StringBuilder sb, byte[] bytes, int count)
        {
            for (int i = 0; i < count && i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(HexDigits[bytes[i] >> 4]).Append(HexDigits[bytes[i] & 0xF]);
            }
        }

        public static string WeatherName(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Clear: return "맑음";
                case WeatherKind.Cloudy: return "흐림";
                case WeatherKind.Rain: return "비";
                case WeatherKind.Snow: return "눈";
                case WeatherKind.Fog: return "안개";
                default: return "없음 (기본 테마)";
            }
        }

        public static string StateName(WeatherPanelState state)
        {
            switch (state)
            {
                case WeatherPanelState.Off: return "꺼짐 (weatherEnabled)";
                case WeatherPanelState.Waiting: return "조회 대기";
                case WeatherPanelState.Ok: return "정상";
                case WeatherPanelState.NoKey: return "<color=#FF8866>키 없음 (secrets.json)</color>";
                case WeatherPanelState.Offline: return "<color=#FF8866>오프라인</color>";
                case WeatherPanelState.AuthError: return "<color=#FF8866>인증 오류</color>";
                case WeatherPanelState.ServiceError: return "<color=#FF8866>서비스 오류</color>";
                default: return "?";
            }
        }

        private static void AppendTwoDigits(StringBuilder sb, int value)
        {
            if (value < 10) sb.Append('0');
            sb.AppendInt(value);
        }
    }
}
