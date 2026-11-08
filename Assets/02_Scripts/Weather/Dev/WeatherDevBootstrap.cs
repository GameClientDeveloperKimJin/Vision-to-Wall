using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;

namespace VisionToWall.Weather.Dev
{
    /// <summary>
    /// Dev scene for R5. Runs a real <see cref="WeatherService"/> (live KMA query when the key works) and draws a
    /// stand-in media wall and boss bolt from <see cref="WeatherTheme"/>, because the real wall and projectile take
    /// theme values only after the Core interface change (doc 55, interface request).
    /// The ambient layer follows <see cref="WeatherAtmosphere"/>: rain streaks fall, snow drifts, fog veils the wall.
    /// Keys: 0 = live weather, 1..5 = clear / cloudy / rain / snow / fog override, R = query now,
    /// C = toggle combat crimson (checks that weather only changes the calm base), P = hand reaction stand-in at the
    /// mouse (checks that weather does not bury it), M = mute, O = overlay.
    /// IMGUI allocates every frame; turn the overlay off (O) for GC checks of the service.
    /// </summary>
    public sealed class WeatherDevBootstrap : MonoBehaviour
    {
        [SerializeField] private WeatherTheme theme;
        [SerializeField, Tooltip("Turn live weather on in memory without touching config.json.")]
        private bool forceLive = true;
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(0.2f)] private float shotIntervalSec = 1.4f;
        [SerializeField, Range(10, 200)] private int baseAmbientCount = 90;

        // Matches MediaWallController's crimson set (combat colors are not weather dependent).
        private static readonly Color CrimsonTop = new Color(0.17f, 0.015f, 0.05f);
        private static readonly Color CrimsonBottom = new Color(0.03f, 0f, 0.012f);
        private static readonly Color CrimsonGlow = new Color(1f, 0.12f, 0.32f);
        private static readonly Color CrimsonLow = new Color(0.9f, 0.05f, 0.25f);
        private static readonly Color CrimsonHigh = new Color(1f, 0.25f, 0.5f);
        private static readonly Color CrimsonAmbient = new Color(1f, 0.15f, 0.35f, 1f);
        private static readonly Color SunLight = new Color(1f, 0.82f, 0.5f);

        private const int Bands = 48;
        private const int Dots = 160;
        private const int MaxAmbient = 500;

        private struct Mote
        {
            public float X, Y;     // 0..1, y up
            public float Speed;    // velocity multiplier
            public float Phase;
            public float Size;     // size multiplier
            public int Cell;       // sprite sheet cell
        }

        private WeatherService _service;
        private WeatherThemeEntry _entry;
        private AudioSource _audio;
        private Vector3[] _dots;   // x, y, value
        private readonly Mote[] _motes = new Mote[MaxAmbient];
        private int _moteCount;
        private System.Random _rng = new System.Random(7);
        private Texture2D _fallbackDot, _fallbackStreak, _fallbackFlake, _fallbackSparkle;
        private float _crimson;
        private float _crimsonTarget;
        private float _lastShot;
        private bool _mute;
        private bool _showHand;
        private GUIStyle _style;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();
            if (forceLive) config.weatherEnabled = true; // in memory only

            _fallbackDot = MakeTexture(RadialSoft);
            _fallbackStreak = MakeTexture(StreakSoft);
            _fallbackFlake = MakeTexture(FlakeStar);
            _fallbackSparkle = MakeTexture(FourPoint);

            _dots = new Vector3[Dots];
            for (int i = 0; i < Dots; i++)
            {
                // Offsets inside a unit disk, denser toward the center.
                float ang = Rand() * Mathf.PI * 2f, r = Rand() * Rand();
                _dots[i] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, Rand());
            }

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.clip = MakeZap();

            _service = gameObject.AddComponent<WeatherService>();
            _service.Changed += OnWeatherChanged;
            _service.Initialize(config);
            OnWeatherChanged(_service.Current);
        }

        private void OnDestroy()
        {
            if (_service != null) _service.Changed -= OnWeatherChanged;
            Destroy(_fallbackDot);
            Destroy(_fallbackStreak);
            Destroy(_fallbackFlake);
            Destroy(_fallbackSparkle);
        }

        private float Rand() => (float)_rng.NextDouble();

        private void OnWeatherChanged(WeatherKind kind)
        {
            _entry = theme != null ? theme.Get(kind) : WeatherTheme.BuiltIn(kind);
            WeatherAtmosphere a = _entry.atmosphere;
            _moteCount = Mathf.Clamp(Mathf.RoundToInt(baseAmbientCount * a.density), 0, MaxAmbient);
            int grid = theme != null && theme.GetSprite(a.shape) != null ? theme.GetSpriteGrid(a.shape) : 1;
            for (int i = 0; i < _moteCount; i++)
            {
                _motes[i] = new Mote
                {
                    X = Rand(), Y = Rand(),
                    Speed = 1f + a.speedJitter * (Rand() * 2f - 1f),
                    Phase = Rand() * 100f,
                    Size = 0.6f + 0.8f * Rand(),
                    Cell = _rng.Next(grid * grid)
                };
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit0Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Unknown);
                if (kb.digit1Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Clear);
                if (kb.digit2Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Cloudy);
                if (kb.digit3Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Rain);
                if (kb.digit4Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Snow);
                if (kb.digit5Key.wasPressedThisFrame) _service.SetOverride(WeatherKind.Fog);
                if (kb.rKey.wasPressedThisFrame) _service.RefreshNow();
                if (kb.cKey.wasPressedThisFrame) _crimsonTarget = _crimsonTarget > 0.5f ? 0f : 1f;
                if (kb.mKey.wasPressedThisFrame) _mute = !_mute;
                if (kb.oKey.wasPressedThisFrame) showOverlay = !showOverlay;
                if (kb.pKey.wasPressedThisFrame) _showHand = !_showHand;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _crimson = Mathf.MoveTowards(_crimson, _crimsonTarget, dt / 2f);
            StepMotes(dt);

            if (Time.unscaledTime - _lastShot >= shotIntervalSec)
            {
                _lastShot = Time.unscaledTime;
                if (!_mute && _entry != null)
                {
                    _audio.pitch = _entry.attack.sfxPitch;
                    _audio.Play();
                }
            }
        }

        /// <summary>Velocity is in wall heights per second; x is converted with the aspect ratio.</summary>
        private void StepMotes(float dt)
        {
            if (_entry == null) return;
            WeatherAtmosphere a = _entry.atmosphere;
            float aspect = Screen.height / (float)Mathf.Max(1, Screen.width);
            const float Margin = 0.15f;
            for (int i = 0; i < _moteCount; i++)
            {
                Mote m = _motes[i];
                m.X += a.velocity.x * m.Speed * dt * aspect;
                m.Y += a.velocity.y * m.Speed * dt;
                if (m.Y < -Margin) { m.Y += 1f + 2f * Margin; m.X = Rand(); }
                else if (m.Y > 1f + Margin) { m.Y -= 1f + 2f * Margin; m.X = Rand(); }
                if (m.X < -Margin) m.X += 1f + 2f * Margin;
                else if (m.X > 1f + Margin) m.X -= 1f + 2f * Margin;
                _motes[i] = m;
            }
        }

        private void OnGUI()
        {
            if (_entry == null) return;
            float w = Screen.width, h = Screen.height;
            Texture tex = Texture2D.whiteTexture;
            WeatherWallColors c = _entry.wall;
            WeatherAtmosphere atmo = _entry.atmosphere;
            float calm = 1f - _crimson; // weather layers fade out in combat; crimson is not weather dependent

            // Background gradient (top -> bottom), lerped toward combat crimson like the real wall.
            Color top = Color.Lerp(c.backgroundTop, CrimsonTop, _crimson);
            Color bottom = Color.Lerp(c.backgroundBottom, CrimsonBottom, _crimson);
            float bandH = h / Bands;
            for (int i = 0; i < Bands; i++)
            {
                float k = i / (Bands - 1f);
                GUI.color = Color.Lerp(top, bottom, k);
                GUI.DrawTexture(new Rect(0, i * bandH, w, bandH + 1), tex);
            }

            // Sunlight: a large warm glow from the upper right corner (clear sky).
            if (atmo.topLight > 0f)
            {
                Color sun = SunLight;
                sun.a = atmo.topLight * calm;
                float r = h * 0.8f;
                GUI.color = sun;
                GUI.DrawTexture(new Rect(w * 0.82f - r, -r * 0.9f, r * 2f, r * 2f), _fallbackDot);
                sun.a = Mathf.Min(1f, atmo.topLight * 2f) * calm;
                GUI.color = sun;
                float core = h * 0.16f;
                GUI.DrawTexture(new Rect(w * 0.82f - core, h * 0.02f - core * 0.4f, core * 2f, core * 2f), _fallbackDot);
            }

            // Hand reaction stand-in (P): a cluster of motion particles around the mouse, only to check that the
            // weather layer does not bury it. The real wall shows these only where a person moves.
            if (_showHand) DrawHand(c, h);

            // Veil (fog, overcast) over the wall.
            if (atmo.veil > 0f)
            {
                Color veil = c.ambient;
                veil.a = atmo.veil * calm;
                GUI.color = veil;
                GUI.DrawTexture(new Rect(0, 0, w, h), tex);
            }

            DrawAtmosphere(w, h, atmo, Color.Lerp(c.ambient, CrimsonAmbient, _crimson));
            DrawBolt(w, h, tex);
            GUI.color = Color.white;
            if (showOverlay) DrawOverlay();
        }

        private void DrawAtmosphere(float w, float h, WeatherAtmosphere a, Color color)
        {
            Texture2D sprite = theme != null ? theme.GetSprite(a.shape) : null;
            int grid = sprite != null ? theme.GetSpriteGrid(a.shape) : 1;
            if (sprite == null) sprite = Fallback(a.shape);

            float baseSize = h * 0.008f * a.size;
            float t = Time.unscaledTime;
            bool puff = a.shape == WeatherParticleShape.Puff || a.shape == WeatherParticleShape.Haze;
            // Rotate streaks to the fall direction (screen y points down).
            float angle = Mathf.Atan2(-a.velocity.x, -a.velocity.y) * Mathf.Rad2Deg;
            float cell = 1f / grid;
            Matrix4x4 saved = GUI.matrix;

            for (int i = 0; i < _moteCount; i++)
            {
                Mote m = _motes[i];
                float sway = a.sway * Mathf.Sin(t * 1.3f + m.Phase) * h;
                float px = m.X * w + sway;
                float py = (1f - m.Y) * h;
                float size = baseSize * m.Size;
                float rw = size, rh = size * a.stretch;
                // A streak sheet cell already holds a long thin streak: draw it square at the full length.
                if (a.shape == WeatherParticleShape.Streak && grid > 1) rw = rh;

                float alpha = color.a * (puff ? 0.3f : 1f);
                if (a.twinkle > 0f)
                {
                    float s = Mathf.Max(0f, Mathf.Sin(t * 3f + m.Phase * 7f));
                    alpha *= Mathf.Lerp(1f, 0.1f + 0.9f * s * s * s * s, a.twinkle);
                }
                Color col = color;
                col.a = Mathf.Clamp01(alpha * (a.twinkle > 0f ? 2.6f : 1.6f));
                GUI.color = col;

                var texCoords = new Rect((m.Cell % grid) * cell, (m.Cell / grid) * cell, cell, cell);
                var rect = new Rect(px - rw * 0.5f, py - rh * 0.5f, rw, rh);
                if (a.shape == WeatherParticleShape.Streak)
                {
                    GUIUtility.RotateAroundPivot(angle, new Vector2(px, py));
                    GUI.DrawTextureWithTexCoords(rect, sprite, texCoords);
                    GUI.matrix = saved;
                }
                else if (a.shape == WeatherParticleShape.Flake)
                {
                    GUIUtility.RotateAroundPivot((t * 20f + m.Phase * 3.6f) % 360f, new Vector2(px, py));
                    GUI.DrawTextureWithTexCoords(rect, sprite, texCoords);
                    GUI.matrix = saved;
                }
                else
                {
                    GUI.DrawTextureWithTexCoords(rect, sprite, texCoords);
                }
            }
        }

        private void DrawHand(WeatherWallColors c, float h)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            float cx = mp.x, cy = h - mp.y;
            float radius = h * 0.12f;

            Color glow = Color.Lerp(c.glow, CrimsonGlow, _crimson);
            glow.a = 0.35f;
            GUI.color = glow;
            GUI.DrawTexture(new Rect(cx - radius * 1.6f, cy - radius * 1.6f, radius * 3.2f, radius * 3.2f), _fallbackDot);

            Color low = Color.Lerp(c.particleLow, CrimsonLow, _crimson);
            Color high = Color.Lerp(c.particleHigh, CrimsonHigh, _crimson);
            float t = Time.unscaledTime;
            for (int i = 0; i < _dots.Length; i++)
            {
                Vector3 d = _dots[i];
                float wob = 0.08f * Mathf.Sin(t * 2f + i);
                float px = cx + (d.x + wob) * radius, py = cy + (d.y - wob) * radius;
                Color pc = Color.Lerp(low, high, d.z);
                pc.a = 1f;
                GUI.color = pc;
                float s = 6f + 6f * d.z;
                GUI.DrawTexture(new Rect(px - s * 0.5f, py - s * 0.5f, s, s), _fallbackDot);
            }
        }

        private Texture2D Fallback(WeatherParticleShape shape)
        {
            switch (shape)
            {
                case WeatherParticleShape.Streak: return _fallbackStreak;
                case WeatherParticleShape.Flake: return _fallbackFlake;
                case WeatherParticleShape.Sparkle: return _fallbackSparkle;
                default: return _fallbackDot;
            }
        }

        /// <summary>Bolt flies right to left once per shot interval: glow head plus a fading trail.</summary>
        private void DrawBolt(float w, float h, Texture tex)
        {
            WeatherAttackLook a = _entry.attack;
            float t = (Time.unscaledTime - _lastShot) / shotIntervalSec;
            float speedPx = w * 0.9f; // per interval
            float x = w * 0.95f - t * speedPx;
            float y = h * 0.3f;

            Color trail = Displayable(a.trailColor);
            Color head = Displayable(a.glowColor);
            float trailLen = speedPx * 0.18f * a.trailTimeScale; // prefab trail time 0.18 s at about one wall/s
            float trailW = 6f * a.trailWidthScale;
            const int Seg = 24;
            for (int i = 0; i < Seg; i++)
            {
                float k = i / (float)Seg;
                trail.a = 1f - k;
                GUI.color = trail;
                GUI.DrawTexture(new Rect(x + k * trailLen, y - trailW * 0.5f * (1f - k), trailLen / Seg + 1, trailW * (1f - k) + 1), tex);
            }

            float size = 26f * a.glowScale;
            head.a = 0.6f;
            GUI.color = head;
            GUI.DrawTexture(new Rect(x - size, y - size, size * 2, size * 2), _fallbackDot);
            head.a = 1f;
            GUI.color = head;
            GUI.DrawTexture(new Rect(x - size * 0.3f, y - size * 0.3f, size * 0.6f, size * 0.6f), _fallbackDot);
        }

        /// <summary>HDR -> LDR for IMGUI: scale so the brightest channel is 1.</summary>
        private static Color Displayable(Color hdr)
        {
            float m = Mathf.Max(hdr.r, Mathf.Max(hdr.g, hdr.b));
            return m > 1f ? new Color(hdr.r / m, hdr.g / m, hdr.b / m, 1f) : new Color(hdr.r, hdr.g, hdr.b, 1f);
        }

        private void DrawOverlay()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            WeatherPoller p = _service.Poller;
            string last = p == null || p.LastSuccessUtc == DateTime.MinValue
                ? "-"
                : KmaRequest.ToKst(p.LastSuccessUtc).ToString("HH:mm:ss") + " KST";
            string next = p == null || !p.LiveAvailable
                ? "-"
                : Math.Max(0, p.NextQueryAt - Time.realtimeSinceStartupAsDouble).ToString("0") + " s";

            string text =
                $"<b>Weather: {_entry.label} ({_service.Current})</b>{(_service.IsOverride ? "  [override]" : "  [live]")}\n" +
                $"Live: {p?.LiveKind}  status {p?.LiveStatus}  PTY {p?.Pty} SKY {p?.Sky}{(p != null && p.Busy ? "  (querying)" : "")}\n" +
                $"Last success: {last}   next query: {next}\n" +
                $"Detail: {p?.LastDetail}\n" +
                $"Air: {_entry.atmosphere.shape} x{_moteCount}  Bolt: trail x{_entry.attack.trailTimeScale:0.0} " +
                $"width x{_entry.attack.trailWidthScale:0.0} glow x{_entry.attack.glowScale:0.0} pitch {_entry.attack.sfxPitch:0.00}   crimson {_crimson:0.0}\n" +
                "0 live  1 clear  2 cloudy  3 rain  4 snow  5 fog  R query  C combat  P hand  M mute  O overlay";
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10, 10, 900, 160), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 16, 890, 150), text, _style);
        }

        // ---- procedural fallbacks (used when the theme has no sprite) ----

        private static Texture2D MakeTexture(Func<float, float, float> alpha)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        private static float RadialSoft(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float k = Mathf.Clamp01(1f - r);
            return k * k;
        }

        private static float StreakSoft(float u, float v)
        {
            float across = Mathf.Clamp01(1f - Mathf.Abs(u) * 3f);
            float along = Mathf.Clamp01(1f - Mathf.Abs(v));
            return across * across * along * (0.4f + 0.6f * Mathf.Clamp01((v + 1f) * 0.5f));
        }

        private static float FlakeStar(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 1f) return 0f;
            float ang = Mathf.Atan2(v, u);
            float arms = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 40f);
            return Mathf.Max(arms * (1f - r), Mathf.Clamp01(1f - r * 4f));
        }

        private static float FourPoint(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 1f) return 0f;
            float cross = Mathf.Max(Mathf.Clamp01(1f - Mathf.Abs(u) * 12f), Mathf.Clamp01(1f - Mathf.Abs(v) * 12f));
            return Mathf.Max(cross * (1f - r), RadialSoft(u * 2.5f, v * 2.5f));
        }

        private static AudioClip MakeZap()
        {
            const int rate = 44100;
            const float dur = 0.16f;
            int n = (int)(rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = Mathf.Lerp(1400f, 300f, t / dur);
                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * (1f - t / dur) * 0.35f;
            }
            AudioClip clip = AudioClip.Create("WeatherDevZap", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
