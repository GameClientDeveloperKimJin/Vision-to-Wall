using System;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>Config values the poller needs, copied once (weather fields are restart-required).</summary>
    public struct WeatherSettings
    {
        public bool Enabled;
        public int GridX;
        public int GridY;
        public double RefreshSec;
        public string OverrideText;
        public string LocationName;

        public static WeatherSettings FromConfig(AppConfig c) => new WeatherSettings
        {
            Enabled = c.weatherEnabled,
            GridX = c.weatherGridX,
            GridY = c.weatherGridY,
            RefreshSec = Math.Max(1, c.weatherRefreshMin) * 60.0,
            OverrideText = c.weatherOverride,
            LocationName = c.weatherLocationName
        };
    }

    public interface IWeatherLog
    {
        void Info(string message);
        void Warn(string message);
    }

    public sealed class UnityWeatherLog : IWeatherLog
    {
        public static readonly UnityWeatherLog Instance = new UnityWeatherLog();
        public void Info(string message) => Debug.Log(message);
        public void Warn(string message) => Debug.LogWarning(message);
    }

    /// <summary>
    /// Engine-free core of <see cref="WeatherService"/>: schedule, retry, keep-last-value, override and the single
    /// warning. <see cref="Tick"/> only compares numbers until a query is due, so it does not allocate per frame.
    /// </summary>
    public sealed class WeatherPoller : IWeatherSource
    {
        public const double MinRetrySec = 60;
        /// <summary>Key errors usually mean a fresh key not yet active (can take about an hour).</summary>
        public const double AuthRetrySec = 3600;

        private readonly WeatherSettings _settings;
        private readonly string _key;
        private readonly SecretsStatus _keyStatus;
        private readonly WeatherClient _client;
        private readonly Func<double> _clock;
        private readonly Func<DateTime> _utcNow;
        private readonly IWeatherLog _log;
        private readonly Action<WeatherFetchResult> _onResult;

        private bool _started;
        private bool _disposed;
        private bool _busy;
        private bool _warned;
        private bool _liveAvailable;
        private double _nextAt;
        private int _failures;

        public WeatherPoller(WeatherSettings settings, string serviceKey, SecretsStatus keyStatus, WeatherClient client,
            Func<double> clock, Func<DateTime> utcNow = null, IWeatherLog log = null)
        {
            _settings = settings;
            _key = string.IsNullOrEmpty(serviceKey) ? null : serviceKey;
            _keyStatus = _key == null && keyStatus == SecretsStatus.Ok ? SecretsStatus.Empty : keyStatus;
            _client = client;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _log = log ?? UnityWeatherLog.Instance;
            _onResult = OnResult;
        }

        public WeatherKind Current { get; private set; } = WeatherKind.Unknown;
        public bool IsOverride { get; private set; }
        public event Action<WeatherKind> Changed;

        /// <summary>Latest live weather, kept across failures and while an override is shown.</summary>
        public WeatherKind LiveKind { get; private set; } = WeatherKind.Unknown;
        public WeatherFetchStatus LiveStatus { get; private set; } = WeatherFetchStatus.None;
        public bool LiveAvailable => _liveAvailable;
        public bool Busy => _busy;
        public int Pty { get; private set; } = -1;
        public int Sky { get; private set; } = -1;
        /// <summary>UTC; DateTime.MinValue until the first success.</summary>
        public DateTime LastSuccessUtc { get; private set; } = DateTime.MinValue;
        public DateTime LastAttemptUtc { get; private set; } = DateTime.MinValue;
        /// <summary>Short reason of the last query (no key material).</summary>
        public string LastDetail { get; private set; } = "";
        /// <summary>Clock value of the next query; meaningful only when <see cref="LiveAvailable"/>.</summary>
        public double NextQueryAt => _nextAt;

        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;

            if (!WeatherClassifier.TryParseOverride(_settings.OverrideText, out WeatherKind overrideKind))
                WarnOnce($"[Weather] weatherOverride \"{_settings.OverrideText}\" is not one of clear/cloudy/rain/snow/fog. Ignored.");

            if (!_settings.Enabled)
            {
                LiveStatus = WeatherFetchStatus.Disabled;
                _log.Info("[Weather] Live weather off (weatherEnabled false). Default theme.");
            }
            else if (_key == null)
            {
                LiveStatus = WeatherFetchStatus.NoKey;
                WarnOnce($"[Weather] No KMA key ({WeatherSecrets.FileName}: {_keyStatus}). Default theme, no network.");
            }
            else if (_client == null)
            {
                LiveStatus = WeatherFetchStatus.Disabled;
            }
            else
            {
                _liveAvailable = true;
                _nextAt = _clock();
            }

            if (overrideKind != WeatherKind.Unknown) SetOverride(overrideKind);
        }

        /// <summary>Unknown returns to live weather (or the default theme). No network while an override is set.</summary>
        public void SetOverride(WeatherKind kind)
        {
            if (_disposed) return;
            if (kind == WeatherKind.Unknown)
            {
                if (!IsOverride) return;
                IsOverride = false;
                SetCurrent(LiveKind);
                _log.Info("[Weather] Override cleared. Showing live weather: " + LiveKind);
                return;
            }

            bool wasOverride = IsOverride;
            WeatherKind before = Current;
            IsOverride = true;
            SetCurrent(kind);
            if (!wasOverride || before != kind) _log.Info("[Weather] Override: " + kind);
        }

        /// <summary>Queries now if live weather is available, no override is set and nothing is in flight.</summary>
        public void RefreshNow()
        {
            if (_liveAvailable) _nextAt = _clock();
        }

        public void Tick()
        {
            if (_disposed || !_liveAvailable || _busy || IsOverride) return;
            if (_clock() < _nextAt) return;

            _busy = true;
            LastAttemptUtc = _utcNow();
            _client.Fetch(_key, _settings.GridX, _settings.GridY, LastAttemptUtc, _onResult);
        }

        public void Dispose() => _disposed = true;

        private void OnResult(WeatherFetchResult r)
        {
            if (_disposed) return;
            _busy = false;
            LastDetail = Scrub(r.Detail);
            string url = Scrub(r.RedactedUrl);

            if (r.Ok)
            {
                bool recovered = _failures > 0;
                _failures = 0;
                LiveStatus = WeatherFetchStatus.Ok;
                LiveKind = r.Kind;
                Pty = r.Pty;
                Sky = r.Sky;
                LastSuccessUtc = _utcNow();
                _nextAt = _clock() + _settings.RefreshSec;
                _log.Info($"[Weather] {_settings.LocationName} ({_settings.GridX},{_settings.GridY}): {r.Kind} " +
                          $"({LastDetail}, base {r.BaseStamp} KST){(recovered ? " - recovered" : "")}");
                if (!IsOverride) SetCurrent(r.Kind);
                return;
            }

            _failures++;
            LiveStatus = r.Status;
            double delay = r.Status == WeatherFetchStatus.Auth
                ? AuthRetrySec
                : Math.Min(_settings.RefreshSec, MinRetrySec * Math.Pow(2, Math.Min(_failures - 1, 10)));
            _nextAt = _clock() + delay;

            string message = $"[Weather] Live weather unavailable ({LastDetail}). Keeping {LiveKind}; " +
                             $"retry in {Math.Ceiling(delay / 60.0)} min. Request: {url}";
            if (!_warned) WarnOnce(message);
            else _log.Info(message);
        }

        private void SetCurrent(WeatherKind kind)
        {
            if (kind == Current) return;
            Current = kind;
            Changed?.Invoke(kind);
        }

        private void WarnOnce(string message)
        {
            if (_warned) { _log.Info(message); return; }
            _warned = true;
            _log.Warn(message);
        }

        /// <summary>Belt and braces: no text leaves this class with the key in it.</summary>
        private string Scrub(string text)
        {
            if (string.IsNullOrEmpty(text) || _key == null) return text ?? "";
            text = text.Replace(_key, KmaRequest.RedactedKey);
            string escaped = Uri.EscapeDataString(_key);
            return escaped == _key ? text : text.Replace(escaped, KmaRequest.RedactedKey);
        }
    }
}
