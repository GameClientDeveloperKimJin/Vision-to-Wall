using System;
using System.Globalization;
using UnityEngine;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>The two KMA short-term services R5 calls.</summary>
    public enum KmaEndpoint
    {
        /// <summary>getUltraSrtNcst: observed values for the last hour (PTY = precipitation type).</summary>
        UltraSrtNcst,
        /// <summary>getUltraSrtFcst: next six hours (SKY = sky state, PTY).</summary>
        UltraSrtFcst
    }

    /// <summary>Outcome of the last weather query. Everything except Ok keeps the previous weather.</summary>
    public enum WeatherFetchStatus
    {
        None = 0,
        Ok,
        Disabled,
        Override,
        NoKey,
        Network,
        Timeout,
        /// <summary>Key not registered yet, expired, wrong key, or HTTP 401/403. Retried after an hour.</summary>
        Auth,
        ServiceError,
        NoData,
        Malformed
    }

    /// <summary>
    /// Builds KMA request URLs (VilageFcstInfoService_2.0). The service key is only ever placed in the URL that goes
    /// to <see cref="IWeatherTransport"/>; anything meant for a log goes through <see cref="Redact"/>.
    /// </summary>
    public static class KmaRequest
    {
        public const string BaseUrl = "https://apis.data.go.kr/1360000/VilageFcstInfoService_2.0/";
        public const string RedactedKey = "***";

        private const string KeyParam = "serviceKey=";
        private static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

        /// <summary>Korea has no daylight saving, so KST is UTC+9 without a time zone lookup.</summary>
        public static DateTime ToKst(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            return new DateTime(utc.Ticks + KstOffset.Ticks, DateTimeKind.Unspecified);
        }

        /// <summary>Nowcast: generated on the hour, served from about HH:10. Uses the previous hour until HH:15.</summary>
        public static DateTime NcstBase(DateTime kstNow)
        {
            DateTime t = kstNow.AddMinutes(-15);
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
        }

        /// <summary>Ultra short forecast: base HH:30, served from about HH:45.</summary>
        public static DateTime FcstBase(DateTime kstNow)
        {
            DateTime t = kstNow.AddMinutes(-45);
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, 30, 0);
        }

        public static DateTime BaseFor(KmaEndpoint endpoint, DateTime kstNow) =>
            endpoint == KmaEndpoint.UltraSrtNcst ? NcstBase(kstNow) : FcstBase(kstNow);

        public static string FormatDate(DateTime kst) => kst.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        public static string FormatTime(DateTime kst) => kst.ToString("HHmm", CultureInfo.InvariantCulture);

        /// <param name="serviceKey">Decoding key; escaped here. Null or empty writes <see cref="RedactedKey"/>.</param>
        public static string BuildUrl(KmaEndpoint endpoint, string serviceKey, DateTime baseKst, int nx, int ny)
        {
            string op = endpoint == KmaEndpoint.UltraSrtNcst ? "getUltraSrtNcst" : "getUltraSrtFcst";
            int rows = endpoint == KmaEndpoint.UltraSrtNcst ? 10 : 60; // 8 categories / 10 categories x 6 hours
            string key = string.IsNullOrEmpty(serviceKey) ? RedactedKey : Uri.EscapeDataString(serviceKey);
            return BaseUrl + op +
                   "?" + KeyParam + key +
                   "&pageNo=1&numOfRows=" + rows.ToString(CultureInfo.InvariantCulture) +
                   "&dataType=JSON" +
                   "&base_date=" + FormatDate(baseKst) +
                   "&base_time=" + FormatTime(baseKst) +
                   "&nx=" + nx.ToString(CultureInfo.InvariantCulture) +
                   "&ny=" + ny.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Replaces the serviceKey value with <see cref="RedactedKey"/>. Safe for logs.</summary>
        public static string Redact(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int start = url.IndexOf(KeyParam, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return url;
            start += KeyParam.Length;
            int end = url.IndexOf('&', start);
            return url.Substring(0, start) + RedactedKey + (end < 0 ? "" : url.Substring(end));
        }
    }

    /// <summary>Parsed result of one KMA response. Pty/Sky are -1 when absent.</summary>
    public struct KmaParseResult
    {
        public WeatherFetchStatus Status;
        public int Pty;
        public int Sky;
        /// <summary>KMA resultCode / returnReasonCode (no key material).</summary>
        public string ResultCode;
        /// <summary>KMA resultMsg / returnAuthMsg, trimmed to a short length.</summary>
        public string Message;

        public bool Ok => Status == WeatherFetchStatus.Ok;

        public static KmaParseResult Fail(WeatherFetchStatus status, string code = null, string message = null) =>
            new KmaParseResult { Status = status, Pty = -1, Sky = -1, ResultCode = code, Message = message };
    }

    /// <summary>
    /// Parses KMA JSON responses and the XML error envelope data.go.kr returns for key errors (even with dataType=JSON).
    /// Allocates; called once per query.
    /// </summary>
    public static class KmaResponseParser
    {
        private const int MaxMessageLength = 80;

        [Serializable] private sealed class Root { public Response response; }
        [Serializable] private sealed class Response { public Header header; public Body body; }
        [Serializable] private sealed class Header { public string resultCode; public string resultMsg; }
        [Serializable] private sealed class Body { public Items items; }
        [Serializable] private sealed class Items { public Item[] item; }

        [Serializable]
        private sealed class Item
        {
            public string category;
            public string obsrValue;
            public string fcstDate;
            public string fcstTime;
            public string fcstValue;
        }

        public static KmaParseResult Parse(KmaEndpoint endpoint, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return KmaParseResult.Fail(WeatherFetchStatus.Malformed, null, "empty body");

            string trimmed = body.TrimStart();
            if (trimmed.StartsWith("<", StringComparison.Ordinal)) return ParseXml(trimmed);
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                return KmaParseResult.Fail(WeatherFetchStatus.Malformed, null, Shorten(trimmed));

            Root root;
            try
            {
                root = JsonUtility.FromJson<Root>(trimmed);
            }
            catch (Exception)
            {
                return KmaParseResult.Fail(WeatherFetchStatus.Malformed, null, "invalid json");
            }

            Header header = root?.response?.header;
            if (header == null || string.IsNullOrEmpty(header.resultCode))
                return KmaParseResult.Fail(WeatherFetchStatus.Malformed, null, "no header");

            string code = header.resultCode.Trim();
            string msg = Shorten(header.resultMsg);
            if (code != "00") return KmaParseResult.Fail(StatusForCode(code), code, msg);

            Item[] items = root.response.body?.items?.item;
            if (items == null || items.Length == 0) return KmaParseResult.Fail(WeatherFetchStatus.NoData, code, msg);

            var result = new KmaParseResult
            {
                Status = WeatherFetchStatus.Ok, Pty = -1, Sky = -1, ResultCode = code, Message = msg
            };

            if (endpoint == KmaEndpoint.UltraSrtNcst)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    Item it = items[i];
                    if (it != null && it.category == "PTY" && TryInt(it.obsrValue, out int v)) { result.Pty = v; break; }
                }
                if (result.Pty < 0) return KmaParseResult.Fail(WeatherFetchStatus.Malformed, code, "no PTY");
            }
            else
            {
                result.Sky = Earliest(items, "SKY");
                result.Pty = Earliest(items, "PTY");
                if (result.Sky < 0) return KmaParseResult.Fail(WeatherFetchStatus.Malformed, code, "no SKY");
            }
            return result;
        }

        /// <summary>KMA codes: 00 ok, 03 no data, 20/30/31/32 access or key problems, others are service errors.</summary>
        public static WeatherFetchStatus StatusForCode(string code)
        {
            switch (code)
            {
                case "00": return WeatherFetchStatus.Ok;
                case "03": return WeatherFetchStatus.NoData;
                case "20": // SERVICE_ACCESS_DENIED_ERROR
                case "30": // SERVICE_KEY_IS_NOT_REGISTERED_ERROR
                case "31": // DEADLINE_HAS_EXPIRED_ERROR
                case "32": // UNREGISTERED_IP_ERROR
                    return WeatherFetchStatus.Auth;
                default: return WeatherFetchStatus.ServiceError;
            }
        }

        private static KmaParseResult ParseXml(string xml)
        {
            string reason = Tag(xml, "returnReasonCode");
            string auth = Tag(xml, "returnAuthMsg");
            if (!string.IsNullOrEmpty(reason))
                return KmaParseResult.Fail(StatusForCode(reason.Trim()), reason.Trim(), Shorten(auth ?? Tag(xml, "errMsg")));

            string code = Tag(xml, "resultCode");
            if (!string.IsNullOrEmpty(code) && code.Trim() != "00")
                return KmaParseResult.Fail(StatusForCode(code.Trim()), code.Trim(), Shorten(Tag(xml, "resultMsg")));

            return KmaParseResult.Fail(WeatherFetchStatus.Malformed, code, "unexpected xml");
        }

        private static string Tag(string xml, string name)
        {
            string open = "<" + name + ">";
            int s = xml.IndexOf(open, StringComparison.Ordinal);
            if (s < 0) return null;
            s += open.Length;
            int e = xml.IndexOf("</" + name + ">", s, StringComparison.Ordinal);
            return e < 0 ? null : xml.Substring(s, e - s);
        }

        /// <summary>Value of the category at the earliest fcstDate+fcstTime (the hour closest to now).</summary>
        private static int Earliest(Item[] items, string category)
        {
            string bestStamp = null;
            int best = -1;
            for (int i = 0; i < items.Length; i++)
            {
                Item it = items[i];
                if (it == null || it.category != category || !TryInt(it.fcstValue, out int v)) continue;
                string stamp = (it.fcstDate ?? "") + (it.fcstTime ?? "");
                if (bestStamp == null || string.CompareOrdinal(stamp, bestStamp) < 0)
                {
                    bestStamp = stamp;
                    best = v;
                }
            }
            return best;
        }

        private static bool TryInt(string s, out int v) =>
            int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim().Replace('\n', ' ').Replace('\r', ' ');
            return s.Length <= MaxMessageLength ? s : s.Substring(0, MaxMessageLength);
        }
    }

    /// <summary>Maps KMA codes and the config override text to <see cref="WeatherKind"/>.</summary>
    public static class WeatherClassifier
    {
        /// <param name="pty">Precipitation type, -1 if unknown. 0 none, 1 rain, 2 rain/snow, 3 snow, 4 shower,
        /// 5 drizzle, 6 drizzle/snow flurry, 7 snow flurry.</param>
        /// <param name="sky">Sky state, -1 if unknown. 1 clear, 3 mostly cloudy, 4 overcast (2 is the retired "few clouds").</param>
        public static WeatherKind Classify(int pty, int sky)
        {
            if (pty == 3 || pty == 7) return WeatherKind.Snow;
            if (pty > 0) return WeatherKind.Rain; // 1, 2 (rain/snow counts as rain), 4, 5, 6

            switch (sky)
            {
                case 1:
                case 2: return WeatherKind.Clear;
                case 3:
                case 4: return WeatherKind.Cloudy;
                default: return WeatherKind.Unknown;
            }
        }

        /// <summary>"" or null = live (returns true with Unknown). Unrecognised text returns false.</summary>
        public static bool TryParseOverride(string text, out WeatherKind kind)
        {
            kind = WeatherKind.Unknown;
            if (string.IsNullOrWhiteSpace(text)) return true;
            switch (text.Trim().ToLowerInvariant())
            {
                case "clear": kind = WeatherKind.Clear; return true;
                case "cloudy": kind = WeatherKind.Cloudy; return true;
                case "rain": kind = WeatherKind.Rain; return true;
                case "snow": kind = WeatherKind.Snow; return true;
                case "fog": kind = WeatherKind.Fog; return true;
                default: return false;
            }
        }

        public static string ToOverrideText(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Clear: return "clear";
                case WeatherKind.Cloudy: return "cloudy";
                case WeatherKind.Rain: return "rain";
                case WeatherKind.Snow: return "snow";
                case WeatherKind.Fog: return "fog";
                default: return "";
            }
        }
    }
}
