using System;
using NUnit.Framework;
using VisionToWall.Core;

namespace VisionToWall.Weather.Tests
{
    public sealed class KmaProtocolTests
    {
        // Fake key for tests only; not a real credential.
        private const string FakeKey = "TESTKEY+abc/def==0123456789";

        private const string NcstOk =
            "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"NORMAL_SERVICE\"},\"body\":{\"dataType\":\"JSON\"," +
            "\"items\":{\"item\":[" +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1400\",\"category\":\"PTY\",\"nx\":60,\"ny\":127,\"obsrValue\":\"1\"}," +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1400\",\"category\":\"T1H\",\"nx\":60,\"ny\":127,\"obsrValue\":\"12.3\"}" +
            "]},\"pageNo\":1,\"numOfRows\":10,\"totalCount\":2}}}";

        private const string FcstOk =
            "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"NORMAL_SERVICE\"},\"body\":{\"dataType\":\"JSON\"," +
            "\"items\":{\"item\":[" +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1330\",\"category\":\"SKY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1500\",\"fcstValue\":\"1\",\"nx\":60,\"ny\":127}," +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1330\",\"category\":\"SKY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1400\",\"fcstValue\":\"4\",\"nx\":60,\"ny\":127}," +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1330\",\"category\":\"PTY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1400\",\"fcstValue\":\"0\",\"nx\":60,\"ny\":127}," +
            "{\"baseDate\":\"20261108\",\"baseTime\":\"1330\",\"category\":\"PTY\",\"fcstDate\":\"20261108\",\"fcstTime\":\"1500\",\"fcstValue\":\"3\",\"nx\":60,\"ny\":127}" +
            "]},\"pageNo\":1,\"numOfRows\":60,\"totalCount\":4}}}";

        private const string KeyErrorXml =
            "<OpenAPI_ServiceResponse><cmmMsgHeader><errMsg>SERVICE ERROR</errMsg>" +
            "<returnAuthMsg>SERVICE_KEY_IS_NOT_REGISTERED_ERROR</returnAuthMsg>" +
            "<returnReasonCode>30</returnReasonCode></cmmMsgHeader></OpenAPI_ServiceResponse>";

        // ---- base time ----

        [Test]
        public void ToKst_AddsNineHours()
        {
            var utc = new DateTime(2026, 11, 8, 20, 30, 0, DateTimeKind.Utc);
            Assert.AreEqual(new DateTime(2026, 11, 9, 5, 30, 0), KmaRequest.ToKst(utc));
        }

        [TestCase(14, 20, 14, 0)]
        [TestCase(14, 10, 13, 0)]
        [TestCase(0, 5, 23, 0)] // previous day
        public void NcstBase_UsesHourAfterFifteenMinutes(int h, int m, int eh, int em)
        {
            DateTime b = KmaRequest.NcstBase(new DateTime(2026, 11, 8, h, m, 0));
            Assert.AreEqual(eh, b.Hour);
            Assert.AreEqual(em, b.Minute);
            if (h == 0) Assert.AreEqual(7, b.Day);
        }

        [TestCase(14, 50, 14, 30)]
        [TestCase(14, 40, 13, 30)]
        [TestCase(0, 20, 23, 30)]
        public void FcstBase_UsesHalfHourAfterFortyFiveMinutes(int h, int m, int eh, int em)
        {
            DateTime b = KmaRequest.FcstBase(new DateTime(2026, 11, 8, h, m, 0));
            Assert.AreEqual(eh, b.Hour);
            Assert.AreEqual(em, b.Minute);
        }

        // ---- URL / redaction ----

        [Test]
        public void BuildUrl_EscapesKeyAndHasParameters()
        {
            string url = KmaRequest.BuildUrl(KmaEndpoint.UltraSrtNcst, FakeKey, new DateTime(2026, 11, 8, 14, 0, 0), 60, 127);
            StringAssert.StartsWith(KmaRequest.BaseUrl + "getUltraSrtNcst?serviceKey=", url);
            StringAssert.Contains(Uri.EscapeDataString(FakeKey), url);
            StringAssert.DoesNotContain("+abc/", url);
            StringAssert.Contains("&base_date=20261108&base_time=1400&nx=60&ny=127", url);
            StringAssert.Contains("dataType=JSON", url);
        }

        [Test]
        public void Redact_RemovesKey()
        {
            string url = KmaRequest.BuildUrl(KmaEndpoint.UltraSrtFcst, FakeKey, new DateTime(2026, 11, 8, 13, 30, 0), 60, 127);
            string red = KmaRequest.Redact(url);
            StringAssert.DoesNotContain(Uri.EscapeDataString(FakeKey), red);
            StringAssert.DoesNotContain(FakeKey, red);
            StringAssert.Contains("serviceKey=***&", red);
            StringAssert.Contains("base_time=1330", red);
        }

        [Test]
        public void BuildUrl_WithoutKey_IsRedacted()
        {
            string url = KmaRequest.BuildUrl(KmaEndpoint.UltraSrtFcst, null, new DateTime(2026, 11, 8, 13, 30, 0), 60, 127);
            StringAssert.Contains("serviceKey=***&", url);
        }

        // ---- parser ----

        [Test]
        public void Parse_Ncst_ReadsPty()
        {
            KmaParseResult r = KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, NcstOk);
            Assert.AreEqual(WeatherFetchStatus.Ok, r.Status);
            Assert.AreEqual(1, r.Pty);
        }

        [Test]
        public void Parse_Fcst_UsesEarliestHour()
        {
            KmaParseResult r = KmaResponseParser.Parse(KmaEndpoint.UltraSrtFcst, FcstOk);
            Assert.AreEqual(WeatherFetchStatus.Ok, r.Status);
            Assert.AreEqual(4, r.Sky);
            Assert.AreEqual(0, r.Pty);
        }

        [Test]
        public void Parse_MissingCategory_IsMalformed()
        {
            string body = NcstOk.Replace("\"PTY\"", "\"REH\"");
            Assert.AreEqual(WeatherFetchStatus.Malformed, KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, body).Status);
            string fcst = FcstOk.Replace("\"SKY\"", "\"LGT\"");
            Assert.AreEqual(WeatherFetchStatus.Malformed, KmaResponseParser.Parse(KmaEndpoint.UltraSrtFcst, fcst).Status);
        }

        [Test]
        public void Parse_NonNumericValue_IsSkipped()
        {
            string body = NcstOk.Replace("\"obsrValue\":\"1\"", "\"obsrValue\":\"강수없음\"");
            Assert.AreEqual(WeatherFetchStatus.Malformed, KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, body).Status);
        }

        [Test]
        public void Parse_NoData()
        {
            const string body = "{\"response\":{\"header\":{\"resultCode\":\"03\",\"resultMsg\":\"NO_DATA\"}}}";
            KmaParseResult r = KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, body);
            Assert.AreEqual(WeatherFetchStatus.NoData, r.Status);
            Assert.AreEqual("03", r.ResultCode);
        }

        [Test]
        public void Parse_EmptyItemsString_IsNoData()
        {
            const string body = "{\"response\":{\"header\":{\"resultCode\":\"00\",\"resultMsg\":\"NORMAL_SERVICE\"},\"body\":{\"items\":\"\"}}}";
            Assert.AreEqual(WeatherFetchStatus.NoData, KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, body).Status);
        }

        [Test]
        public void Parse_KeyErrorXml_IsAuth()
        {
            KmaParseResult r = KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, KeyErrorXml);
            Assert.AreEqual(WeatherFetchStatus.Auth, r.Status);
            Assert.AreEqual("30", r.ResultCode);
            Assert.AreEqual("SERVICE_KEY_IS_NOT_REGISTERED_ERROR", r.Message);
        }

        [TestCase("{\"response\":{\"header\":{\"resultCode\":\"30\",\"resultMsg\":\"SERVICE_KEY_IS_NOT_REGISTERED_ERROR\"}}}", WeatherFetchStatus.Auth)]
        [TestCase("{\"response\":{\"header\":{\"resultCode\":\"22\",\"resultMsg\":\"LIMITED_NUMBER_OF_SERVICE_REQUESTS_EXCEEDS_ERROR\"}}}", WeatherFetchStatus.ServiceError)]
        [TestCase("{\"response\":{\"header\":{\"resultCode\":\"10\",\"resultMsg\":\"INVALID_REQUEST_PARAMETER_ERROR\"}}}", WeatherFetchStatus.ServiceError)]
        [TestCase("", WeatherFetchStatus.Malformed)]
        [TestCase("Unauthorized", WeatherFetchStatus.Malformed)]
        [TestCase("{not json", WeatherFetchStatus.Malformed)]
        [TestCase("{}", WeatherFetchStatus.Malformed)]
        [TestCase("<html><body>gateway</body></html>", WeatherFetchStatus.Malformed)]
        public void Parse_ErrorBodies(string body, WeatherFetchStatus expected)
        {
            Assert.AreEqual(expected, KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, body).Status);
        }

        // ---- HTTP interpretation ----

        [Test]
        public void Interpret_TransportFailures()
        {
            Assert.AreEqual(WeatherFetchStatus.Network, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = false, Error = "Cannot resolve destination host" }).Status);
            Assert.AreEqual(WeatherFetchStatus.Timeout, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = false, TimedOut = true, Error = "Request timeout" }).Status);
            Assert.AreEqual(WeatherFetchStatus.Auth, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = true, Code = 401, Body = "Unauthorized" }).Status);
            Assert.AreEqual(WeatherFetchStatus.ServiceError, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = true, Code = 500, Body = "Internal" }).Status);
            Assert.AreEqual(WeatherFetchStatus.Auth, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = true, Code = 500, Body = KeyErrorXml }).Status);
            Assert.AreEqual(WeatherFetchStatus.Ok, WeatherClient.Interpret(KmaEndpoint.UltraSrtNcst,
                new WeatherHttpResponse { Reached = true, Code = 200, Body = NcstOk }).Status);
        }

        // ---- classification ----

        [TestCase(0, 1, WeatherKind.Clear)]
        [TestCase(0, 3, WeatherKind.Cloudy)]
        [TestCase(0, 4, WeatherKind.Cloudy)]
        [TestCase(-1, 1, WeatherKind.Clear)]
        [TestCase(1, 1, WeatherKind.Rain)]
        [TestCase(2, 1, WeatherKind.Rain)] // rain/snow counts as rain
        [TestCase(4, -1, WeatherKind.Rain)]
        [TestCase(5, 4, WeatherKind.Rain)]
        [TestCase(6, 4, WeatherKind.Rain)]
        [TestCase(3, 1, WeatherKind.Snow)]
        [TestCase(7, -1, WeatherKind.Snow)]
        [TestCase(0, -1, WeatherKind.Unknown)]
        [TestCase(-1, -1, WeatherKind.Unknown)]
        [TestCase(0, 9, WeatherKind.Unknown)]
        public void Classify(int pty, int sky, WeatherKind expected)
        {
            Assert.AreEqual(expected, WeatherClassifier.Classify(pty, sky));
        }

        [TestCase("", true, WeatherKind.Unknown)]
        [TestCase(null, true, WeatherKind.Unknown)]
        [TestCase("clear", true, WeatherKind.Clear)]
        [TestCase(" Cloudy ", true, WeatherKind.Cloudy)]
        [TestCase("rain", true, WeatherKind.Rain)]
        [TestCase("SNOW", true, WeatherKind.Snow)]
        [TestCase("fog", true, WeatherKind.Fog)]
        [TestCase("sunny", false, WeatherKind.Unknown)]
        public void ParseOverride(string text, bool ok, WeatherKind expected)
        {
            Assert.AreEqual(ok, WeatherClassifier.TryParseOverride(text, out WeatherKind kind));
            Assert.AreEqual(expected, kind);
        }

        [Test]
        public void Combine_NowcastRainWinsOverForecastSky()
        {
            KmaParseResult ncst = KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, NcstOk); // PTY 1
            KmaParseResult fcst = KmaResponseParser.Parse(KmaEndpoint.UltraSrtFcst, FcstOk); // SKY 4, PTY 0
            WeatherFetchResult r = WeatherClient.Combine(ncst, fcst, "u?serviceKey=k&a=1", "u?serviceKey=k&b=2",
                new DateTime(2026, 11, 8, 13, 30, 0));
            Assert.AreEqual(WeatherFetchStatus.Ok, r.Status);
            Assert.AreEqual(WeatherKind.Rain, r.Kind);
            Assert.AreEqual("20261108 1330", r.BaseStamp);
            StringAssert.Contains("serviceKey=***", r.RedactedUrl);
        }

        [Test]
        public void Combine_NowcastFailed_UsesForecastPty()
        {
            KmaParseResult ncst = KmaParseResult.Fail(WeatherFetchStatus.Timeout);
            KmaParseResult fcst = KmaResponseParser.Parse(KmaEndpoint.UltraSrtFcst, FcstOk);
            WeatherFetchResult r = WeatherClient.Combine(ncst, fcst, "a", "b", DateTime.Now);
            Assert.AreEqual(WeatherKind.Cloudy, r.Kind); // forecast PTY 0, SKY 4
        }

        [Test]
        public void Combine_ForecastFailedWithoutRain_Fails()
        {
            KmaParseResult ncst = KmaResponseParser.Parse(KmaEndpoint.UltraSrtNcst, NcstOk.Replace("\"obsrValue\":\"1\"", "\"obsrValue\":\"0\""));
            KmaParseResult fcst = KmaParseResult.Fail(WeatherFetchStatus.ServiceError, "22", "LIMIT");
            WeatherFetchResult r = WeatherClient.Combine(ncst, fcst, "a", "b", DateTime.Now);
            Assert.AreEqual(WeatherFetchStatus.ServiceError, r.Status);
            Assert.AreEqual(WeatherKind.Unknown, r.Kind);
        }

        // ---- secrets ----

        [Test]
        public void Secrets_Parse()
        {
            Assert.AreEqual(SecretsStatus.Ok, WeatherSecrets.TryParse("{\"kmaServiceKey\":\" " + FakeKey + " \"}", out string key));
            Assert.AreEqual(FakeKey, key);
            Assert.AreEqual(SecretsStatus.Empty, WeatherSecrets.TryParse("{\"kmaServiceKey\":\"\"}", out _));
            Assert.AreEqual(SecretsStatus.Empty, WeatherSecrets.TryParse("{}", out _));
            Assert.AreEqual(SecretsStatus.Empty, WeatherSecrets.TryParse("", out _));
            Assert.AreEqual(SecretsStatus.Placeholder,
                WeatherSecrets.TryParse("{\"kmaServiceKey\":\"공공데이터포털 인증키를 여기에 붙여넣으세요\"}", out key));
            Assert.IsNull(key);
            Assert.AreEqual(SecretsStatus.Unreadable, WeatherSecrets.TryParse("{oops", out _));
        }

        [Test]
        public void Secrets_MissingFile()
        {
            Assert.AreEqual(SecretsStatus.Missing,
                WeatherSecrets.TryLoadKmaKey("Temp/__no_such_dir__/secrets.json", out string key));
            Assert.IsNull(key);
        }

        [Test]
        public void ExampleTemplate_IsAPlaceholder()
        {
            string path = System.IO.Path.Combine(UnityEngine.Application.streamingAssetsPath, "secrets.example.json");
            Assume.That(System.IO.File.Exists(path));
            Assert.AreEqual(SecretsStatus.Placeholder, WeatherSecrets.TryLoadKmaKey(path, out _));
        }

        // ---- theme ----

        [Test]
        public void Theme_HasAllKinds_AndUnknownMatchesPrefabDefaults()
        {
            foreach (WeatherKind k in Enum.GetValues(typeof(WeatherKind)))
                Assert.AreEqual(k, WeatherTheme.BuiltIn(k).kind, k.ToString());

            WeatherThemeEntry d = WeatherTheme.BuiltIn(WeatherKind.Unknown);
            Assert.AreEqual(new UnityEngine.Color(0.03f, 0.06f, 0.16f), d.wall.backgroundTop);
            Assert.AreEqual(new UnityEngine.Color(0.35f, 0.85f, 1f), d.wall.glow);
            Assert.AreEqual(1f, d.attack.sfxPitch);
            Assert.AreEqual(1f, d.attack.trailTimeScale);
        }

        [Test]
        public void Theme_Asset_FallsBackToUnknown()
        {
            var theme = UnityEngine.ScriptableObject.CreateInstance<WeatherTheme>();
            try
            {
                Assert.AreEqual(6, theme.Count);
                Assert.AreEqual("비", theme.Get(WeatherKind.Rain).label);
                Assert.AreEqual(WeatherKind.Fog, theme.Get(WeatherKind.Fog).kind);
                Assert.AreEqual(WeatherKind.Unknown, theme.Get((WeatherKind)99).kind);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void AttackBolt_StaysRed()
        {
            foreach (WeatherThemeEntry e in WeatherTheme.CreateDefaults())
            {
                UnityEngine.Color c = e.attack.trailColor;
                Assert.Greater(c.r, c.g * 3f, e.label);
                Assert.Greater(c.r, c.b * 3f, e.label);
            }
        }
    }
}
