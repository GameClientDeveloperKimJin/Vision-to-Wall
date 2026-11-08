using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>
    /// Calm (non-combat) media wall colors for one weather. Combat crimson is not here: the wall lerps from these
    /// base colors to its own crimson set, so awakening and combat look the same in every weather.
    /// Field names mirror MediaWallController's calm colors.
    /// </summary>
    [Serializable]
    public struct WeatherWallColors
    {
        public Color backgroundTop;
        public Color backgroundBottom;
        public Color glow;
        public Color particleLow;
        public Color particleHigh;
        /// <summary>Ambient particle start color (alpha used as-is).</summary>
        public Color ambient;
    }

    /// <summary>
    /// Boss projectile look and sound for one weather. Presentation only: none of these values may feed hit tests,
    /// speed or aim timing (user decision 2026-11-08). The bolt stays in the red family so attacks still read as danger.
    /// </summary>
    [Serializable]
    public struct WeatherAttackLook
    {
        /// <summary>HDR color for the projectile trail material (default prefab: 4, 0.04, 0.03).</summary>
        [ColorUsage(false, true)] public Color trailColor;
        /// <summary>HDR color for the projectile glow billboard (default prefab: 6, 0.1, 0.06).</summary>
        [ColorUsage(false, true)] public Color glowColor;
        /// <summary>Multiplier on TrailRenderer.time (longer streak = rain).</summary>
        [Range(0.2f, 3f)] public float trailTimeScale;
        /// <summary>Multiplier on TrailRenderer.widthMultiplier.</summary>
        [Range(0.2f, 3f)] public float trailWidthScale;
        /// <summary>Multiplier on the glow billboard's visual scale (not the collider).</summary>
        [Range(0.2f, 3f)] public float glowScale;
        /// <summary>AudioSource pitch for the laser fire cue.</summary>
        [Range(0.5f, 2f)] public float sfxPitch;
    }

    /// <summary>Sprite used by the ambient particles. Dot is the current round glow.</summary>
    public enum WeatherParticleShape
    {
        Dot = 0,
        /// <summary>Long thin streak stretched along the velocity (rain).</summary>
        Streak = 1,
        /// <summary>Crystal flake (snow).</summary>
        Flake = 2,
        /// <summary>Large soft cloud puff (overcast).</summary>
        Puff = 3,
        /// <summary>Twinkling star glint (clear sky).</summary>
        Sparkle = 4,
        /// <summary>Large thin wisp of mist (fog).</summary>
        Haze = 5
    }

    /// <summary>
    /// How the wall's ambient particles move and look for one weather. This is what makes rain read as rain:
    /// colors alone do not. Units are wall heights per second (x right, y up). Maps onto the AmbientParticles
    /// system: velocity -> velocity over lifetime, stretch -> stretched billboard length scale, sway -> noise,
    /// size -> start size, density -> emission rate, veil -> a full-wall overlay alpha.
    /// </summary>
    [Serializable]
    public struct WeatherAtmosphere
    {
        public WeatherParticleShape shape;
        public Vector2 velocity;
        /// <summary>Random spread added to the velocity per particle (0..1 of velocity).</summary>
        [Range(0f, 1f)] public float speedJitter;
        /// <summary>Side to side sway amplitude in wall heights (snow, fog).</summary>
        [Range(0f, 0.2f)] public float sway;
        /// <summary>Multiplier on the current ambient particle size.</summary>
        [Range(0.2f, 30f)] public float size;
        /// <summary>Length along the velocity for streaks (1 = round).</summary>
        [Range(1f, 30f)] public float stretch;
        /// <summary>Multiplier on the current ambient particle count.</summary>
        [Range(0f, 6f)] public float density;
        /// <summary>Full-wall haze in the ambient color (fog, overcast). 0 = none.</summary>
        [Range(0f, 0.6f)] public float veil;
        /// <summary>Brightness flicker per particle, 0..1 (sparkle).</summary>
        [Range(0f, 1f)] public float twinkle;
        /// <summary>Warm glow from the top of the wall (sunlight). 0 = none.</summary>
        [Range(0f, 0.6f)] public float topLight;
    }

    [Serializable]
    public sealed class WeatherThemeEntry
    {
        public WeatherKind kind;
        public string label;
        public WeatherWallColors wall;
        public WeatherAtmosphere atmosphere;
        public WeatherAttackLook attack;
    }

    /// <summary>
    /// Weather palettes for the wall and the boss attack look. Unknown is the default theme and matches the current
    /// MediaWall / Projectile prefab values, so "no weather" changes nothing. Missing entries fall back to Unknown.
    /// </summary>
    [CreateAssetMenu(menuName = "VisionToWall/Weather Theme", fileName = "WeatherTheme")]
    public sealed class WeatherTheme : ScriptableObject
    {
        [SerializeField] private WeatherThemeEntry[] entries = CreateDefaults();

        [Header("Ambient particle sprites (white on black or alpha; colored at runtime). Empty = procedural.")]
        [SerializeField] private Texture2D dotSprite;
        [SerializeField] private Texture2D streakSprite;
        [SerializeField] private Texture2D flakeSprite;
        [SerializeField] private Texture2D puffSprite;
        [SerializeField] private Texture2D sparkleSprite;
        [SerializeField] private Texture2D hazeSprite;
        [SerializeField, Range(1, 4), Tooltip("Cells per side when the streak sprite is a sheet (VARCO: 2x2).")]
        private int streakGrid = 2;
        [SerializeField, Range(1, 4), Tooltip("Cells per side when the flake sprite is a sheet (VARCO: 2x2).")]
        private int flakeGrid = 2;

        public int Count => entries != null ? entries.Length : 0;

        /// <summary>Cells per side of the shape's sprite sheet (1 = single image).</summary>
        public int GetSpriteGrid(WeatherParticleShape shape) =>
            shape == WeatherParticleShape.Streak ? streakGrid :
            shape == WeatherParticleShape.Flake ? flakeGrid : 1;

        /// <summary>Assigned sprite for the shape, or null (caller uses its own fallback).</summary>
        public Texture2D GetSprite(WeatherParticleShape shape)
        {
            switch (shape)
            {
                case WeatherParticleShape.Streak: return streakSprite;
                case WeatherParticleShape.Flake: return flakeSprite;
                case WeatherParticleShape.Puff: return puffSprite;
                case WeatherParticleShape.Sparkle: return sparkleSprite;
                case WeatherParticleShape.Haze: return hazeSprite;
                default: return dotSprite;
            }
        }

        /// <summary>No allocation. Falls back to the asset's Unknown entry, then to the built-in default.</summary>
        public WeatherThemeEntry Get(WeatherKind kind)
        {
            WeatherThemeEntry fallback = null;
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    WeatherThemeEntry e = entries[i];
                    if (e == null) continue;
                    if (e.kind == kind) return e;
                    if (e.kind == WeatherKind.Unknown) fallback = e;
                }
            }
            return fallback ?? BuiltIn(kind);
        }

        private void Reset() => entries = CreateDefaults();

        private static WeatherThemeEntry[] _builtIn;

        /// <summary>Built-in palette used when no asset is assigned. Shared; do not modify the result.</summary>
        public static WeatherThemeEntry BuiltIn(WeatherKind kind)
        {
            _builtIn ??= CreateDefaults();
            for (int i = 0; i < _builtIn.Length; i++)
                if (_builtIn[i].kind == kind) return _builtIn[i];
            return _builtIn[0];
        }

        public static WeatherThemeEntry[] CreateDefaults()
        {
            WeatherThemeEntry[] e = CreatePalettes();
            //                      shape                          velocity          jit   sway   size  str  dens  veil  twk  top
            e[0].atmosphere = Atmo(WeatherParticleShape.Dot,     0f,     0.01f,  1f,   0.01f,  1f,   1f,  1f,   0f,   0f,  0f);    // current wall
            e[1].atmosphere = Atmo(WeatherParticleShape.Sparkle, 0.005f, 0.01f,  0.5f, 0.005f, 9f,   1f,  0.6f, 0f,   1f,  0.45f); // sun glints, warm top light
            e[2].atmosphere = Atmo(WeatherParticleShape.Puff,    0.02f,  0f,     0.4f, 0.01f,  45f,  1f,  0.3f, 0.08f, 0f, 0f);     // slow cloud banks
            e[3].atmosphere = Atmo(WeatherParticleShape.Streak, -0.12f, -1.6f,  0.25f, 0f,    0.7f, 14f, 4f,   0.06f, 0f, 0f);   // slanted fast streaks
            e[4].atmosphere = Atmo(WeatherParticleShape.Flake,   0.01f, -0.08f,  0.5f, 0.025f, 5f,   1f,  1.6f, 0.03f, 0.2f, 0f);  // slow swaying flakes
            e[5].atmosphere = Atmo(WeatherParticleShape.Haze,    0.03f,  0.002f, 0.5f, 0.02f,  70f,  1f,  0.45f, 0.2f, 0f,  0f);    // drifting haze over a veil
            return e;
        }

        private static WeatherAtmosphere Atmo(WeatherParticleShape shape, float vx, float vy, float jitter, float sway,
            float size, float stretch, float density, float veil, float twinkle, float topLight) =>
            new WeatherAtmosphere
            {
                shape = shape, velocity = new Vector2(vx, vy), speedJitter = jitter, sway = sway, size = size,
                stretch = stretch, density = density, veil = veil, twinkle = twinkle, topLight = topLight
            };

        private static WeatherThemeEntry[] CreatePalettes() => new[]
        {
            Entry(WeatherKind.Unknown, "기본",
                new Color(0.03f, 0.06f, 0.16f), new Color(0f, 0f, 0.03f), new Color(0.35f, 0.85f, 1f),
                new Color(0.2f, 0.45f, 1f), new Color(0.75f, 1f, 1f), new Color(0.4f, 0.7f, 1f, 0.35f),
                new Color(4f, 0.04f, 0.03f), new Color(6f, 0.1f, 0.06f), 1f, 1f, 1f, 1f),
            Entry(WeatherKind.Clear, "맑음",
                new Color(0.06f, 0.16f, 0.36f), new Color(0.01f, 0.03f, 0.08f), new Color(0.45f, 0.9f, 1f),
                new Color(0.25f, 0.6f, 1f), new Color(0.9f, 1f, 1f), new Color(1f, 0.85f, 0.5f, 0.35f),
                new Color(4.5f, 0.35f, 0.05f), new Color(6f, 0.6f, 0.1f), 1f, 1f, 1.1f, 1.05f),
            Entry(WeatherKind.Cloudy, "흐림",
                new Color(0.06f, 0.07f, 0.10f), new Color(0.01f, 0.01f, 0.02f), new Color(0.55f, 0.65f, 0.75f),
                new Color(0.35f, 0.42f, 0.55f), new Color(0.8f, 0.85f, 0.9f), new Color(0.6f, 0.65f, 0.7f, 0.3f),
                new Color(3.2f, 0.04f, 0.06f), new Color(4.5f, 0.08f, 0.1f), 1.2f, 1.3f, 1.25f, 0.9f),
            Entry(WeatherKind.Rain, "비",
                new Color(0.01f, 0.05f, 0.12f), new Color(0f, 0.01f, 0.04f), new Color(0.2f, 0.6f, 0.9f),
                new Color(0.1f, 0.35f, 0.8f), new Color(0.5f, 0.85f, 1f), new Color(0.55f, 0.75f, 1f, 0.45f),
                new Color(4f, 0.06f, 0.25f), new Color(5f, 0.1f, 0.35f), 1.8f, 0.6f, 0.85f, 1.15f),
            Entry(WeatherKind.Snow, "눈",
                new Color(0.10f, 0.11f, 0.18f), new Color(0.02f, 0.02f, 0.05f), new Color(0.8f, 0.9f, 1f),
                new Color(0.6f, 0.7f, 1f), new Color(1f, 1f, 1f), new Color(1f, 1f, 1f, 0.55f),
                new Color(4f, 0.2f, 0.35f), new Color(6f, 0.5f, 0.7f), 0.6f, 1.2f, 1.3f, 1.25f),
            Entry(WeatherKind.Fog, "안개",
                new Color(0.08f, 0.10f, 0.11f), new Color(0.04f, 0.05f, 0.055f), new Color(0.5f, 0.7f, 0.7f),
                new Color(0.3f, 0.45f, 0.5f), new Color(0.7f, 0.8f, 0.8f), new Color(0.75f, 0.8f, 0.8f, 0.5f),
                new Color(3f, 0.05f, 0.04f), new Color(4f, 0.08f, 0.06f), 1.4f, 1.5f, 1.6f, 0.85f),
        };

        private static WeatherThemeEntry Entry(WeatherKind kind, string label,
            Color top, Color bottom, Color glow, Color low, Color high, Color ambient,
            Color trail, Color boltGlow, float trailTime, float trailWidth, float glowScale, float pitch) =>
            new WeatherThemeEntry
            {
                kind = kind,
                label = label,
                wall = new WeatherWallColors
                {
                    backgroundTop = top, backgroundBottom = bottom, glow = glow,
                    particleLow = low, particleHigh = high, ambient = ambient
                },
                attack = new WeatherAttackLook
                {
                    trailColor = trail, glowColor = boltGlow, trailTimeScale = trailTime,
                    trailWidthScale = trailWidth, glowScale = glowScale, sfxPitch = pitch
                }
            };
    }
}
