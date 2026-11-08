using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>
    /// <see cref="IWeatherSource"/> for the app. Reads the KMA key from secrets.json, queries on start and every
    /// weatherRefreshMin, keeps the last value on failure and honours weatherOverride. Offline, no key, a key that is
    /// not active yet or a broken response all end in the default theme (Unknown) with one console warning.
    /// Update only compares the clock until a query is due (no per-frame allocation); queries never block the frame.
    /// </summary>
    public sealed class WeatherService : MonoBehaviour, IWeatherSource
    {
        [SerializeField, Min(1), Tooltip("Per request. Two requests per query (nowcast, forecast).")]
        private int timeoutSec = WeatherClient.DefaultTimeoutSec;

        private WeatherPoller _poller;
        private IWeatherTransport _transport;
        private Action<WeatherKind> _forward;

        public WeatherKind Current => _poller != null ? _poller.Current : WeatherKind.Unknown;
        public bool IsOverride => _poller != null && _poller.IsOverride;
        public event Action<WeatherKind> Changed;

        /// <summary>Status for the engineer panel. Null before <see cref="Initialize(AppConfig)"/>.</summary>
        public WeatherPoller Poller => _poller;

        /// <summary>App entry: loads secrets.json and uses UnityWebRequest.</summary>
        public void Initialize(AppConfig config)
        {
            SecretsStatus status = SecretsStatus.Missing;
            string key = null;
            // Read the key only when it would be used.
            if (config.weatherEnabled) status = WeatherSecrets.TryLoadKmaKey(out key);
            Initialize(config, key, status, new UnityWebRequestTransport());
        }

        /// <summary>Tests and dev scenes: inject the key source and transport.</summary>
        public void Initialize(AppConfig config, string serviceKey, SecretsStatus keyStatus, IWeatherTransport transport)
        {
            Shutdown();
            _transport = transport;
            var client = transport != null ? new WeatherClient(transport, timeoutSec) : null;
            _poller = new WeatherPoller(WeatherSettings.FromConfig(config), serviceKey, keyStatus, client,
                () => Time.realtimeSinceStartupAsDouble);
            _forward ??= k => Changed?.Invoke(k);
            _poller.Changed += _forward;
            _poller.Start();
        }

        public void SetOverride(WeatherKind kind) => _poller?.SetOverride(kind);
        public void RefreshNow() => _poller?.RefreshNow();

        private void Update() => _poller?.Tick();

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            _transport?.CancelAll();
            if (_poller != null)
            {
                _poller.Changed -= _forward;
                _poller.Dispose();
            }
            _poller = null;
            _transport = null;
        }
    }
}
