using System;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// Build measurement log (OP session). Off unless the player is started with <c>-perfLog &lt;csv path&gt;</c>:
    /// without it nothing is created and nothing runs. Optional <c>-perfIntervalSec N</c> (default 10).
    ///
    /// One CSV row per phase segment: a row is written when the game phase changes, every interval, or when the
    /// frame buffer fills, so every row belongs to one phase. Columns: frame count, average fps, 1% low fps
    /// (fps at the 99th percentile frame time), worst frame, frames with GC allocation and their bytes
    /// ("GC Allocated In Frame", development builds only: -1 in release), memory counters, GC collection count and
    /// the number of combats started so far. The frame right after a write is left out of the GC columns
    /// (the probe's own file write).
    ///
    /// Hand latency: the hand stream packet carries the sender's time.monotonic() in ms (GetTickCount64 on Windows
    /// Python 3.12); Environment.TickCount is its low 32 bits, so now - timestamp = sensor send -> Unity dispatch
    /// (Update of the receiver) on the same PC. Resolution 15.6 ms on both ends: use the average over many packets.
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        private const int MaxFrames = 16384;
        private const string Header =
            "unix_sec,t_sec,phase,frames,avg_fps,p1_low_fps,max_ms,gc_frames,gc_bytes,total_used_mb,gc_reserved_mb," +
            "system_used_mb,gc_count,combats,results,hand_lat_n,hand_lat_avg_ms,hand_lat_max_ms";

        private readonly float[] _frameSec = new float[MaxFrames];
        private readonly float[] _sorted = new float[MaxFrames];
        private readonly StringBuilder _sb = new StringBuilder(256);

        private StreamWriter _writer;
        private float _intervalSec = 10f;
        private IGamePhaseSource _phase;
        private GamePhase _rowPhase;
        private int _count;
        private float _rowStart;
        private long _gcFrames;
        private long _gcBytes;
        private bool _skipGcSample;
        private int _combats;
        private int _results;
        private bool _gcValid;
        private readonly System.Collections.Generic.List<IPointSource> _points =
            new System.Collections.Generic.List<IPointSource>(2);
        private Action<MotionFrame> _onFrame;
        private long _latN;
        private double _latSum;
        private long _latMax;

        private ProfilerRecorder _gcInFrame;
        private ProfilerRecorder _totalUsed;
        private ProfilerRecorder _gcReserved;
        private ProfilerRecorder _systemUsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string path = GetArg("-perfLog");
            if (string.IsNullOrEmpty(path)) return;

            var go = new GameObject("PerfProbe (-perfLog)");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<PerfProbe>();
            if (float.TryParse(GetArg("-perfIntervalSec"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float interval) && interval >= 1f)
                probe._intervalSec = interval;
            probe.Open(path);
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private void Open(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                bool exists = File.Exists(path);
                _writer = new StreamWriter(path, true, new UTF8Encoding(false), 4096);
                if (!exists) _writer.WriteLine(Header);
                _writer.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PerfProbe] cannot open " + path + ": " + e.Message);
                enabled = false;
                return;
            }

            _gcInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _totalUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            _gcReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            _systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            _gcValid = _gcInFrame.Valid && Debug.isDebugBuild;

            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (_phase == null && mb is IGamePhaseSource source) _phase = source;
                // Hand and body receivers are the same type; both carry the sender's timestamp, so listen to all.
                if (mb is IPointSource points && mb.isActiveAndEnabled) _points.Add(points);
            }
            _onFrame = OnFrame;
            foreach (IPointSource p in _points) p.FrameReceived += _onFrame;
            _rowPhase = CurrentPhase();
            _rowStart = Time.realtimeSinceStartup;
            Debug.Log("[PerfProbe] logging to " + path + " every " + _intervalSec + " s, gc per frame " +
                      (_gcValid ? "on" : "off (release build)") + ", phase " + (_phase != null ? "found" : "not found"));
        }

        private void OnFrame(MotionFrame frame)
        {
            long lat = (uint)((uint)Environment.TickCount - frame.TimestampMs); // low 32 bits of GetTickCount, wrap-safe
            if (lat > uint.MaxValue / 2) lat -= (long)uint.MaxValue + 1; // sender clock slightly ahead
            if (lat < -1000 || lat > 10000) return; // different clock (sender on another PC)
            _latN++;
            _latSum += lat;
            if (lat > _latMax) _latMax = lat;
        }

        private GamePhase CurrentPhase() => _phase != null ? _phase.Phase : GamePhase.MediaWall;

        private void Update()
        {
            if (_writer == null) return;

            // GC Allocated In Frame / LastValue = the previous frame, which is what unscaledDeltaTime measures too.
            if (_gcValid)
            {
                long bytes = _gcInFrame.LastValue;
                if (_skipGcSample) _skipGcSample = false;
                else if (bytes > 0) { _gcFrames++; _gcBytes += bytes; }
            }
            if (_count < MaxFrames) _frameSec[_count++] = Time.unscaledDeltaTime;

            GamePhase phase = CurrentPhase();
            float now = Time.realtimeSinceStartup;
            bool changed = phase != _rowPhase;
            if (changed || now - _rowStart >= _intervalSec || _count >= MaxFrames)
            {
                WriteRow(now);
                if (changed)
                {
                    if (phase == GamePhase.Combat) _combats++;
                    else if (phase == GamePhase.Result) _results++;
                }
                _rowPhase = phase;
                _rowStart = now;
            }
        }

        private void WriteRow(float now)
        {
            if (_count == 0) return;
            double sum = 0;
            float max = 0f;
            for (int i = 0; i < _count; i++)
            {
                float f = _frameSec[i];
                sum += f;
                if (f > max) max = f;
                _sorted[i] = f;
            }
            Array.Sort(_sorted, 0, _count);
            float p99 = _sorted[Mathf.Clamp(Mathf.CeilToInt(_count * 0.99f) - 1, 0, _count - 1)];

            _sb.Length = 0;
            _sb.AppendInt(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Append(',')
               .AppendFixed(now, 1).Append(',')
               .Append(PhaseKey(_rowPhase)).Append(',')
               .AppendInt(_count).Append(',')
               .AppendFixed(sum > 0 ? (float)(_count / sum) : 0f, 1).Append(',')
               .AppendFixed(p99 > 0f ? 1f / p99 : 0f, 1).Append(',')
               .AppendFixed(max * 1000f, 2).Append(',');
            if (_gcValid) _sb.AppendInt(_gcFrames).Append(',').AppendInt(_gcBytes).Append(',');
            else _sb.Append("-1,-1,");
            _sb.AppendFixed(ToMb(_totalUsed), 1).Append(',')
               .AppendFixed(ToMb(_gcReserved), 1).Append(',')
               .AppendFixed(ToMb(_systemUsed), 1).Append(',')
               .AppendInt(GC.CollectionCount(0)).Append(',')
               .AppendInt(_combats).Append(',')
               .AppendInt(_results).Append(',')
               .AppendInt(_latN).Append(',')
               .AppendFixed(_latN > 0 ? (float)(_latSum / _latN) : -1f, 1).Append(',')
               .AppendInt(_latN > 0 ? _latMax : -1);
            _writer.Write(_sb);
            _writer.WriteLine();
            _writer.Flush();

            _count = 0;
            _gcFrames = 0;
            _gcBytes = 0;
            _skipGcSample = true;
            _latN = 0;
            _latSum = 0;
            _latMax = 0;
        }

        private static float ToMb(ProfilerRecorder r) => r.Valid ? r.LastValue / (1024f * 1024f) : -1f;

        private static string PhaseKey(GamePhase p)
        {
            switch (p)
            {
                case GamePhase.MediaWall: return "wall";
                case GamePhase.Awaken: return "awaken";
                case GamePhase.Combat: return "combat";
                case GamePhase.Result: return "result";
                default: return "other";
            }
        }

        private void OnDestroy()
        {
            if (_onFrame != null) foreach (IPointSource p in _points) p.FrameReceived -= _onFrame;
            if (_writer != null)
            {
                WriteRow(Time.realtimeSinceStartup);
                _writer.Dispose();
                _writer = null;
            }
            _gcInFrame.Dispose();
            _totalUsed.Dispose();
            _gcReserved.Dispose();
            _systemUsed.Dispose();
        }
    }
}
