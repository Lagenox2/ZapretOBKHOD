using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace ZapretOBKHOD
{
    internal sealed class CheckResult
    {
        public string Name;
        public bool Ok;
        public bool Warn;
        public long Ms;
        public string Detail;
    }

    internal static class NetTest
    {
        private sealed class Target
        {
            public string Name;
            public string Url;
            public Target(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }

        public static void RunAll(Action<List<CheckResult>> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var results = new List<CheckResult>();
                results.Add(Check(new Target("YouTube", "https://www.youtube.com/")));
                results.Add(Check(new Target("Discord", "https://discord.com/api/v9/gateway")));
                results.Add(Check(new Target("Telegram", "https://web.telegram.org/")));
                if (done != null) done(results);
            });
        }

        private static CheckResult Check(Target target)
        {
            var result = new CheckResult();
            result.Name = target.Name;

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            var watch = Stopwatch.StartNew();
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(target.Url);
                request.Method = "GET";
                request.Proxy = null;
                request.Timeout = 9000;
                request.ReadWriteTimeout = 9000;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 4;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZapretOBKHOD/1.0";
                request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
                request.Headers.Add("Cache-Control", "no-cache");

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    watch.Stop();
                    int code = (int)response.StatusCode;
                    result.Ok = true;
                    result.Warn = code >= 400 || watch.ElapsedMilliseconds > 4000;
                    result.Ms = watch.ElapsedMilliseconds;
                    result.Detail = "HTTP " + code + " · " + result.Ms + " мс";
                    if (watch.ElapsedMilliseconds > 4000) result.Detail += " · медленно";
                }
            }
            catch (WebException ex)
            {
                watch.Stop();
                result.Ms = watch.ElapsedMilliseconds;

                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response is HttpWebResponse)
                {
                    using (var response = (HttpWebResponse)ex.Response)
                    {
                        int code = (int)response.StatusCode;
                        result.Ok = true;
                        result.Warn = true;
                        result.Detail = "HTTP " + code + " · " + result.Ms + " мс";
                    }
                }
                else
                {
                    result.Detail = Describe(ex);
                }
            }
            catch (Exception ex)
            {
                watch.Stop();
                result.Ms = watch.ElapsedMilliseconds;
                result.Detail = ex.Message;
            }

            return result;
        }

        private static string Describe(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.Timeout:
                    return "таймаут — соединение не установлено";
                case WebExceptionStatus.NameResolutionFailure:
                    return "DNS не отвечает";
                case WebExceptionStatus.ConnectFailure:
                    return "сервер недоступен (TCP)";
                case WebExceptionStatus.SecureChannelFailure:
                    return "ошибка TLS";
                case WebExceptionStatus.TrustFailure:
                    return "сертификат не принят";
                case WebExceptionStatus.ConnectionClosed:
                    return "соединение разорвано";
                case WebExceptionStatus.ReceiveFailure:
                    return "соединение сброшено";
                default:
                    return ex.Message;
            }
        }
    }
}
