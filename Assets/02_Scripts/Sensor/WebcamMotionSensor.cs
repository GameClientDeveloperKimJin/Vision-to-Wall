using System;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VisionToWall.Core;

namespace VisionToWall.Sensor
{
    /// <summary>
    /// Virtual LiDAR. FR-01 webcam input, FR-02 GPU downsampling, FR-03 frame differencing.
    /// Does nothing until <see cref="Initialize"/> is called. Submits one <see cref="MotionFrame"/>
    /// per processed camera frame (Count 0 included). Zero allocations per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class WebcamMotionSensor : MonoBehaviour, IDebugViewProvider
    {
        [Tooltip("Readback row 0 is the top row on this platform. Toggle if motion appears vertically inverted.")]
        [SerializeField] private bool flipReadbackY = false;

        [Tooltip("Maximum width of the mirrored preview texture (debug view + first downsample stage).")]
        [SerializeField, Min(16)] private int previewMaxWidth = 640;

        [Tooltip("Brightness gain for the DiffTexture debug view.")]
        [SerializeField, Min(1f)] private float debugDiffGain = 4f;

        private static readonly Color32 GridIdleColor = new Color32(24, 24, 24, 255);
        private static readonly Color32 GridLowColor = new Color32(255, 220, 0, 255);
        private static readonly Color32 GridHighColor = new Color32(255, 0, 0, 255);

        private AppConfig _config;
        private IMotionFrameSink _sink;
        private WebCamTexture _webcam;
        private GridDownsampler _downsampler;
        private FrameDiffDetector _detector;
        private MotionFrame _frame;

        private NativeArray<Color32> _readback;
        private Action<AsyncGPUReadbackRequest> _onReadback;
        private bool _readbackPending;

        private Texture2D _diffTexture;
        private Texture2D _gridTexture;
        private Color32[] _diffPixels;
        private Color32[] _gridPixels;

        private bool _initialized;
        private bool _disposed;
        private bool _lastMirror;
        private uint _seq;

        /// <summary>Camera opened and playing.</summary>
        public bool IsRunning => _webcam != null && _webcam.isPlaying;

        /// <summary>Opened device name, or empty if none.</summary>
        public string DeviceName { get; private set; } = string.Empty;

        /// <summary>Points in the last submitted frame (debug).</summary>
        public int LastCount { get; private set; }

        public Texture SourceTexture => _downsampler != null ? _downsampler.Preview : null;
        public Texture DiffTexture => _diffTexture;
        public Texture GridTexture => _gridTexture;
        public int GridWidth => _detector != null ? _detector.Width : 0;
        public int GridHeight => _detector != null ? _detector.Height : 0;

        /// <summary>Opens the camera and allocates all buffers. Call once (App controls order).</summary>
        public void Initialize(AppConfig config, IMotionFrameSink sink)
        {
            if (_initialized)
            {
                Debug.LogWarning("[Sensor] Initialize called twice. Ignored.", this);
                return;
            }
            if (config == null) throw new ArgumentNullException(nameof(config));

            _initialized = true;
            _config = config;
            _sink = sink;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Debug.LogError("[Sensor] AsyncGPUReadback not supported on this device. Sensor disabled.", this);
                return;
            }

            if (!OpenCamera(config)) return;

            AllocateBuffers(config);
            _lastMirror = ConfigService.Current.mirror;
        }

        private bool OpenCamera(AppConfig config)
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            LogDevices(devices);

            if (devices.Length == 0)
            {
                Debug.LogWarning("[Sensor] No webcam found. Sensor not running.", this);
                return false;
            }

            if (config.cameraIndex < 0 || config.cameraIndex >= devices.Length)
            {
                Debug.LogWarning($"[Sensor] cameraIndex {config.cameraIndex} out of range (0..{devices.Length - 1}). Sensor not running.", this);
                return false;
            }

            DeviceName = devices[config.cameraIndex].name;
            _webcam = new WebCamTexture(DeviceName, config.cameraWidth, config.cameraHeight, config.cameraFps)
            {
                name = "Sensor.Webcam"
            };
            _webcam.Play();

            if (!_webcam.isPlaying)
            {
                Debug.LogWarning($"[Sensor] Failed to start webcam '{DeviceName}'. It may be in use by another app.", this);
            }
            else
            {
                Debug.Log($"[Sensor] Opened [{config.cameraIndex}] '{DeviceName}' request {config.cameraWidth}x{config.cameraHeight}@{config.cameraFps}", this);
            }
            return true;
        }

        private void AllocateBuffers(AppConfig config)
        {
            int gw = config.gridWidth;
            int gh = config.gridHeight;

            int previewW = Mathf.Min(config.cameraWidth, previewMaxWidth);
            int previewH = Mathf.Max(1, Mathf.RoundToInt(previewW * (float)config.cameraHeight / config.cameraWidth));

            _downsampler = new GridDownsampler(previewW, previewH, gw, gh);
            _detector = new FrameDiffDetector(gw, gh) { FlipY = flipReadbackY };
            _frame = new MotionFrame();
            _readback = new NativeArray<Color32>(gw * gh, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _onReadback = OnReadback;

            _diffTexture = CreateDebugTexture("Sensor.Diff", gw, gh);
            _gridTexture = CreateDebugTexture("Sensor.GridCells", gw, gh);
            _diffPixels = new Color32[gw * gh];
            _gridPixels = new Color32[gw * gh];
        }

        private void Update()
        {
            if (_disposed || _downsampler == null || _webcam == null) return;
            if (!_webcam.isPlaying || !_webcam.didUpdateThisFrame) return;

            AppConfig runtime = ConfigService.Current;
            if (runtime.mirror != _lastMirror)
            {
                // Mirror flip moves every pixel; drop the stale reference frame to avoid a false full-screen burst.
                _lastMirror = runtime.mirror;
                _detector.Reset();
            }

            _downsampler.Blit(_webcam, runtime.mirror, _webcam.videoVerticallyMirrored);

            if (_readbackPending) return;
            _readbackPending = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref _readback, _downsampler.Grid, 0, TextureFormat.RGBA32, _onReadback);
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (_disposed || request.hasError) return;

            float threshold = ConfigService.Current.threshold;
            _detector.FlipY = flipReadbackY;

            bool produced = _detector.Process(_readback, threshold, _frame);
            UpdateDebugTextures(threshold);
            if (!produced) return;

            _frame.Seq = ++_seq;
            _frame.TimestampMs = (uint)Environment.TickCount;
            LastCount = _frame.Count;
            _sink?.Submit(_frame);
        }

        private void UpdateDebugTextures(float threshold)
        {
            float[] diff = _detector.Diff;
            int n = diff.Length;
            for (int i = 0; i < n; i++)
            {
                float d = diff[i];

                byte g = (byte)(Mathf.Clamp01(d * debugDiffGain) * 255f);
                _diffPixels[i] = new Color32(g, g, g, 255);

                _gridPixels[i] = d > threshold
                    ? Color32.Lerp(GridLowColor, GridHighColor, FrameDiffDetector.Intensity(d, threshold))
                    : GridIdleColor;
            }

            _diffTexture.SetPixelData(_diffPixels, 0);
            _diffTexture.Apply(false);
            _gridTexture.SetPixelData(_gridPixels, 0);
            _gridTexture.Apply(false);
        }

        private void OnDestroy()
        {
            _disposed = true;

            if (_readbackPending)
            {
                // The native array must not be freed while the GPU still writes into it.
                AsyncGPUReadback.WaitAllRequests();
                _readbackPending = false;
            }

            if (_webcam != null)
            {
                if (_webcam.isPlaying) _webcam.Stop();
                Destroy(_webcam);
                _webcam = null;
            }

            _downsampler?.Dispose();
            _downsampler = null;

            if (_readback.IsCreated) _readback.Dispose();

            if (_diffTexture != null) Destroy(_diffTexture);
            if (_gridTexture != null) Destroy(_gridTexture);
            _diffTexture = null;
            _gridTexture = null;
        }

        private static Texture2D CreateDebugTexture(string name, int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static void LogDevices(WebCamDevice[] devices)
        {
            var sb = new StringBuilder();
            sb.Append("[Sensor] Webcam devices: ").Append(devices.Length);
            for (int i = 0; i < devices.Length; i++)
            {
                sb.Append("\n  [").Append(i).Append("] ").Append(devices[i].name);
                if (devices[i].isFrontFacing) sb.Append(" (front)");
            }
            Debug.Log(sb.ToString());
        }
    }
}
