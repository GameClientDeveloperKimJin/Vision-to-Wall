using System;
using System.Collections.Generic;
using UnityEngine.Networking;
using VisionToWall.Core;

namespace VisionToWall.Weather
{
    /// <summary>Raw HTTP outcome handed back by <see cref="IWeatherTransport"/>.</summary>
    public struct WeatherHttpResponse
    {
        /// <summary>False for DNS, connection or TLS failures and timeouts (no HTTP status).</summary>
        public bool Reached;
        public bool TimedOut;
        public long Code;
        public string Body;
        /// <summary>Transport error text. Never contains the URL.</summary>
        public string Error;
    }

    /// <summary>HTTP GET seam so the client and service can be tested without a network.</summary>
    public interface IWeatherTransport
    {
        /// <summary>Starts a GET. <paramref name="done"/> must be invoked exactly once on the main thread, unless cancelled.</summary>
        void Get(string url, int timeoutSec, Action<WeatherHttpResponse> done);

        /// <summary>Aborts requests in flight; their callbacks are not invoked.</summary>
        void CancelAll();
    }

    /// <summary>
    /// <see cref="UnityWebRequest"/> transport. Uses the async operation's completed callback, so no coroutine or
    /// MonoBehaviour is needed and nothing runs per frame.
    /// </summary>
    public sealed class UnityWebRequestTransport : IWeatherTransport
    {
        private readonly List<UnityWebRequest> _inFlight = new List<UnityWebRequest>(2);

        public void Get(string url, int timeoutSec, Action<WeatherHttpResponse> done)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = Math.Max(1, timeoutSec);
            _inFlight.Add(request);

            request.SendWebRequest().completed += _ =>
            {
                if (!_inFlight.Remove(request)) { request.Dispose(); return; } // cancelled
                var response = new WeatherHttpResponse
                {
                    Code = request.responseCode,
                    Body = request.downloadHandler != null ? request.downloadHandler.text : null,
                    Error = request.error
                };
                response.Reached = request.result == UnityWebRequest.Result.Success ||
                                   request.result == UnityWebRequest.Result.ProtocolError;
                response.TimedOut = !response.Reached && request.error != null &&
                                    request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                request.Dispose();
                done?.Invoke(response);
            };
        }

        public void CancelAll()
        {
            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                UnityWebRequest r = _inFlight[i];
                _inFlight.RemoveAt(i);
                r.Abort(); // completed still fires; Remove() fails there, so it only disposes
            }
        }
    }

    /// <summary>Result of one full weather query (nowcast + forecast).</summary>
    public struct WeatherFetchResult
    {
        public WeatherFetchStatus Status;
        public WeatherKind Kind;
        public int Pty;
        public int Sky;
        /// <summary>Short reason for logs and the engineer panel (KMA code, HTTP code). No key material.</summary>
        public string Detail;
        /// <summary>Last request URL with the key replaced by ***.</summary>
        public string RedactedUrl;
        /// <summary>Forecast base time, KST "yyyyMMdd HHmm".</summary>
        public string BaseStamp;

        public bool Ok => Status == WeatherFetchStatus.Ok;
    }

    /// <summary>
    /// Queries the KMA nowcast (PTY) then the ultra short forecast (SKY, fallback PTY), and classifies them.
    /// Rain or snow from either call is enough; sky state is needed only when nothing falls.
    /// </summary>
    public sealed class WeatherClient
    {
        public const int DefaultTimeoutSec = 10;

        private readonly IWeatherTransport _transport;
        private readonly int _timeoutSec;

        public WeatherClient(IWeatherTransport transport, int timeoutSec = DefaultTimeoutSec)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _timeoutSec = timeoutSec;
        }

        public void Fetch(string serviceKey, int nx, int ny, DateTime utcNow, Action<WeatherFetchResult> done)
        {
            DateTime kst = KmaRequest.ToKst(utcNow);
            DateTime ncstBase = KmaRequest.NcstBase(kst);
            DateTime fcstBase = KmaRequest.FcstBase(kst);
            string ncstUrl = KmaRequest.BuildUrl(KmaEndpoint.UltraSrtNcst, serviceKey, ncstBase, nx, ny);

            _transport.Get(ncstUrl, _timeoutSec, ncstResponse =>
            {
                KmaParseResult ncst = Interpret(KmaEndpoint.UltraSrtNcst, ncstResponse);
                if (ncst.Status == WeatherFetchStatus.Auth)
                {
                    done?.Invoke(Failure(ncst, ncstUrl, ncstBase)); // same key: the forecast call would fail too
                    return;
                }

                string fcstUrl = KmaRequest.BuildUrl(KmaEndpoint.UltraSrtFcst, serviceKey, fcstBase, nx, ny);
                _transport.Get(fcstUrl, _timeoutSec, fcstResponse =>
                {
                    KmaParseResult fcst = Interpret(KmaEndpoint.UltraSrtFcst, fcstResponse);
                    done?.Invoke(Combine(ncst, fcst, ncstUrl, fcstUrl, fcstBase));
                });
            });
        }

        /// <summary>Turns an HTTP outcome into a parse result. Public for tests.</summary>
        public static KmaParseResult Interpret(KmaEndpoint endpoint, WeatherHttpResponse response)
        {
            if (!response.Reached)
                return KmaParseResult.Fail(response.TimedOut ? WeatherFetchStatus.Timeout : WeatherFetchStatus.Network,
                    null, response.Error);

            if (response.Code == 401 || response.Code == 403)
                return KmaParseResult.Fail(WeatherFetchStatus.Auth, "HTTP " + response.Code, null);

            if (response.Code >= 400 || (response.Code != 200 && response.Code != 0))
            {
                // data.go.kr sometimes wraps key errors in a non-200 XML body; prefer that reason.
                KmaParseResult inner = KmaResponseParser.Parse(endpoint, response.Body);
                if (inner.Status == WeatherFetchStatus.Auth) return inner;
                return KmaParseResult.Fail(WeatherFetchStatus.ServiceError, "HTTP " + response.Code, inner.Message);
            }

            return KmaResponseParser.Parse(endpoint, response.Body);
        }

        /// <summary>Public for tests.</summary>
        public static WeatherFetchResult Combine(KmaParseResult ncst, KmaParseResult fcst,
            string ncstUrl, string fcstUrl, DateTime fcstBase)
        {
            int pty = ncst.Ok ? ncst.Pty : (fcst.Ok ? fcst.Pty : -1);
            int sky = fcst.Ok ? fcst.Sky : -1;
            WeatherKind kind = WeatherClassifier.Classify(pty, sky);

            if (kind == WeatherKind.Unknown)
            {
                KmaParseResult failed = !fcst.Ok ? fcst : ncst;
                return Failure(failed, !fcst.Ok ? fcstUrl : ncstUrl, fcstBase);
            }

            string detail = "PTY " + pty + " SKY " + sky + (ncst.Ok ? "" : " (nowcast " + ncst.Status + ")") +
                            (fcst.Ok ? "" : " (forecast " + fcst.Status + ")");
            return new WeatherFetchResult
            {
                Status = WeatherFetchStatus.Ok,
                Kind = kind,
                Pty = pty,
                Sky = sky,
                Detail = detail,
                RedactedUrl = KmaRequest.Redact(fcstUrl),
                BaseStamp = KmaRequest.FormatDate(fcstBase) + " " + KmaRequest.FormatTime(fcstBase)
            };
        }

        private static WeatherFetchResult Failure(KmaParseResult r, string url, DateTime baseKst)
        {
            string detail = r.Status.ToString();
            if (!string.IsNullOrEmpty(r.ResultCode)) detail += " " + r.ResultCode;
            if (!string.IsNullOrEmpty(r.Message)) detail += " " + r.Message;
            return new WeatherFetchResult
            {
                Status = r.Status,
                Kind = WeatherKind.Unknown,
                Pty = -1,
                Sky = -1,
                Detail = detail,
                RedactedUrl = KmaRequest.Redact(url),
                BaseStamp = KmaRequest.FormatDate(baseKst) + " " + KmaRequest.FormatTime(baseKst)
            };
        }
    }
}
