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
    public sealed class MediaWallController : MonoBehaviour, IWallPresentation, IWallBaseTheme
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

        [Header("Crimson theme (IWallPresentation)")]
        [SerializeField] private Color backgroundTopCrimson = new Color(0.17f, 0.015f, 0.05f);
        [SerializeField] private Color backgroundBottomCrimson = new Color(0.03f, 0f, 0.012f);
        [SerializeField] private Color glowColorCrimson = new Color(1f, 0.12f, 0.32f);
        [SerializeField] private Color particleColorLowCrimson = new Color(0.9f, 0.05f, 0.25f, 1f);
        [SerializeField] private Color particleColorHighCrimson = new Color(1f, 0.25f, 0.5f, 1f);
        [SerializeField, Tooltip("Optional. Its start color follows the theme.")]
        private ParticleSystem ambientParticles;
        [SerializeField] private Color ambientColorCrimson = new Color(1f, 0.15f, 0.35f, 1f);

        [Header("Weather (IWallBaseTheme, v2.2)")]
        [SerializeField, Tooltip("VisionToWall/WeatherParticle material. Empty = weather particles and sun off.")]
        private Material weatherMaterial;
        [SerializeField, Range(10, 400), Tooltip("Weather particles on screen at density 1.")]
        private int weatherBaseCount = 90;
        [SerializeField, Range(0f, 1f), Tooltip("Weather particle brightness at rest.")]
        private float weatherAlpha = 0.7f;
        [SerializeField, Range(0f, 1f), Tooltip("Weather fade at full hand activity, so the hand reaction stays readable.")]
        private float weatherHandDim = 0.6f;
        [SerializeField, Range(0f, 1f), Tooltip("Weather particle brightness left at full combat crimson (veil and sun go to 0).")]
        private float weatherCombatKeep = 0.35f;
        [SerializeField] private Color sunColor = new Color(1f, 0.82f, 0.5f, 1f);

        [Header("Absorb (IWallPresentation)")]
        [SerializeField, Tooltip("Pull toward the boss. Particles need the External Forces module on.")]
        private ParticleSystemForceField absorbField;
        [SerializeField, Min(0f), Tooltip("Field gravity at full strength.")]
        private float absorbGravity = 10f;

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

        private float _crimson;
        private float _absorb;
        private Color _particleLow;
        private Color _particleHigh;
        private Color _ambientColor;

        // Calm (crimson 0) colors. Start as the serialized values; IWallBaseTheme replaces them per weather.
        private Color _baseTop;
        private Color _baseBottom;
        private Color _baseGlow;
        private Color _baseLow;
        private Color _baseHigh;

        // Weather layer (IWallBaseTheme.SetAtmosphere). Off for the default atmosphere: the wall is then untouched.
        private const int WeatherMaxParticles = 800;
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");
        private ParticleSystem _weatherParticles;
        private Material _weatherMat;
        private Transform _sun;
        private Material _sunMat;
        private Texture2D _sunTex;
        private WeatherAtmosphere _atmosphere;
        private int _weatherGrid = 1;
        private bool _weatherOn;
        private float _veil;
        private float _weatherFade = -1f;
        private float _wallHeight;
        private float _wallWidth;

        /// <summary>Smoothed 0..1 motion activity (exposed for debug UI).</summary>
        public float Activity => _activity;
        public int RippleCount => _ripples.Count;
        public float CrimsonBlend => _crimson;
        public float AbsorbStrength => _absorb;
        /// <summary>True while a non-default weather atmosphere is shown (debug, tests).</summary>
        public bool WeatherLayerOn => _weatherOn;
        public float WeatherFade => _weatherFade;

        /// <summary>Lerps wall and particle colors. Writes only when the value changes.</summary>
        public void SetTheme(float crimsonBlend)
        {
            EnsureSetup();
            crimsonBlend = Mathf.Clamp01(crimsonBlend);
            if (Mathf.Approximately(crimsonBlend, _crimson)) return;

            _crimson = crimsonBlend;
            ApplyTheme();
        }

        /// <summary>Pulls wall particles toward a point; its depth is replaced by the particle plane. 0 turns it off.</summary>
        public void SetAbsorb(float strength, Vector3 worldCenter)
        {
            EnsureSetup();
            strength = Mathf.Clamp01(strength);
            bool changed = !Mathf.Approximately(strength, _absorb);
            _absorb = strength;
            if (absorbField == null) return;

            bool on = strength > 0f;
            if (absorbField.gameObject.activeSelf != on) absorbField.gameObject.SetActive(on);
            if (!on) return;

            if (changed) absorbField.gravity = strength * absorbGravity;
            absorbField.transform.position = new Vector3(worldCenter.x, worldCenter.y, _particleZ);
        }

        /// <summary>
        /// IWallBaseTheme: replaces the calm (crimson 0) colors. The crimson set is untouched, so awakening and combat
        /// look the same in every weather. Call on weather change only.
        /// </summary>
        public void SetBaseColors(in WeatherWallColors colors)
        {
            EnsureSetup();
            _baseTop = colors.backgroundTop;
            _baseBottom = colors.backgroundBottom;
            _baseGlow = colors.glow;
            _baseLow = colors.particleLow;
            _baseHigh = colors.particleHigh;
            _ambientColor = colors.ambient;
            ApplyTheme();
            if (_weatherOn) ApplyWeatherColor();
        }

        /// <summary>
        /// IWallBaseTheme: weather particles, veil and sun. The default atmosphere (Dot, no sprite) turns the layer
        /// off and leaves the wall exactly as before. Call on weather change only (allocates a gradient).
        /// </summary>
        public void SetAtmosphere(in WeatherAtmosphere atmosphere, Texture2D sprite, int spriteGrid)
        {
            EnsureSetup();
            _atmosphere = atmosphere;
            bool on = weatherMaterial != null && sprite != null && atmosphere.shape != WeatherParticleShape.Dot &&
                      atmosphere.density > 0f;
            _veil = on ? Mathf.Clamp01(atmosphere.veil) : 0f;
            ApplyTheme();

            if (!on)
            {
                _weatherOn = false;
                if (_weatherParticles != null)
                {
                    _weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _weatherParticles.gameObject.SetActive(false);
                }
                if (_sun != null) _sun.gameObject.SetActive(false);
                return;
            }

            EnsureWeatherObjects();
            _weatherOn = true;
            _weatherMat.SetTexture(MainTexId, sprite);
            ConfigureWeather(atmosphere, Mathf.Max(1, spriteGrid));
            _sun.gameObject.SetActive(atmosphere.topLight > 0f);
            _weatherFade = -1f; // force a fade write
            UpdateWeatherFade();
        }

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
            if (_weatherMat != null) Destroy(_weatherMat);
            if (_sunMat != null) Destroy(_sunMat);
            if (_sunTex != null) Destroy(_sunTex);
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
            _baseTop = backgroundTop;
            _baseBottom = backgroundBottom;
            _baseGlow = glowColor;
            _baseLow = particleColorLow;
            _baseHigh = particleColorHigh;
            _particleLow = particleColorLow;
            _particleHigh = particleColorHigh;
            if (ambientParticles != null) _ambientColor = ambientParticles.main.startColor.color;
            if (absorbField != null) absorbField.gameObject.SetActive(false);

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
            ApplyTheme();
            _material.SetFloat(RippleSpeedId, rippleSpeed);
            _material.SetFloat(RippleDecayId, rippleDecay);
        }

        private void ApplyTheme()
        {
            _particleLow = Color.Lerp(_baseLow, particleColorLowCrimson, _crimson);
            _particleHigh = Color.Lerp(_baseHigh, particleColorHighCrimson, _crimson);

            if (ambientParticles != null)
            {
                ParticleSystem.MainModule main = ambientParticles.main;
                main.startColor = Color.Lerp(_ambientColor, ambientColorCrimson, _crimson);
            }

            if (_material == null) return;
            // The veil (fog, overcast) is mixed into the calm background, behind every particle, so it never covers
            // the hand reaction; lerping to crimson removes it in combat. Veil 0 keeps the exact original colors.
            Color top = _veil > 0f ? Color.Lerp(_baseTop, VeilColor(), _veil) : _baseTop;
            Color bottom = _veil > 0f ? Color.Lerp(_baseBottom, VeilColor(), _veil) : _baseBottom;
            _material.SetColor(ColorTopId, Color.Lerp(top, backgroundTopCrimson, _crimson));
            _material.SetColor(ColorBottomId, Color.Lerp(bottom, backgroundBottomCrimson, _crimson));
            _material.SetColor(GlowColorId, Color.Lerp(_baseGlow, glowColorCrimson, _crimson));
        }

        private Color VeilColor() => new Color(_ambientColor.r, _ambientColor.g, _ambientColor.b, 1f);

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
            UpdateWeatherFade();
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
                Color c = Color.Lerp(_particleLow, _particleHigh, v);
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

            _wallHeight = height;
            _wallWidth = width;
            if (_weatherOn) ConfigureWeather(_atmosphere, _weatherGrid);
        }

        // ---------------------------------------------------------------- weather layer (IWallBaseTheme)

        /// <summary>
        /// Weather brightness: full at rest, lower while a hand moves (weatherHandDim) and in combat. Written only
        /// when it changes by more than 0.5%, so the per-frame cost is a few float operations.
        /// </summary>
        private void UpdateWeatherFade()
        {
            if (!_weatherOn) return;
            float calm = 1f - _crimson;
            float hand = 1f - weatherHandDim * _activity;
            float fade = Mathf.Lerp(weatherCombatKeep, 1f, calm) * hand;
            if (Mathf.Abs(fade - _weatherFade) < 0.005f) return;
            _weatherFade = fade;
            _weatherMat.SetFloat(FadeId, fade);
            _sunMat.SetFloat(FadeId, calm * hand);
        }

        private void EnsureWeatherObjects()
        {
            if (_weatherParticles != null) return;

            _weatherMat = new Material(weatherMaterial) { name = weatherMaterial.name + " (Instance)" };
            var go = new GameObject("WeatherParticles");
            go.transform.SetParent(transform, false);
            _weatherParticles = go.AddComponent<ParticleSystem>();
            _weatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _weatherParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true; // result slow motion must not slow the rain
            main.maxParticles = WeatherMaxParticles;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            ParticleSystem.ShapeModule shape = _weatherParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _weatherMat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.maxParticleSize = 4f; // fog puffs are larger than the screen-fraction default
            renderer.minParticleSize = 0f;

            // Sun: one soft quad at the upper right, behind the particles.
            _sunTex = MakeSunTexture();
            _sunMat = new Material(weatherMaterial) { name = "WeatherSun (Instance)" };
            _sunMat.SetTexture(MainTexId, _sunTex);
            _sunMat.SetFloat(UseVertexColorId, 0f);
            var sun = new GameObject("WeatherSun");
            sun.transform.SetParent(transform, false);
            sun.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var sunRenderer = sun.AddComponent<MeshRenderer>();
            sunRenderer.sharedMaterial = _sunMat;
            sunRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sunRenderer.receiveShadows = false;
            _sun = sun.transform;
            sun.SetActive(false);
        }

        /// <summary>Maps the atmosphere (units: wall heights per second) onto the weather particle system.</summary>
        private void ConfigureWeather(in WeatherAtmosphere a, int grid)
        {
            if (_weatherParticles == null || _wallHeight <= 0f) return;
            _weatherGrid = grid;
            float h = _wallHeight;
            bool puff = IsPuff(a.shape);

            int count = Mathf.Clamp(Mathf.RoundToInt(weatherBaseCount * a.density), 1, WeatherMaxParticles);
            float fall = Mathf.Abs(a.velocity.y);
            // Falling particles live about as long as it takes to cross the spawn box, so most of them are on screen.
            float life = fall > 0.05f ? Mathf.Clamp(1.3f / fall, 0.5f, 12f) : 9f;

            ParticleSystem.MainModule main = _weatherParticles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            float size = h * 0.008f * a.size;
            if (a.shape == WeatherParticleShape.Streak && grid > 1) size *= a.stretch; // a sheet cell holds a long streak
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startRotation = a.shape == WeatherParticleShape.Streak
                ? new ParticleSystem.MinMaxCurve(Mathf.Atan2(a.velocity.x, -a.velocity.y))
                : new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ApplyWeatherColor();

            ParticleSystem.EmissionModule emission = _weatherParticles.emission;
            emission.rateOverTime = count / life;

            ParticleSystem.ShapeModule shape = _weatherParticles.shape;
            shape.scale = new Vector3(_wallWidth * 1.3f, h * 1.3f, 0.01f);
            Vector2 c = NormalizedToWorld(0.5f, 0.5f);
            // Behind the motion particles (they sit at _particleZ).
            _weatherParticles.transform.position = new Vector3(c.x, c.y, _particleZ + 0.2f);

            ParticleSystem.VelocityOverLifetimeModule vel = _weatherParticles.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            float j = Mathf.Clamp01(a.speedJitter);
            vel.x = new ParticleSystem.MinMaxCurve(a.velocity.x * h * (1f - j), a.velocity.x * h * (1f + j));
            vel.y = new ParticleSystem.MinMaxCurve(a.velocity.y * h * (1f - j), a.velocity.y * h * (1f + j));
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.NoiseModule noise = _weatherParticles.noise;
            noise.enabled = a.sway > 0f;
            noise.strength = a.sway * h * 2f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.15f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ParticleSystem.RotationOverLifetimeModule rot = _weatherParticles.rotationOverLifetime;
            rot.enabled = a.shape == WeatherParticleShape.Flake;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            ParticleSystem.TextureSheetAnimationModule sheet = _weatherParticles.textureSheetAnimation;
            sheet.enabled = grid > 1;
            if (grid > 1)
            {
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = grid;
                sheet.numTilesY = grid;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, grid * grid - 0.01f);
                sheet.cycleCount = 1;
            }

            ParticleSystem.ColorOverLifetimeModule col = _weatherParticles.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(a.twinkle > 0f ? TwinkleGradient(a.twinkle) : FadeGradient(puff));

            // Sun at the upper right of the wall.
            Vector2 s = NormalizedToWorld(0.82f, 0.97f);
            _sun.position = new Vector3(s.x, s.y, _particleZ + 0.3f);
            if (wallCamera != null) _sun.rotation = wallCamera.transform.rotation;
            float d = h * 1.6f;
            _sun.localScale = new Vector3(d, d, 1f);
            Color sc = sunColor;
            sc.a = Mathf.Clamp01(a.topLight);
            _sunMat.SetColor(TintId, sc);

            _weatherParticles.gameObject.SetActive(true);
            _weatherParticles.Clear(true);
            _weatherParticles.Simulate(life, true, true); // fill the wall at once instead of trickling in
            _weatherParticles.Play(true);
        }

        private static bool IsPuff(WeatherParticleShape shape) =>
            shape == WeatherParticleShape.Puff || shape == WeatherParticleShape.Haze;

        private void ApplyWeatherColor()
        {
            if (_weatherParticles == null) return;
            float mul = IsPuff(_atmosphere.shape) ? 0.48f : _atmosphere.twinkle > 0f ? 2.6f : 1.6f;
            Color c = _ambientColor;
            c.a = Mathf.Clamp01(_ambientColor.a * mul) * weatherAlpha;
            ParticleSystem.MainModule main = _weatherParticles.main;
            main.startColor = c;
        }

        private static Gradient FadeGradient(bool slow)
        {
            float edge = slow ? 0.25f : 0.08f;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, edge),
                    new GradientAlphaKey(1f, 1f - edge), new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        /// <summary>Several bright pulses over a lifetime; random lifetimes keep the particles out of phase.</summary>
        private static Gradient TwinkleGradient(float amount)
        {
            float low = Mathf.Lerp(1f, 0.1f, Mathf.Clamp01(amount));
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(low, 0.26f),
                    new GradientAlphaKey(1f, 0.42f), new GradientAlphaKey(low, 0.58f), new GradientAlphaKey(1f, 0.74f),
                    new GradientAlphaKey(low, 0.88f), new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        /// <summary>Soft core plus wide halo, alpha only (tinted by _Tint).</summary>
        private static Texture2D MakeSunTexture()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.Alpha8, false)
            {
                wrapMode = TextureWrapMode.Clamp, name = "WeatherSunGlow"
            };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float halo = Mathf.Clamp01(1f - r);
                float core = Mathf.Clamp01(1f - r * 6f);
                float a = Mathf.Clamp01(halo * halo * 0.55f + core * core);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
