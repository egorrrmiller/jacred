using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Models.AppConf;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace JacRed.Core.Utils;

/// <summary>
///     Сервис для HTTP-запросов с поддержкой прокси, сжатия и безопасного управления ресурсами.
///     Использует IHttpClientFactory для предотвращения утечек сокетов и памяти.
///     Не является статическим — корректно работает в DI.
/// </summary>
public class HttpService
{
    public static readonly string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpService> _logger;
    private readonly ProxyManager _proxyManager;

    public HttpService(
        HttpClient httpClient,
        ILogger<HttpService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _proxyManager = new ProxyManager();
    }

    #region Get

    public async ValueTask<string> Get(
        string url,
        Encoding? encoding = null,
        string? cookie = null,
        string? referer = null,
        int timeoutSeconds = 15,
        int maxResponseSize = 10_000_000,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        int httpVersion = 1)
    {
        var result = await BaseGetAsync(
            url,
            encoding,
            cookie,
            referer,
            timeoutSeconds,
            maxResponseSize,
            addHeaders,
            useProxy,
            httpVersion);

        return result.content ?? string.Empty;
    }

    #endregion

    #region Get<T>

    public async ValueTask<T> Get<T>(
        string url,
        Encoding? encoding = null,
        string? cookie = null,
        string? referer = null,
        int maxResponseSize = 10_000_000,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool ignoreDeserializeErrors = false,
        bool useProxy = false)
    {
        try
        {
            var html = await Get(
                url,
                encoding,
                cookie,
                referer,
                timeoutSeconds,
                maxResponseSize,
                addHeaders,
                useProxy);

            if (string.IsNullOrWhiteSpace(html))
                return default!;

            var settings = ignoreDeserializeErrors
                ? new JsonSerializerSettings { Error = (se, ev) => ev.ErrorContext.Handled = true }
                : null;

            return JsonConvert.DeserializeObject<T>(html, settings)!;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("HttpService.Get<T> failed for {Url}: {Error}", url, ex.Message);
            return default!;
        }
    }

    #endregion

    #region BaseGetAsync

    public async ValueTask<(string? content, HttpResponseMessage response)> BaseGetAsync(
        string url,
        Encoding? encoding = null,
        string? cookie = null,
        string? referer = null,
        int timeoutSeconds = 15,
        int maxResponseSize = 10_000_000,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        int httpVersion = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = new Version(httpVersion, 0)
        };

        // Настройка заголовков
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie))
            request.Headers.Add("cookie", cookie);
        if (!string.IsNullOrEmpty(referer))
            request.Headers.Add("referer", referer);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        // Прокси через DelegatingHandler (см. регистрацию в DI)
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return (null, response);

            var contentBytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (contentBytes.Length == 0)
                return (null, response);

            var content = encoding != null
                ? encoding.GetString(contentBytes)
                : Encoding.UTF8.GetString(contentBytes);

            return string.IsNullOrWhiteSpace(content) ? (null, response) : (content, response);
        }
        catch (OperationCanceledException) when (timeoutSeconds > 0)
        {
            return (null, CreateErrorResponse(HttpStatusCode.RequestTimeout, url));
        }
        catch (Exception ex)
        {
            _logger.LogDebug("BaseGetAsync failed for {Url}: {Error}", url, ex.Message);
            return (null, CreateErrorResponse(HttpStatusCode.InternalServerError, url));
        }
    }

    #endregion

    #region Download

    public async ValueTask<byte[]?> Download(
        string url,
        string? cookie = null,
        string? referer = null,
        int timeoutSeconds = 30,
        int maxResponseSize = 10_000_000,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie))
            request.Headers.Add("cookie", cookie);
        if (!string.IsNullOrEmpty(referer))
            request.Headers.Add("referer", referer);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return null;

            var data = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return data.Length == 0 ? null : data;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode code, string url)
    {
        return new HttpResponseMessage
        {
            StatusCode = code,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url)
        };
    }

    #region ProxyManager

    private class ProxyManager
    {
        private readonly object _lock = new();
        private readonly ConcurrentBag<string> _proxyRandomList = new();
        private bool _initialized;

        public void ConfigureProxy(HttpClientHandler handler, string url, bool useProxy)
        {
            handler.UseProxy = false;

            // Global proxy by pattern
            if (AppInit.conf?.globalproxy != null)
                foreach (var p in AppInit.conf.globalproxy)
                {
                    if (p.list == null || p.list.Count == 0 || !Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                        continue;

                    handler.UseProxy = true;
                    handler.Proxy = CreateWebProxy(p);
                    return;
                }

            // Main proxy
            if (useProxy && AppInit.conf?.proxy?.list != null && AppInit.conf.proxy.list.Count > 0)
            {
                InitializeProxyList();
                handler.UseProxy = true;
                handler.Proxy = CreateWebProxy(AppInit.conf.proxy);
            }
        }

        private void InitializeProxyList()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                if (AppInit.conf?.proxy?.list != null)
                {
                    var shuffled = AppInit.conf.proxy.list.OrderBy(_ => Guid.NewGuid()).ToList();
                    foreach (var ip in shuffled)
                        _proxyRandomList.Add(ip);
                }

                _initialized = true;
            }
        }

        private WebProxy CreateWebProxy(ProxySettings settings)
        {
            var ip = settings.list.Count == 1
                ? settings.list[0]
                : settings.list[Random.Shared.Next(settings.list.Count)];

            var credentials = settings.useAuth
                ? new NetworkCredential(settings.username, settings.password)
                : null;

            return new WebProxy(ip, settings.BypassOnLocal, null, credentials);
        }
    }

    #endregion

    #region Post

    public ValueTask<string> Post(
        string url,
        string data,
        string? cookie = null,
        int maxResponseSize = 10_000_000,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy);
    }

    public async ValueTask<string> Post(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };

        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie))
            request.Headers.Add("cookie", cookie);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return string.Empty;

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (bytes.Length == 0)
                return string.Empty;

            return encoding != null
                ? encoding.GetString(bytes)
                : Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region Post<T>

    public async ValueTask<T> Post<T>(
        string url,
        string data,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null,
        bool ignoreDeserializeErrors = false)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return await Post<T>(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding,
            ignoreDeserializeErrors);
    }

    public async ValueTask<T> Post<T>(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null,
        bool ignoreDeserializeErrors = false)
    {
        try
        {
            var json = await Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding);
            if (string.IsNullOrEmpty(json))
                return default!;

            var settings = ignoreDeserializeErrors
                ? new JsonSerializerSettings { Error = (se, ev) => ev.ErrorContext.Handled = true }
                : null;

            return JsonConvert.DeserializeObject<T>(json, settings)!;
        }
        catch
        {
            return default!;
        }
    }

    #endregion
}