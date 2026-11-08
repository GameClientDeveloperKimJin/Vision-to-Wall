using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VisionToWall.Core;

namespace VisionToWall.Weather.Dev
{
    /// <summary>
    /// Dev scene for R5. Runs a real <see cref="WeatherService"/> (live KMA query when the key works) and draws a
    /// stand-in media wall and boss bolt from <see cref="WeatherTheme"/>, because the real wall and projectile take
    /// theme colors only after the Core interface change (see doc 55, interface request).
    /// Keys: 0 = live weather, 1..5 = clear / cloudy / rain / snow / fog override, R = query now,
    /// C = toggle combat crimson (checks that weather only changes the calm base), M = mute, O = overlay.
    /// IMGUI allocates every frame; turn the overlay off (O) for GC checks of the service.
    /// </summary>
    public sealed class WeatherDevBootstrap : MonoBehaviour
    {
        [SerializeField] private WeatherTheme theme;
        [SerializeField, Tooltip("Turn live weather on in memory without touching config.json.")]
        private bool forceLive = true;
        [SerializeField] private bool showOverlay = true;
        [SerializeField, Min(0.2f)] private float shotIntervalSec = 1.4f;

        // Matches MediaWallController's crimson set (combat colors are not weather dependent).
        private static readonly Color CrimsonTop = new Color(0.17f, 0.015f, 0.05f);
        private static readonly Color CrimsonBottom = new Color(0.03f, 0f, 0.012f);
        private static readonly Color CrimsonGlow = new Color(1f, 0.12f, 0.32f);
        private static readonly Color CrimsonLow = new Color(0.9f, 0.05f, 0.25f);
        private static readonly Color CrimsonHigh = new Color(1f, 0.25f, 0.5f);
        private static readonly Color CrimsonAmbient = new Color(1f, 0.15f, 0.35f, 1f);

        private const int Bands = 48;
        private const int Dots = 160;
        private const int AmbientDots = 70;

        private WeatherService _service;
        private WeatherThemeEntry _entry;
        private AudioSource _audio;
        private Vector3[] _dots;   // x, y, value
        private Vector3[] _ambient; // x, y, speed
        private float _crimson;
        private float _crimsonTarget;
        private float _lastShot;
        private bool _mute;
        private GUIStyle _style;

        private void Awake()
        {
            AppConfig config = ConfigService.Load();
            if (forceLive) config.weatherEnabled = true; // in memory only

            _service = gameObject.AddComponent<WeatherService>();
            _service.Changed += OnWeatherChanged;
            _service.Initialize(config);
            OnWeatherChanged(_service.Current);

            var rng = new System.Random(7);
            _dots = new Vector3[Dots];
            for (int i = 0; i < Dots; i++)
                _dots[i] = new Vector3((float)rng.NextDouble(), 0.25f + 0.5f * (float)rng.NextDouble(), (float)rng.NextDouble());
            _ambient = new Vector3[AmbientDots];
            for (int i = 0; i < AmbientDots; i++)
                _ambient[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 0.01f + 0.03f * (float)rng.NextDouble());

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.clip = MakeZap();
        }

        private void OnDestroy()
        {
            if (_service != null) _service.Changed -= OnWeatherChanged;
        }

        private void OnWeatherChanged(WeatherKind kind)
        {
            _entry = theme != null ? theme.Get(kind) : WeatherTheme.BuiltIn(kind);
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
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _crimson = Mathf.MoveTowards(_crimson, _crimsonTarget, dt / 2f);

            for (int i = 0; i < _ambient.Length; i++)
            {
                Vector3 a = _ambient[i];
                a.y += a.z * dt;
                if (a.y > 1f) a.y -= 1f;
                _ambient[i] = a;
            }

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

        private void OnGUI()
        {
            if (_entry == null) return;
            float w = Screen.width, h = Screen.height;
            Texture tex = Texture2D.whiteTexture;
            WeatherWallColors c = _entry.wall;

            // Background gradient (top -> bottom), lerped toward combat crimson like the real wall.
            Color top = Color.Lerp(c.backgroundTop, CrimsonTop, _crimson);
            Color bottom = Color.Lerp(c.backgroundBottom, CrimsonBottom, _crimson);
            float bandH = h / Bands;
            for (int i = 0; i < Bands; i++)
            {
                GUI.color = Color.Lerp(top, bottom, i / (Bands - 1f));
                GUI.DrawTexture(new Rect(0, i * bandH, w, bandH + 1), tex);
            }

            // Ambient particles.
            Color amb = Color.Lerp(c.ambient, CrimsonAmbient, _crimson);
            for (int i = 0; i < _ambient.Length; i++)
            {
                GUI.color = amb;
                Vector3 a = _ambient[i];
                GUI.DrawTexture(new Rect(a.x * w, (1f - a.y) * h, 3, 3), tex);
            }

            // Glow band and motion particles (the wall's "person" silhouette area).
            Color glow = Color.Lerp(c.glow, CrimsonGlow, _crimson);
            glow.a = 0.18f;
            GUI.color = glow;
            GUI.DrawTexture(new Rect(0, h * 0.42f, w, h * 0.16f), tex);
            Color low = Color.Lerp(c.particleLow, CrimsonLow, _crimson);
            Color high = Color.Lerp(c.particleHigh, CrimsonHigh, _crimson);
            for (int i = 0; i < _dots.Length; i++)
            {
                Vector3 d = _dots[i];
                Color pc = Color.Lerp(low, high, d.z);
                pc.a = 1f;
                GUI.color = pc;
                GUI.DrawTexture(new Rect(d.x * w, d.y * h, 5, 5), tex);
            }

            DrawBolt(w, h, tex);
            GUI.color = Color.white;
            if (showOverlay) DrawOverlay();
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
            head.a = 0.35f;
            GUI.color = head;
            GUI.DrawTexture(new Rect(x - size, y - size, size * 2, size * 2), tex);
            head.a = 1f;
            GUI.color = head;
            GUI.DrawTexture(new Rect(x - size * 0.35f, y - size * 0.35f, size * 0.7f, size * 0.7f), tex);
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
                $"Bolt: trail x{_entry.attack.trailTimeScale:0.0} width x{_entry.attack.trailWidthScale:0.0} " +
                $"glow x{_entry.attack.glowScale:0.0} pitch {_entry.attack.sfxPitch:0.00}   crimson {_crimson:0.0}\n" +
                "0 live  1 clear  2 cloudy  3 rain  4 snow  5 fog  R query  C combat  M mute  O overlay";
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10, 10, 860, 160), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 16, 850, 150), text, _style);
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
