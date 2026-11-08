using System;
using System.Collections.Generic;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Weather.Tests
{
    public sealed class WeatherPollerTests
    {
        private const string FakeKey = "FAKEKEY0123456789abcdef";

        private sealed class FakeTransport : IWeatherTransport
        {
            public readonly List<string> Urls = new List<string>();
            public readonly Queue<WeatherHttpResponse> Responses = new Queue<WeatherHttpResponse>();
            public bool Hold;
            private readonly List<Action<WeatherHttpResponse>> _pending = new List<Action<WeatherHttpResponse>>();

            public void Get(string url, int timeoutSec, Action<WeatherHttpResponse> done)
            {
                Urls.Add(url);
                if (Hold) { _pending.Add(done); return; }
                done(Responses.Count > 0 ? Responses.Dequeue() : new WeatherHttpResponse { Reached = false, Error = "offline" });
            }

            public void Release()
            {
                Hold = false;
                var p = _pending.ToArray();
                _pending.Clear();
                foreach (var d in p) Get("(held)", 0, d);
            }

            public void CancelAll() => _pending.Clear();
        }

        private sealed class FakeLog : IWeatherLog
        {
            public readonly List<string> Infos = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public void Info(string m) => Infos.Add(m);
            public void Warn(string m) => Warnings.Add(m);
            public IEnumerable<string> All { get { foreach (var s in Infos) yield return s; foreach (var s in Warnings) yield return s; } }
        }

        private static WeatherHttpResponse Ok(string body) => new WeatherHttpResponse { Reached = true, Code = 200, Body = body };

        private static string Ncst(int pty) =>
            "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"NORMAL_SERVICE\"},\"body\":{\"items\":{\"item\":[" +
            "{\"category\":\"PTY\",\"obsrValue\":\"" + pty + "\"}]}}}}";

        private static string Fcst(int sky, int pty = 0) =>
            "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"NORMAL_SERVICE\"},\"body\":{\"items\":{\"item\":[" +
            "{\"category\":\"SKY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1500\",\"fcstValue\":\"" + sky + "\"}," +
            "{\"category\":\"PTY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1500\",\"fcstValue\":\"" + pty + "\"}]}}}}";

        private const string AuthXml =
            "<OpenAPI_ServiceResponse><cmmMsgHeader><returnAuthMsg>SERVICE_KEY_IS_NOT_REGISTERED_ERROR</returnAuthMsg>" +
            "<returnReasonCode>30</returnReasonCode></cmmMsgHeader></OpenAPI_ServiceResponse>";

        private double _now;
        private FakeTransport _transport;
        private FakeLog _log;
        private readonly List<WeatherKind> _changes = new List<WeatherKind>();

        [SetUp]
        public void SetUp()
        {
            _now = 100;
            _transport = new FakeTransport();
            _log = new FakeLog();
            _changes.Clear();
        }

        private WeatherPoller Make(bool enabled = true, string key = FakeKey, string overrideText = "",
            SecretsStatus status = SecretsStatus.Ok, int refreshMin = 30)
        {
            var settings = new WeatherSettings
            {
                Enabled = enabled, GridX = 60, GridY = 127, RefreshSec = refreshMin * 60.0,
                OverrideText = overrideText, LocationName = "서울"
            };
            var p = new WeatherPoller(settings, key, key == null && status == SecretsStatus.Ok ? SecretsStatus.Missing : status,
                new WeatherClient(_transport), () => _now,
                () => new DateTime(2026, 11, 8, 6, 0, 0, DateTimeKind.Utc), _log);
            p.Changed += k => _changes.Add(k);
            p.Start();
            return p;
        }

        [Test]
        public void Disabled_NoNetwork_NoWarning_DefaultTheme()
        {
            WeatherPoller p = Make(enabled: false);
            p.Tick();
            Assert.AreEqual(0, _transport.Urls.Count);
            Assert.AreEqual(WeatherKind.Unknown, p.Current);
            Assert.AreEqual(WeatherFetchStatus.Disabled, p.LiveStatus);
            Assert.AreEqual(0, _log.Warnings.Count);
        }

        [Test]
        public void NoKey_OneWarning_NoNetwork()
        {
            WeatherPoller p = Make(key: null, status: SecretsStatus.Missing);
            for (int i = 0; i < 5; i++) { _now += 4000; p.Tick(); }
            Assert.AreEqual(0, _transport.Urls.Count);
            Assert.AreEqual(WeatherFetchStatus.NoKey, p.LiveStatus);
            Assert.AreEqual(WeatherKind.Unknown, p.Current);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("Missing", _log.Warnings[0]);
        }

        [Test]
        public void Success_SetsKind_RaisesChangedOnce_SchedulesRefresh()
        {
            _transport.Responses.Enqueue(Ok(Ncst(0)));
            _transport.Responses.Enqueue(Ok(Fcst(1)));
            WeatherPoller p = Make();
            p.Tick();

            Assert.AreEqual(2, _transport.Urls.Count);
            StringAssert.Contains("getUltraSrtNcst", _transport.Urls[0]);
            StringAssert.Contains("getUltraSrtFcst", _transport.Urls[1]);
            StringAssert.Contains("base_date=20261108&base_time=1400", _transport.Urls[0]); // 15:00 KST
            StringAssert.Contains("base_time=1430", _transport.Urls[1]);
            Assert.AreEqual(WeatherKind.Clear, p.Current);
            Assert.IsFalse(p.IsOverride);
            Assert.AreEqual(WeatherFetchStatus.Ok, p.LiveStatus);
            CollectionAssert.AreEqual(new[] { WeatherKind.Clear }, _changes);
            Assert.AreEqual(0, _log.Warnings.Count);

            _now += 30 * 60 - 1;
            p.Tick();
            Assert.AreEqual(2, _transport.Urls.Count, "not due yet");
            _now += 2;
            _transport.Responses.Enqueue(Ok(Ncst(0)));
            _transport.Responses.Enqueue(Ok(Fcst(1)));
            p.Tick();
            Assert.AreEqual(4, _transport.Urls.Count);
            Assert.AreEqual(1, _changes.Count, "same kind: no extra Changed");
        }

        [Test]
        public void Failure_KeepsLastValue_WarnsOnce_BacksOff()
        {
            _transport.Responses.Enqueue(Ok(Ncst(3)));
            _transport.Responses.Enqueue(Ok(Fcst(4)));
            WeatherPoller p = Make();
            p.Tick();
            Assert.AreEqual(WeatherKind.Snow, p.Current);

            // Offline from here on (empty queue = network error).
            _now += 30 * 60;
            p.Tick();
            Assert.AreEqual(WeatherKind.Snow, p.Current);
            Assert.AreEqual(WeatherFetchStatus.Network, p.LiveStatus);
            Assert.AreEqual(1, _log.Warnings.Count);
            Assert.AreEqual(_now + 60, p.NextQueryAt, 1e-6);

            _now += 60; p.Tick();
            Assert.AreEqual(_now + 120, p.NextQueryAt, 1e-6);
            _now += 120; p.Tick();
            Assert.AreEqual(_now + 240, p.NextQueryAt, 1e-6);
            for (int i = 0; i < 10; i++) { _now = p.NextQueryAt; p.Tick(); }
            Assert.AreEqual(_now + 30 * 60, p.NextQueryAt, 1e-6, "capped at the refresh interval");

            Assert.AreEqual(1, _log.Warnings.Count);
            Assert.AreEqual(WeatherKind.Snow, p.Current);
            CollectionAssert.AreEqual(new[] { WeatherKind.Snow }, _changes);
        }

        [Test]
        public void FirstFailure_StaysUnknown()
        {
            WeatherPoller p = Make();
            p.Tick();
            Assert.AreEqual(WeatherKind.Unknown, p.Current);
            Assert.AreEqual(0, _changes.Count);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("serviceKey=***", _log.Warnings[0]);
        }

        [Test]
        public void AuthError_SkipsForecast_RetriesAfterAnHour_ThenRecovers()
        {
            _transport.Responses.Enqueue(Ok(AuthXml));
            WeatherPoller p = Make();
            p.Tick();
            Assert.AreEqual(1, _transport.Urls.Count, "forecast skipped on key error");
            Assert.AreEqual(WeatherFetchStatus.Auth, p.LiveStatus);
            Assert.AreEqual(_now + WeatherPoller.AuthRetrySec, p.NextQueryAt, 1e-6);
            StringAssert.Contains("SERVICE_KEY_IS_NOT_REGISTERED_ERROR", _log.Warnings[0]);

            _now += WeatherPoller.AuthRetrySec;
            _transport.Responses.Enqueue(Ok(Ncst(1)));
            _transport.Responses.Enqueue(Ok(Fcst(4)));
            p.Tick();
            Assert.AreEqual(WeatherKind.Rain, p.Current);
            Assert.AreEqual(1, _log.Warnings.Count);
            Assert.IsTrue(_log.Infos.Exists(s => s.Contains("recovered")));
        }

        [Test]
        public void Override_FromConfig_NoNetwork()
        {
            WeatherPoller p = Make(overrideText: "fog");
            p.Tick();
            Assert.AreEqual(WeatherKind.Fog, p.Current);
            Assert.IsTrue(p.IsOverride);
            Assert.AreEqual(0, _transport.Urls.Count);
            CollectionAssert.AreEqual(new[] { WeatherKind.Fog }, _changes);
        }

        [Test]
        public void Override_WorksWhenWeatherDisabled()
        {
            WeatherPoller p = Make(enabled: false, overrideText: "snow");
            Assert.AreEqual(WeatherKind.Snow, p.Current);
            Assert.IsTrue(p.IsOverride);
            Assert.AreEqual(0, _log.Warnings.Count);
        }

        [Test]
        public void InvalidOverride_WarnsOnce_GoesLive()
        {
            _transport.Responses.Enqueue(Ok(Ncst(0)));
            _transport.Responses.Enqueue(Ok(Fcst(3)));
            WeatherPoller p = Make(overrideText: "sunny");
            Assert.IsFalse(p.IsOverride);
            p.Tick();
            Assert.AreEqual(WeatherKind.Cloudy, p.Current);
            Assert.AreEqual(1, _log.Warnings.Count);
        }

        [Test]
        public void RuntimeOverride_SwitchesAllKinds_AndClearsToLive()
        {
            _transport.Responses.Enqueue(Ok(Ncst(0)));
            _transport.Responses.Enqueue(Ok(Fcst(1)));
            WeatherPoller p = Make();
            p.Tick();
            Assert.AreEqual(WeatherKind.Clear, p.Current);

            foreach (var k in new[] { WeatherKind.Cloudy, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Fog, WeatherKind.Clear })
            {
                p.SetOverride(k);
                Assert.AreEqual(k, p.Current);
                Assert.IsTrue(p.IsOverride);
            }

            int urls = _transport.Urls.Count;
            _now += 100000;
            p.Tick();
            Assert.AreEqual(urls, _transport.Urls.Count, "no queries while overridden");

            p.SetOverride(WeatherKind.Fog);
            p.SetOverride(WeatherKind.Unknown);
            Assert.IsFalse(p.IsOverride);
            Assert.AreEqual(WeatherKind.Clear, p.Current, "back to last live value");
            CollectionAssert.AreEqual(new[]
            {
                WeatherKind.Clear, WeatherKind.Cloudy, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Fog,
                WeatherKind.Clear, WeatherKind.Fog, WeatherKind.Clear
            }, _changes);
        }

        [Test]
        public void InFlight_DoesNotStartSecondQuery_AndDisposeIgnoresLateResult()
        {
            _transport.Hold = true;
            WeatherPoller p = Make();
            p.Tick();
            Assert.IsTrue(p.Busy);
            _now += 10000;
            p.Tick();
            Assert.AreEqual(1, _transport.Urls.Count);

            p.Dispose();
            _transport.Responses.Enqueue(Ok(Ncst(1)));
            _transport.Responses.Enqueue(Ok(Fcst(1)));
            _transport.Release();
            Assert.AreEqual(WeatherKind.Unknown, p.Current);
            Assert.AreEqual(0, _changes.Count);
        }

        [Test]
        public void Logs_NeverContainKey()
        {
            // Server echoing the key back in an error message must still be scrubbed.
            _transport.Responses.Enqueue(new WeatherHttpResponse { Reached = true, Code = 200, Body = "bad key " + FakeKey });
            _transport.Responses.Enqueue(new WeatherHttpResponse { Reached = true, Code = 200, Body = "bad key " + FakeKey });
            WeatherPoller p = Make();
            p.Tick();
            foreach (string line in _log.All) StringAssert.DoesNotContain(FakeKey, line);
            StringAssert.DoesNotContain(FakeKey, p.LastDetail);
        }
    }
}
