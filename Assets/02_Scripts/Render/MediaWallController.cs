using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Render
{
    /// <summary>
    /// FR-07. Maps normalized motion points onto the wall, emits particles and feeds ripples to RippleWall.shader.
    /// <para>
    /// Packets arrive at ~25 Hz while rendering runs at 60+ fps, and point counts swing hard frame to frame
    /// (~0 at each hand reversal). So packets only update state (held points, target activity, new ripples);
    /// emission and shader intensity are driven per render frame from an exponentially smoothed activity value.
    /// </para>
    /// Zero GC per frame: EmitParams, the held frame and the ripple array are allocated once.
    /// </summary>
    public sealed class MediaWallController : MonoBehaviour
    {
        [Header("Scene refs")]
        [SerializeField] private Camera wallCamera;
        [SerializeField] private MeshRenderer wallRenderer;
        [SerializeField] private ParticleSystem motionParticles;
        [SerializeField] private AttractModeView attractView;
        [Tooltip("Wall quad distance in front of the camera (world units).")]
        [SerializeField, Min(0.1f)] private float wallDistance = 10f;

        [Header("Colors (data-driven theme hook, v2.2)")]
        [SerializeField] private Color backgroundTop = new Color(0.03f, 0.06f, 0.16f);
        [SerializeField] private Color backgroundBottom = new Color(0f, 0f, 0.03f);
        [SerializeField] private Color glowColor = new Color(0.35f, 0.85f, 1f);
        [SerializeField] private Color particleColorLow = new Color(0.2f, 0.45f, 1f, 1f);
        [SerializeField] private Color particleColorHigh = new Color(0.75f, 1f, 1f, 1f);

        [Header("Smoothing")]
        [Tooltip("Time constant of the exponential activity filter.")]
        [SerializeField, Range(0.05f, 1f)] private float activitySmoothingSec = 0.25f;
        [Tooltip("Sum of point intensities that maps to full activity.")]
        [SerializeField, Min(0.1f)] private float activitySaturation = 8f;
        [Tooltip("No packet for this long -> target activity drops to 0 (network loss).")]
        [SerializeField, Min(0.05f)] private float packetStaleSec = 0.25f;

        [Header("Particles")]
        [SerializeField, Min(0f)] private float emitPerSecond = 1200f;
        [SerializeField, Range(1, 256)] private int maxEmitPerFrame = 64;
        [SerializeField] private Vector2 sizeRange = new Vector2(0.25f, 0.9f);
        [SerializeField] private Vector2 lifetimeRange = new Vector2(0.6f, 1.4f);
        [Tooltip("Random spread around a point, in normalized wall units (about half a grid cell).")]
        [SerializeField, Range(0f, 0.1f)] private float positionJitter = 0.025f;
        [SerializeField, Min(0f)] private float driftSpeed = 0.6f;

        [Header("Ripples")]
        [SerializeField, Range(0, 8)] private int ripplesPerPacket = 1;
        [SerializeField, Range(0f, 1f)] private float rippleMinIntensity = 0.3f;
        [SerializeField, Min(0f)] private float rippleStrength = 1f;
        [SerializeField, Min(0.01f)] private float rippleSpeed = 0.35f;
        [SerializeField, Min(0f)] private float rippleDecay = 2.2f;

        [Header("Idle / Active")]
        [SerializeField, Range(0f, 2f)] private float idleIntensity = 0.35f;
        [SerializeField, Range(0f, 2f)] private float activeIntensity = 1f;
        [SerializeField, Min(0.01f)] private float stateBlendSec = 0.5f;

        private const int MaxRipplesPerPacket = 8;

        private static readonly int RipplesId = Shader.PropertyToID("_Ripples");
        private static readonly int RippleCountId = Shader.PropertyToID("_RippleCount");
        private static readonly int RippleTimeId = Shader.PropertyToID("_RippleTime");
        private static readonly int RippleSpeedId = Shader.PropertyToID("_RippleSpeed");
        private static readonly int RippleDecayId = Shader.PropertyToID("_RippleDecay");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
        private static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        private readonly RippleBuffer _ripples = new RippleBuffer();
        private readonly int[] _topIndices = new int[MaxRipplesPerPacket];
        private MotionFrame _held;
        private ParticleSystem.EmitParams _emit;

        private IPointSource _source;
        private IExhibitionStateSource _state;
        private bool _subscribed;
        private bool _setup;
        private Material _material;

        private Vector2 _wallMin;
        private Vector2 _wallMax;
        private float _particleZ;
        private int _screenW;
        private int _screenH;
        private float _orthoSize;

        private float _targetActivity;
        private float _activity;
        private float _blend;
        private float _emitAccumulator;
        private float _lastPacketTime = float.NegativeInfinity;

        /// <summary>Smoothed 0..1 motion activity (exposed for debug UI).</summary>
        public float Activity => _activity;
        public int RippleCount => _ripples.Count;

        public void Initialize(IPointSource source, IExhibitionStateSource state)
        {
            EnsureSetup();
            Unsubscribe();
            _source = source;
            _state = state;
            _held.Clear();
            _ripples.Clear();
            _targetActivity = 0f;
            _activity = 0f;
            _blend = state != null && state.Current == ExhibitionState.Active ? 1f : 0f;
            if (isActiveAndEnabled) Subscribe();
            if (attractView != null) attractView.Bind(state);
        }

        private void Awake() => EnsureSetup();

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void OnValidate()
        {
            if (_material != null) ApplyStaticMaterialParams();
        }

        private void EnsureSetup()
        {
            if (_setup) return;
            _setup = true;

            _held = new MotionFrame();
            _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            if (wallCamera == null) wallCamera = GetComponentInChildren<Camera>();
            if (wallRenderer != null)
            {
                _material = wallRenderer.material; // per-instance copy, destroyed in OnDestroy
                ApplyStaticMaterialParams();
            }
            else
            {
                Debug.LogError("[Render] MediaWallController: wallRenderer not assigned.", this);
            }

            UpdateLayout(force: true);
        }

        private void Subscribe()
        {
            if (_subscribed || _source == null) return;
            _source.FrameReceived += OnFrameReceived;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _source.FrameReceived -= OnFrameReceived;
            _subscribed = false;
        }

        private void ApplyStaticMaterialParams()
        {
            _material.SetColor(ColorTopId, backgroundTop);
            _material.SetColor(ColorBottomId, backgroundBottom);
            _material.SetColor(GlowColorId, glowColor);
            _material.SetFloat(RippleSpeedId, rippleSpeed);
            _material.SetFloat(RippleDecayId, rippleDecay);
        }

        // ---------------------------------------------------------------- packet side (~25 Hz)

        private void OnFrameReceived(MotionFrame frame)
        {
            float now = Time.unscaledTime;
            _lastPacketTime = now;

            int count = frame.Count;
            MotionPoint[] points = frame.Points;

            float sumV = 0f;
            for (int i = 0; i < count; i++) sumV += points[i].v;
            _targetActivity = Mathf.Clamp01(sumV / activitySaturation);

            // Hold the last non-empty set so emission fades out at the last positions instead of vanishing.
            if (count > 0) _held.CopyFrom(frame);

            AddRipples(points, count, now);
        }

        private void AddRipples(MotionPoint[] points, int count, float now)
        {
            int k = Mathf.Min(ripplesPerPacket, MaxRipplesPerPacket);
            if (k <= 0 || count == 0) return;

            // Partial selection of the k strongest points: O(count * k), no sort, no allocation.
            int found = 0;
            for (int i = 0; i < count; i++)
            {
                float v = points[i].v;
                if (v < rippleMinIntensity) continue;

                int slot;
                if (found < k)
                {
                    slot = found++;
                }
                else
                {
                    slot = 0;
                    for (int s = 1; s < k; s++)
                        if (points[_topIndices[s]].v < points[_topIndices[slot]].v) slot = s;
                    if (points[_topIndices[slot]].v >= v) continue;
                }
                _topIndices[slot] = i;
            }

            for (int s = 0; s < found; s++)
            {
                MotionPoint p = points[_topIndices[s]];
                _ripples.TryAdd(p.x, p.y, now, p.v * rippleStrength);
            }
        }

        // ---------------------------------------------------------------- render side (every frame)

        private void Update()
        {
            UpdateLayout(force: false);

            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            if (now - _lastPacketTime > packetStaleSec) _targetActivity = 0f;
            _activity += (_targetActivity - _activity) * (1f - Mathf.Exp(-dt / activitySmoothingSec));

            bool active = _state != null && _state.Current == ExhibitionState.Active;
            _blend = Mathf.MoveTowards(_blend, active ? 1f : 0f, dt / stateBlendSec);

            EmitParticles(dt);
            PushShaderParams(now);
        }

        private void EmitParticles(float dt)
        {
            if (motionParticles == null) return;

            int count = _held.Count;
            if (count == 0 || _activity < 0.001f)
            {
                _emitAccumulator = 0f;
                return;
            }

            _emitAccumulator += emitPerSecond * _activity * dt;
            int n = Mathf.Min((int)_emitAccumulator, maxEmitPerFrame);
            if (n <= 0) return;
            _emitAccumulator = Mathf.Min(_emitAccumulator - n, maxEmitPerFrame); // no backlog after a hitch

            // Evenly spaced sampling with a random phase so every point gets coverage over time.
            float stride = count / (float)n;
            float phase = Random.value * stride;
            MotionPoint[] points = _held.Points;

            for (int i = 0; i < n; i++)
            {
                int idx = (int)(phase + i * stride);
                if (idx >= count) idx = count - 1;
                MotionPoint p = points[idx];

                Vector2 jitter = Random.insideUnitCircle * positionJitter;
                Vector2 world = NormalizedToWorld(p.x + jitter.x, p.y + jitter.y);

                float v = Mathf.Clamp01(p.v);
                Color c = Color.Lerp(particleColorLow, particleColorHigh, v);
                c.a = Mathf.Lerp(0.25f, 1f, v) * Mathf.Lerp(0.5f, 1f, _activity);

                Vector2 drift = Random.insideUnitCircle * driftSpeed;
                _emit.position = new Vector3(world.x, world.y, _particleZ);
                _emit.velocity = new Vector3(drift.x, drift.y + driftSpeed * 0.5f, 0f);
                _emit.startSize = Mathf.Lerp(sizeRange.x, sizeRange.y, v);
                _emit.startLifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
                _emit.startColor = c;
                motionParticles.Emit(_emit, 1);
            }
        }

        private void PushShaderParams(float now)
        {
            if (_material == null) return;

            float target = Mathf.Lerp(idleIntensity, activeIntensity * Mathf.Lerp(0.7f, 1f, _activity), _blend);
            _material.SetVectorArray(RipplesId, _ripples.Data);
            _material.SetFloat(RippleCountId, _ripples.Count);
            _material.SetFloat(RippleTimeId, now);
            _material.SetFloat(IntensityId, target);
        }

        // ---------------------------------------------------------------- layout

        /// <summary>FR-07 coordinate mapping. (0,0) = bottom-left, (1,1) = top-right of the wall.</summary>
        public Vector2 NormalizedToWorld(float x, float y)
        {
            return new Vector2(
                Mathf.LerpUnclamped(_wallMin.x, _wallMax.x, x),
                Mathf.LerpUnclamped(_wallMin.y, _wallMax.y, y));
        }

        private void UpdateLayout(bool force)
        {
            if (wallCamera == null) return;
            if (!force && Screen.width == _screenW && Screen.height == _screenH &&
                Mathf.Approximately(wallCamera.orthographicSize, _orthoSize)) return;

            _screenW = Screen.width;
            _screenH = Screen.height;
            _orthoSize = wallCamera.orthographicSize;

            float height = _orthoSize * 2f;
            float aspect = wallCamera.aspect;
            float width = height * aspect;

            Transform cam = wallCamera.transform;
            Vector3 center = cam.position + cam.forward * wallDistance;
            _wallMin = new Vector2(center.x - width * 0.5f, center.y - height * 0.5f);
            _wallMax = new Vector2(center.x + width * 0.5f, center.y + height * 0.5f);
            _particleZ = center.z - 0.5f; // just in front of the wall

            if (wallRenderer != null)
            {
                Transform wall = wallRenderer.transform;
                wall.position = center;
                wall.rotation = cam.rotation;
                wall.localScale = new Vector3(width, height, 1f);
            }

            if (_material != null) _material.SetFloat(AspectId, aspect);
        }
    }
}
