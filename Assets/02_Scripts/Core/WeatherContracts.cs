using System;
using UnityEngine;

namespace VisionToWall.Core
{
    /// <summary>Weather classes used for the wall theme and the boss attack look (v2.2).</summary>
    public enum WeatherKind
    {
        /// <summary>No data yet, weather off, or the last query failed with nothing to keep. Default theme.</summary>
        Unknown = 0,
        Clear = 1,
        Cloudy = 2,
        Rain = 3,
        Snow = 4,
        /// <summary>Not reported by the KMA short-term forecast; reachable through config.weatherOverride.</summary>
        Fog = 5
    }

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

    /// <summary>
    /// Weather -> media wall (Render.MediaWallController). Calm colors and ambient particle look only; the wall keeps
    /// its own awakening/combat crimson, so this never fights IWallPresentation.SetTheme. Call on weather change.
    /// </summary>
    public interface IWallBaseTheme
    {
        void SetBaseColors(in WeatherWallColors colors);

        /// <param name="sprite">Particle sprite, or null for the wall's own.</param>
        /// <param name="spriteGrid">Cells per side when the sprite is a sheet (1 = single image).</param>
        void SetAtmosphere(in WeatherAtmosphere atmosphere, Texture2D sprite, int spriteGrid);
    }

    /// <summary>Weather -> boss attack look (Presentation). Visual and sound only, never hit tests or timing.</summary>
    public interface IAttackLookTarget
    {
        void SetAttackLook(in WeatherAttackLook look);
    }

    /// <summary>Weather -> Render/Game/UI. Implemented by Weather.WeatherService (R5) or a module mock.</summary>
    public interface IWeatherSource
    {
        WeatherKind Current { get; }

        /// <summary>True when Current comes from config.weatherOverride, not the API.</summary>
        bool IsOverride { get; }

        /// <summary>Raised on the main thread when Current changes.</summary>
        event Action<WeatherKind> Changed;
    }
}
