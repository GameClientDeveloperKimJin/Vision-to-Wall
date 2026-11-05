using System.Text;
using TMPro;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Ring buffer of the last packets (seq, count, first 3 points) formatted as JSON lines.
    /// Plain class so it can be tested without a scene.
    /// </summary>
    public sealed class PacketLog
    {
        public const int Lines = 10;
        public const int PointsPerLine = 3;
        public const int Decimals = 2;

        private struct Entry
        {
            public uint Seq;
            public int Count;
            public MotionPoint P0, P1, P2;
        }

        private readonly Entry[] _ring = new Entry[Lines];
        private int _next;
        private int _size;

        public int Size => _size;

        public void Clear()
        {
            _next = 0;
            _size = 0;
        }

        public void Record(MotionFrame frame)
        {
            if (frame == null) return;

            ref Entry e = ref _ring[_next];
            e.Seq = frame.Seq;
            e.Count = frame.Count;
            if (frame.Count > 0) e.P0 = frame.Points[0];
            if (frame.Count > 1) e.P1 = frame.Points[1];
            if (frame.Count > 2) e.P2 = frame.Points[2];

            _next = (_next + 1) % Lines;
            if (_size < Lines) _size++;
        }

        /// <summary>Clears <paramref name="sb"/> and writes oldest to newest, one line per packet.</summary>
        public void Build(StringBuilder sb)
        {
            sb.Length = 0;
            int start = (_next - _size + Lines) % Lines;
            for (int i = 0; i < _size; i++)
            {
                if (i > 0) sb.Append('\n');
                AppendLine(sb, ref _ring[(start + i) % Lines]);
            }
        }

        // #1234 (16) {"points":[{"x":0.53,"y":0.21,"v":0.80},...]}
        private static void AppendLine(StringBuilder sb, ref Entry e)
        {
            sb.Append('#').AppendInt(e.Seq).Append(" (").AppendInt(e.Count).Append(") {\"points\":[");
            int shown = e.Count < PointsPerLine ? e.Count : PointsPerLine;
            if (shown > 0) AppendPoint(sb, e.P0);
            if (shown > 1) AppendPoint(sb.Append(','), e.P1);
            if (shown > 2) AppendPoint(sb.Append(','), e.P2);
            if (e.Count > shown) sb.Append(",...");
            sb.Append("]}");
        }

        private static void AppendPoint(StringBuilder sb, MotionPoint p)
        {
            sb.Append("{\"x\":").AppendFixed(p.x, Decimals)
              .Append(",\"y\":").AppendFixed(p.y, Decimals)
              .Append(",\"v\":").AppendFixed(p.v, Decimals)
              .Append('}');
        }
    }

    /// <summary>FR-08 packet log: records while the panel is open, renders on demand with one reused StringBuilder.</summary>
    public sealed class PacketLogView : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        private readonly PacketLog _log = new PacketLog();
        private readonly StringBuilder _sb = new StringBuilder(PacketLog.Lines * 160);

        public void Record(MotionFrame frame) => _log.Record(frame);

        public void Clear()
        {
            _log.Clear();
            Render();
        }

        public void Render()
        {
            if (text == null) return;
            if (_log.Size == 0)
            {
                text.SetText("(수신 패킷 없음)");
                return;
            }
            _log.Build(_sb);
            text.SetText(_sb);
        }
    }
}
