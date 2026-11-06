using System.Text;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>Allocation-free text for the combat HUD and the F1 game section.</summary>
    public static class GameHudText
    {
        /// <summary>01:05. Negative input shows 00:00.</summary>
        public static void AppendClock(StringBuilder sb, int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            AppendTwoDigits(sb, seconds / 60);
            sb.Append(':');
            AppendTwoDigits(sb, seconds % 60);
        }

        private static void AppendTwoDigits(StringBuilder sb, int value)
        {
            if (value < 10) sb.Append('0');
            sb.AppendInt(value);
        }

        // 단계: 전투  |  각성 게이지 1.00
        // 입력: 몸  |  강도 0.42
        // 몸 스트림 (9001)  수신 123  버림 0
        /// <param name="bodyStats">Null in webcam mode: the line is left out.</param>
        public static void AppendGameStatus(StringBuilder sb, IGamePhaseSource phase, IPlayerInputSource input,
            INetworkStats bodyStats, int bodyPort)
        {
            sb.Append("단계: ").Append(PhaseName(phase));
            if (phase != null) sb.Append("  |  각성 게이지 ").AppendFixed(phase.AwakenCharge, 2);

            sb.Append("\n입력: ");
            if (input == null)
            {
                sb.Append("연결 없음");
            }
            else
            {
                PlayerSample sample = input.Current;
                sb.Append(SourceName(sample.source)).Append("  |  강도 ").AppendFixed(sample.motion, 2);
            }

            if (bodyStats == null) return;

            sb.Append("\n몸 스트림 (").AppendInt(bodyPort).Append(")  수신 ").AppendInt(bodyStats.PacketsReceived)
              .Append("  버림 ").AppendInt(bodyStats.PacketsDropped);
        }

        public static string PhaseName(IGamePhaseSource phase)
        {
            if (phase == null) return "연결 없음";
            switch (phase.Phase)
            {
                case GamePhase.MediaWall: return "미디어 월";
                case GamePhase.Awaken: return "각성";
                case GamePhase.Combat: return "전투";
                case GamePhase.Result: return "결과";
                default: return "?";
            }
        }

        public static string SourceName(PlayerPositionSource source)
        {
            switch (source)
            {
                case PlayerPositionSource.Body: return "몸";
                case PlayerPositionSource.MotionCentroid: return "모션";
                default: return "없음";
            }
        }
    }
}
