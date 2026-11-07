using UnityEngine;
using VisionToWall.Core;
using VisionToWall.Network;

namespace VisionToWall.UI.Dev
{
    /// <summary>
    /// MiniCharacterDev scene driver: binds <see cref="UdpPoseReceiver"/> on config.udpPosePort and drives the
    /// mini character with <see cref="MockGameSources"/> (keys: 0 media wall, 1 awaken, 2 combat, V/G result).
    /// Send joints with Tools/HandSensor fake_pose_sender.py (no camera) or hand_sensor.py.
    /// IMGUI and logging allocate; turn both off (Inspector) when profiling GC.
    /// </summary>
    public sealed class MiniCharacterDevBootstrap : MonoBehaviour
    {
        [SerializeField] private UdpPoseReceiver receiver;
        [SerializeField] private MiniCharacterView view;
        [SerializeField] private MockGameSources gameSources;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool logEnabled = true;
        [SerializeField, Min(0.1f)] private float logIntervalSec = 1f;

        private float _nextLog;

        private void Start()
        {
            AppConfig config = ConfigService.Load();
            int port = config.udpPosePort != 0 ? config.udpPosePort : 9002;
            receiver.Initialize(config, port);
            view.Initialize(receiver, gameSources, config);
            Debug.Log($"[MiniCharacterDev] Listening on {port}, awakenTransitionSec={config.awakenTransitionSec}");
        }

        private void Update()
        {
            if (!logEnabled || Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + logIntervalSec;
            Debug.Log(StatusLine());
        }

        private string StatusLine() =>
            $"[MiniCharacterDev] phase={gameSources.Phase} rx={receiver.PacketsReceived} empty={receiver.EmptyPacketsReceived} " +
            $"drop={receiver.PacketsDropped} (pool {receiver.DroppedPoolExhausted}, invalid {receiver.DroppedInvalid}, " +
            $"seq {receiver.DroppedSeq}, socket {receiver.DroppedSocket}) seq={receiver.LastSeq} " +
            $"person={receiver.Latest.HasPerson} panel={view.PanelAlpha:0.00} figure={view.FigurePresence:0.00}";

        private void OnGUI()
        {
            if (!showOverlay) return;
            GUI.Label(new Rect(10f, 10f, 1200f, 24f), StatusLine());
            GUI.Label(new Rect(10f, 32f, 1200f, 24f), "0 media wall · 1 awaken · 2 combat · V victory · G game over");
        }
    }
}
