using System;

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
