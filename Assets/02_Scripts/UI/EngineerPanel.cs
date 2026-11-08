using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VisionToWall.Core;

namespace VisionToWall.UI
{
    /// <summary>
    /// FR-08 F1 engineer overlay. Closed: no text updates, no packet logging, cursor hidden (kiosk).
    /// Open: cursor visible, text refreshed at <see cref="refreshHz"/> (4 Hz). All formatting is allocation-free.
    /// </summary>
    public sealed class EngineerPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text networkText;
        [SerializeField] private TMP_Text gameText;
        [Tooltip("Device, pose joints and weather (OP). Optional: left out of the panel when not assigned.")]
        [SerializeField] private TMP_Text roadmapText;

        [Header("Debug views")]
        [Tooltip("Container of the three debug views; hidden when there is no IDebugViewProvider (external sensor).")]
        [SerializeField] private GameObject viewsRoot;
        [SerializeField] private RawImage sourceView;
        [SerializeField] private RawImage diffView;
        [SerializeField] private RawImage gridView;

        [Header("Sub views")]
        [SerializeField] private PacketLogView packetLog;
        [SerializeField] private ConfigEditorView configEditor;

        [Header("Behaviour")]
        [SerializeField] private bool startOpen;
        [SerializeField] private bool hideCursorWhenClosed = true;
        [SerializeField, Range(1f, 30f)] private float refreshHz = 4f;
        [Tooltip("Creates an EventSystem (InputSystemUIInputModule) if the scene has none, so the slider works.")]
        [SerializeField] private bool ensureEventSystem = true;

        private readonly StringBuilder _sb = new StringBuilder(256);
        private readonly FpsCounter _fps = new FpsCounter();

        private IDebugViewProvider _debugViews;
        private IPointSource _points;
        private INetworkStats _stats;
        private IExhibitionStateSource _state;
        private IGamePhaseSource _gamePhase;
        private IPlayerInputSource _playerInput;
        private INetworkStats _bodyStats;
        private IDevicePanelSource _device;
        private INetworkStats _poseStats;
        private IPoseSource _pose;
        private IWeatherPanelSource _weather;
        private readonly byte[] _devicePacket = new byte[DeviceProtocol.PacketSize];

        private Action<MotionFrame> _onFrame;
        private bool _subscribed;
        private bool _open;
        private float _nextRefresh;
        private string _cameraName = string.Empty;

        public bool IsOpen => _open;

        private void Awake()
        {
            _onFrame = OnFrameReceived; // cached: no delegate allocation on toggle
            if (ensureEventSystem) EnsureEventSystem();
        }

        private void Start() => SetOpen(startOpen);

        private void OnDestroy() => Unsubscribe();

        /// <summary>Called once by the App. Any argument may be null (that section shows "n/a").</summary>
        public void Initialize(
            IDebugViewProvider debugViews,
            IPointSource points,
            INetworkStats stats,
            IExhibitionStateSource state)
        {
            Unsubscribe();
            _debugViews = debugViews;
            if (viewsRoot != null) viewsRoot.SetActive(debugViews != null);
            _points = points;
            _stats = stats;
            _state = state;
            if (_open) Subscribe();
        }

        /// <summary>Camera device name for the status line (not part of <see cref="IDebugViewProvider"/>).</summary>
        public void SetCameraInfo(string deviceName) => _cameraName = deviceName ?? string.Empty;

        /// <summary>Game section sources. <paramref name="bodyStatsOrNull"/> is null when there is no body stream (webcam mode).</summary>
        public void SetGameSources(IGamePhaseSource phase, IPlayerInputSource input, INetworkStats bodyStatsOrNull)
        {
            _gamePhase = phase;
            _playerInput = input;
            _bodyStats = bodyStatsOrNull;
        }

        /// <summary>
        /// Roadmap section sources (device, pose joints, weather). Any argument may be null: device/weather show
        /// "연결 없음", pose shows "꺼짐" (webcam mode or udpPosePort 0). Read only while the panel is open.
        /// </summary>
        public void SetRoadmapSources(IDevicePanelSource deviceOrNull, INetworkStats poseStatsOrNull,
            IPoseSource poseOrNull, IWeatherPanelSource weatherOrNull)
        {
            _device = deviceOrNull;
            _poseStats = poseStatsOrNull;
            _pose = poseOrNull;
            _weather = weatherOrNull;
        }

        public void Toggle() => SetOpen(!_open);

        public void SetOpen(bool open)
        {
            _open = open;
            if (panelRoot != null) panelRoot.SetActive(open);
            if (hideCursorWhenClosed) Cursor.visible = open;

            if (open)
            {
                _fps.Reset();
                if (packetLog != null) packetLog.Clear();
                if (configEditor != null) configEditor.SyncFromConfig();
                Subscribe();
                _nextRefresh = 0f; // refresh on the next frame
            }
            else
            {
                Unsubscribe();
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) Toggle();
            if (!_open) return;

            _fps.Sample(Time.unscaledDeltaTime);

            float now = Time.unscaledTime;
            if (now < _nextRefresh) return;
            _nextRefresh = now + 1f / refreshHz;
            Refresh(now);
        }

        private void Refresh(float now)
        {
            BindDebugViews();
            WriteStatus();
            WriteNetwork(now);
            WriteGame();
            WriteRoadmap();
            if (packetLog != null) packetLog.Render();
        }

        private void WriteGame()
        {
            if (gameText == null) return;
            _sb.Length = 0;
            GameHudText.AppendGameStatus(_sb, _gamePhase, _playerInput, _bodyStats, ConfigService.Current.udpBodyPort);
            gameText.SetText(_sb);
        }

        private void WriteRoadmap()
        {
            if (roadmapText == null) return;
            _sb.Length = 0;
            RoadmapStatusText.Append(_sb, _device, _poseStats, _pose, ConfigService.Current.udpPosePort, _weather,
                DateTime.UtcNow, _devicePacket);
            roadmapText.SetText(_sb);
        }

        private void OnFrameReceived(MotionFrame frame)
        {
            if (packetLog != null) packetLog.Record(frame);
        }

        private void Subscribe()
        {
            if (_subscribed || _points == null) return;
            _points.FrameReceived += _onFrame;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_points != null) _points.FrameReceived -= _onFrame;
            _subscribed = false;
        }

        // Textures may be created after Initialize (webcam start), so bind lazily; assignment happens once.
        private void BindDebugViews()
        {
            if (_debugViews == null) return;
            Bind(sourceView, _debugViews.SourceTexture, false);
            Bind(diffView, _debugViews.DiffTexture, true);
            Bind(gridView, _debugViews.GridTexture, true);
        }

        private static void Bind(RawImage view, Texture tex, bool point)
        {
            if (view == null) return;
            // A RawImage without a texture draws a white box; hide it until the webcam delivers frames.
            view.enabled = tex != null;
            if (view.texture == tex) return;
            view.texture = tex;
            if (tex != null && point) tex.filterMode = FilterMode.Point;
        }

        // FPS 60  |  상태: 체험 중 (Active)  |  그리드 16x16
        // 카메라: <name> (번호 0)
        private void WriteStatus()
        {
            if (statusText == null) return;
            _sb.Length = 0;
            _sb.Append("FPS ").AppendInt(Mathf.RoundToInt(_fps.Value))
               .Append("  |  상태: ").Append(StateName(_state));
            if (_debugViews != null && _debugViews.GridWidth > 0)
            {
                _sb.Append("  |  그리드 ").AppendInt(_debugViews.GridWidth).Append('x').AppendInt(_debugViews.GridHeight);
            }
            _sb.Append("\n카메라: ").Append(_cameraName.Length > 0 ? _cameraName : "(알 수 없음)");
            if (_debugViews != null)
            {
                _sb.Append(" (번호 ").AppendInt(ConfigService.Current.cameraIndex).Append(')');
                if (_debugViews.SourceTexture == null) _sb.Append("\n<color=#FF8866>영상 없음: 손 센서 등이 웹캠 사용 중?</color>");
            }
            statusText.SetText(_sb);
        }

        // UDP 포트 9000  수신 1234  버림 0
        // 마지막 패킷 #1234 (0.02초 전)
        private void WriteNetwork(float now)
        {
            if (networkText == null) return;
            _sb.Length = 0;
            _sb.Append("UDP 포트 ").AppendInt(ConfigService.Current.udpPort);
            if (_stats == null)
            {
                _sb.Append("  (통계 없음)");
            }
            else
            {
                _sb.Append("  수신 ").AppendInt(_stats.PacketsReceived)
                   .Append("  버림 ").AppendInt(_stats.PacketsDropped);
                _sb.Append("\n마지막 패킷 ");
                if (_stats.LastPacketTime < 0f)
                {
                    _sb.Append("- (아직 수신 없음)");
                }
                else
                {
                    _sb.Append('#').AppendInt(_stats.LastSeq).Append(" (")
                       .AppendFixed(Mathf.Max(0f, now - _stats.LastPacketTime), 2).Append("초 전)");
                }
            }
            networkText.SetText(_sb);
        }

        private static string StateName(IExhibitionStateSource state)
        {
            if (state == null) return "없음";
            switch (state.Current)
            {
                case ExhibitionState.Idle: return "대기 중 (Idle)";
                case ExhibitionState.Active: return "체험 중 (Active)";
                default: return "?";
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem (EngineerPanel)", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }
    }
}
