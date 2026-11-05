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

        [Header("Debug views")]
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
            _points = points;
            _stats = stats;
            _state = state;
            if (_open) Subscribe();
        }

        /// <summary>Camera device name for the status line (not part of <see cref="IDebugViewProvider"/>).</summary>
        public void SetCameraInfo(string deviceName) => _cameraName = deviceName ?? string.Empty;

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
            if (packetLog != null) packetLog.Render();
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
            if (view == null || view.texture == tex) return;
            view.texture = tex;
            view.enabled = tex != null;
            if (tex != null && point) tex.filterMode = FilterMode.Point;
        }

        // FPS 60  |  State: Active
        // Camera: <name> (index 0)  grid 16x16
        private void WriteStatus()
        {
            if (statusText == null) return;
            _sb.Length = 0;
            _sb.Append("FPS ").AppendInt(Mathf.RoundToInt(_fps.Value))
               .Append("  |  State: ").Append(StateName(_state));
            _sb.Append("\nCamera: ").Append(_cameraName.Length > 0 ? _cameraName : "(unknown)")
               .Append(" (index ").AppendInt(ConfigService.Current.cameraIndex).Append(')');
            if (_debugViews != null && _debugViews.GridWidth > 0)
            {
                _sb.Append("  grid ").AppendInt(_debugViews.GridWidth).Append('x').AppendInt(_debugViews.GridHeight);
            }
            statusText.SetText(_sb);
        }

        // UDP :9000  Recv 1234  Drop 0
        // Last seq 1234 (0.02s ago)
        private void WriteNetwork(float now)
        {
            if (networkText == null) return;
            _sb.Length = 0;
            _sb.Append("UDP :").AppendInt(ConfigService.Current.udpPort);
            if (_stats == null)
            {
                _sb.Append("  (stats n/a)");
            }
            else
            {
                _sb.Append("  Recv ").AppendInt(_stats.PacketsReceived)
                   .Append("  Drop ").AppendInt(_stats.PacketsDropped);
                _sb.Append("\nLast seq ");
                if (_stats.LastPacketTime < 0f)
                {
                    _sb.Append("- (no packet yet)");
                }
                else
                {
                    _sb.AppendInt(_stats.LastSeq).Append(" (")
                       .AppendFixed(Mathf.Max(0f, now - _stats.LastPacketTime), 2).Append("s ago)");
                }
            }
            networkText.SetText(_sb);
        }

        private static string StateName(IExhibitionStateSource state)
        {
            if (state == null) return "n/a";
            switch (state.Current)
            {
                case ExhibitionState.Idle: return "Idle";
                case ExhibitionState.Active: return "Active";
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
