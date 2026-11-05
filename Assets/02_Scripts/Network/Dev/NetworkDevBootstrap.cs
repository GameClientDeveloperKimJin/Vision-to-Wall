using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Network.Dev
{
    /// <summary>
    /// Loopback dev scene driver: binds a <see cref="UdpMotionReceiver"/> on config.udpPort and shows
    /// sent / received / dropped counters. Pair with a <see cref="FakeMotionSender"/> (same scene) or
    /// Tools/FakeSender/fake_sender.py.
    /// IMGUI and logging allocate; turn both off (Inspector) when profiling GC.
    /// </summary>
    [RequireComponent(typeof(UdpMotionReceiver))]
    public sealed class NetworkDevBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional. Shows sender-side counters for the loopback test.")]
        private FakeMotionSender fakeSender;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logEnabled = true;
        [SerializeField, Min(0.1f)] private float logIntervalSec = 1f;

        private const float Pad = 8f;
        private const float PanelWidth = 520f;
        private const float LineHeight = 20f;
        private const float MarkerSize = 12f;
        private static readonly Color PlotBg = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color MarkerColor = new Color(0.2f, 1f, 0.4f, 1f);
        private const float PlotSize = 200f;

        private UdpMotionReceiver _receiver;
        private readonly MotionFrame _last = new MotionFrame();
        private int _udpPort;
        private float _nextLogTime;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();
            _udpPort = config.udpPort;
            _receiver = GetComponent<UdpMotionReceiver>();
            _receiver.Initialize(config);
        }

        private void OnEnable()
        {
            if (_receiver == null) _receiver = GetComponent<UdpMotionReceiver>();
            _receiver.FrameReceived += OnFrame;
        }

        private void OnDisable()
        {
            if (_receiver != null) _receiver.FrameReceived -= OnFrame;
        }

        private void OnFrame(MotionFrame frame) => _last.CopyFrom(frame);

        private void Update()
        {
            if (!logEnabled || Time.unscaledTime < _nextLogTime) return;
            _nextLogTime = Time.unscaledTime + logIntervalSec;

            UdpMotionSender s = fakeSender != null ? fakeSender.Sender : null;
            Debug.Log(s != null
                ? $"[NetworkDev] sent={s.FramesSent} skipped={s.FramesSkipped} sendErr={s.SendErrors} | recv={_receiver.PacketsReceived} drop={_receiver.PacketsDropped} (pool {_receiver.DroppedPoolExhausted} invalid {_receiver.DroppedInvalid} seq {_receiver.DroppedSeq} socket {_receiver.DroppedSocket}) lastSeq={_receiver.LastSeq} count={_last.Count} fps={1f / Time.unscaledDeltaTime:F0}"
                : $"[NetworkDev] recv={_receiver.PacketsReceived} drop={_receiver.PacketsDropped} (pool {_receiver.DroppedPoolExhausted} invalid {_receiver.DroppedInvalid} seq {_receiver.DroppedSeq} socket {_receiver.DroppedSocket}) lastSeq={_receiver.LastSeq} count={_last.Count} fps={1f / Time.unscaledDeltaTime:F0}");
        }

        private void OnGUI()
        {
            if (!showOverlay || _receiver == null) return;

            float y = Pad;
            Line(ref y, $"UDP port {_udpPort}  receiver running={_receiver.IsRunning}");

            UdpMotionSender s = fakeSender != null ? fakeSender.Sender : null;
            if (s != null)
                Line(ref y, $"Sent {s.FramesSent}   Skipped {s.FramesSkipped}   SendErr {s.SendErrors}");

            Line(ref y, $"Recv {_receiver.PacketsReceived}   Drop {_receiver.PacketsDropped}   LastSeq {_receiver.LastSeq}");
            Line(ref y, $"  Drop: pool {_receiver.DroppedPoolExhausted}  invalid {_receiver.DroppedInvalid}  seq {_receiver.DroppedSeq}  socket {_receiver.DroppedSocket}");
            float age = _receiver.LastPacketTime < 0f ? -1f : Time.unscaledTime - _receiver.LastPacketTime;
            Line(ref y, $"Last packet age {age:F2}s   Count {_last.Count}");

            // Plot last frame points (origin bottom-left -> flip for GUI).
            var plot = new Rect(Pad, y + Pad, PlotSize, PlotSize);
            Color prev = GUI.color;
            GUI.color = PlotBg;
            GUI.DrawTexture(plot, Texture2D.whiteTexture);
            GUI.color = MarkerColor;
            for (int i = 0; i < _last.Count; i++)
            {
                MotionPoint p = _last.Points[i];
                float px = plot.x + p.x * plot.width - MarkerSize * 0.5f;
                float py = plot.y + (1f - p.y) * plot.height - MarkerSize * 0.5f;
                GUI.DrawTexture(new Rect(px, py, MarkerSize, MarkerSize), Texture2D.whiteTexture);
            }
            GUI.color = prev;
        }

        private static void Line(ref float y, string text)
        {
            GUI.Label(new Rect(Pad, y, PanelWidth, LineHeight), text);
            y += LineHeight;
        }
    }
}
