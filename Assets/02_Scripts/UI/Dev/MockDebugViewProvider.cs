using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.UI.Dev
{
    /// <summary>
    /// Dev-only <see cref="IDebugViewProvider"/>. Source: animated test pattern (4:3).
    /// Diff/Grid: built from <see cref="MockPointSource"/> frames; grid cells above the live config threshold are lit.
    /// </summary>
    public sealed class MockDebugViewProvider : MonoBehaviour, IDebugViewProvider
    {
        [SerializeField] private MockPointSource source;
        [SerializeField] private int sourceWidth = 160;
        [SerializeField] private int sourceHeight = 120;
        [SerializeField, Range(1f, 60f)] private float sourceHz = 15f;
        [SerializeField] private string deviceName = "Mock Camera";

        private Texture2D _source;
        private Texture2D _diff;
        private Texture2D _grid;
        private Color32[] _sourcePixels;
        private Color32[] _diffPixels;
        private Color32[] _gridPixels;
        private float _nextSource;

        public Texture SourceTexture => _source;
        public Texture DiffTexture => _diff;
        public Texture GridTexture => _grid;
        public int GridWidth => source != null ? source.GridWidth : 0;
        public int GridHeight => source != null ? source.GridHeight : 0;
        public string DeviceName => deviceName;

        private void Awake()
        {
            if (source == null) source = GetComponent<MockPointSource>();
            int gw = Mathf.Max(1, GridWidth);
            int gh = Mathf.Max(1, GridHeight);

            _source = NewTexture("Mock.Source", sourceWidth, sourceHeight, FilterMode.Bilinear);
            _diff = NewTexture("Mock.Diff", gw, gh, FilterMode.Point);
            _grid = NewTexture("Mock.Grid", gw, gh, FilterMode.Point);
            _sourcePixels = new Color32[sourceWidth * sourceHeight];
            _diffPixels = new Color32[gw * gh];
            _gridPixels = new Color32[gw * gh];
        }

        private void OnEnable()
        {
            if (source != null) source.FrameReceived += OnFrame;
        }

        private void OnDisable()
        {
            if (source != null) source.FrameReceived -= OnFrame;
        }

        private void OnDestroy()
        {
            Destroy(_source);
            Destroy(_diff);
            Destroy(_grid);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextSource) return;
            _nextSource = now + 1f / sourceHz;

            for (int y = 0; y < sourceHeight; y++)
            {
                for (int x = 0; x < sourceWidth; x++)
                {
                    float u = (float)x / sourceWidth;
                    float v = (float)y / sourceHeight;
                    byte r = (byte)(127 + 127 * Mathf.Sin(u * 9f + now * 1.3f));
                    byte g = (byte)(127 + 127 * Mathf.Sin(v * 7f - now * 0.9f));
                    byte b = (byte)(127 + 127 * Mathf.Sin((u + v) * 5f + now * 2.1f));
                    _sourcePixels[y * sourceWidth + x] = new Color32(r, g, b, 255);
                }
            }
            _source.SetPixels32(_sourcePixels);
            _source.Apply(false);
        }

        private void OnFrame(MotionFrame frame)
        {
            int gw = _diff.width;
            int gh = _diff.height;
            var black = new Color32(0, 0, 0, 255);
            var dark = new Color32(30, 30, 30, 255);
            for (int i = 0; i < _diffPixels.Length; i++)
            {
                _diffPixels[i] = black;
                _gridPixels[i] = dark;
            }

            float threshold = ConfigService.Current.threshold;
            for (int i = 0; i < frame.Count; i++)
            {
                MotionPoint p = frame.Points[i];
                int gx = Mathf.Clamp((int)(p.x * gw), 0, gw - 1);
                int gy = Mathf.Clamp((int)(p.y * gh), 0, gh - 1);
                byte l = (byte)(Mathf.Clamp01(p.v) * 255f);
                _diffPixels[gy * gw + gx] = new Color32(l, l, l, 255);
                if (p.v >= threshold) _gridPixels[gy * gw + gx] = new Color32(0, 230, 120, 255);
            }

            _diff.SetPixels32(_diffPixels);
            _diff.Apply(false);
            _grid.SetPixels32(_gridPixels);
            _grid.Apply(false);
        }

        private static Texture2D NewTexture(string name, int w, int h, FilterMode filter)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
