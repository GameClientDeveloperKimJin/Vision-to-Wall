using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Sensor.Dev
{
    /// <summary>
    /// Dev scene driver: loads config, wires <see cref="WebcamMotionSensor"/> to <see cref="LogSink"/>
    /// and draws the three debug textures plus threshold/mirror controls with IMGUI.
    /// IMGUI allocates every frame; turn the overlay off (Inspector) when profiling GC.
    /// </summary>
    [RequireComponent(typeof(WebcamMotionSensor))]
    public sealed class SensorDevBootstrap : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logEnabled = true;
        [SerializeField, Min(0.05f)] private float logIntervalSec = 0.5f;
        [SerializeField, Min(32f)] private float viewSize = 320f;

        private const float Pad = 8f;
        private const float ThresholdMin = 0.01f;
        private const float ThresholdMax = 0.5f;

        private WebcamMotionSensor _sensor;
        private LogSink _sink;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();
            _sink = new LogSink { LogEnabled = logEnabled, LogIntervalSec = logIntervalSec };
            _sensor = GetComponent<WebcamMotionSensor>();
            _sensor.Initialize(config, _sink);
        }

        private void OnValidate()
        {
            if (_sink != null)
            {
                _sink.LogEnabled = logEnabled;
                _sink.LogIntervalSec = logIntervalSec;
            }
        }

        private void OnGUI()
        {
            // _sink is not serialized, so it is null after a script reload during Play.
            if (!showOverlay || _sensor == null || _sink == null) return;

            Texture src = _sensor.SourceTexture;
            float aspect = src != null ? (float)src.height / src.width : 9f / 16f;
            float h = viewSize * aspect;

            float x = Pad;
            DrawView(new Rect(x, Pad, viewSize, h), src, "Source");
            x += viewSize + Pad;
            DrawView(new Rect(x, Pad, viewSize, h), _sensor.DiffTexture, "Diff");
            x += viewSize + Pad;
            DrawView(new Rect(x, Pad, viewSize, h), _sensor.GridTexture, "Grid");

            AppConfig c = ConfigService.Current;
            float y = h + Pad * 2f;

            GUI.Label(new Rect(Pad, y, 1000f, 40f),
                $"Device: {_sensor.DeviceName}  Running: {_sensor.IsRunning}  Grid: {_sensor.GridWidth}x{_sensor.GridHeight}\n" +
                $"Count: {_sink.LastCount}  meanX: {_sink.LastMeanX:F2}  meanY: {_sink.LastMeanY:F2}  maxV: {_sink.LastMaxV:F2}  seq: {_sink.LastSeq}");
            y += 44f;

            GUI.Label(new Rect(Pad, y, 140f, 20f), $"Threshold {c.threshold:F3}");
            float t = GUI.HorizontalSlider(new Rect(Pad + 140f, y + 5f, 240f, 20f), c.threshold, ThresholdMin, ThresholdMax);
            if (!Mathf.Approximately(t, c.threshold))
            {
                c.threshold = t;
                ConfigService.NotifyChanged();
            }
            y += 24f;

            bool m = GUI.Toggle(new Rect(Pad, y, 120f, 20f), c.mirror, "Mirror");
            if (m != c.mirror)
            {
                c.mirror = m;
                ConfigService.NotifyChanged();
            }

            bool log = GUI.Toggle(new Rect(Pad + 130f, y, 120f, 20f), logEnabled, "Log");
            if (log != logEnabled)
            {
                logEnabled = log;
                _sink.LogEnabled = log;
            }
        }

        private static void DrawView(Rect r, Texture tex, string label)
        {
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
            else GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width, 20f), label);
        }
    }
}
