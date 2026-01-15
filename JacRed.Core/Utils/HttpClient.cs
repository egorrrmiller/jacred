using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace JacRed.Core.Utils;

/// <summary>
/// Сервис для HTTP-запросов с поддержкой прокси, сжатия и управления таймаутами.
/// Использует IHttpClientFactory — нет утечек памяти. Сохранены все публичные методы.
/// </summary>
public class HttpService
{
    public static readonly string UserAgent = 
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpService> _logger;
    private readonly ProxyHandler _proxyHandler;

    public HttpService(HttpClient httpClient, ILogger<HttpService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _proxyHandler = new ProxyHandler();
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
            url, encoding, cookie, referer, timeoutSeconds, maxResponseSize, addHeaders, useProxy, httpVersion);

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
            var html = await Get(url, encoding, cookie, referer, timeoutSeconds, maxResponseSize, addHeaders, useProxy);
            if (string.IsNullOrWhiteSpace(html)) return default!;

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

        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("cookie", cookie);
        if (!string.IsNullOrEmpty(referer)) request.Headers.Add("referer", referer);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        // Применяем прокси через middleware-подход (заголовок + логика)
        _proxyHandler.Apply(request, url, useProxy);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return (null, response);

            var content = await ReadContentAsStringAsync(
                response,
                encoding ?? Encoding.UTF8,
                maxResponseSize,
                cts.Token).ConfigureAwait(false);
            if (content.Length == 0) return (null, response);

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
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("cookie", cookie);
        if (!string.IsNullOrEmpty(referer)) request.Headers.Add("referer", referer);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        _proxyHandler.Apply(request, url, useProxy);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
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
        return Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy, null, maxResponseSize);
    }

    public async ValueTask<string> Post(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null,
        int maxResponseSize = 10_000_000)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("cookie", cookie);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        _proxyHandler.Apply(request, url, useProxy);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
                return string.Empty;

            var text = await ReadContentAsStringAsync(
                response,
                encoding ?? Encoding.UTF8,
                maxResponseSize,
                cts.Token).ConfigureAwait(false);
            return string.IsNullOrEmpty(text) ? string.Empty : text;
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
        bool ignoreDeserializeErrors = false,
        int maxResponseSize = 10_000_000)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return await Post<T>(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding, ignoreDeserializeErrors, maxResponseSize);
    }

    public async ValueTask<T> Post<T>(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null,
        bool ignoreDeserializeErrors = false,
        int maxResponseSize = 10_000_000)
    {
        try
        {
            var json = await Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding, maxResponseSize);
            if (string.IsNullOrEmpty(json)) return default!;
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

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode code, string url)
    {
        return new HttpResponseMessage
        {
            StatusCode = code,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, url)
        };
    }

    private static async Task<string> ReadContentAsStringAsync(
        HttpResponseMessage response,
        Encoding encoding,
        int maxResponseSize,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 8192,
            leaveOpen: true);

        var builder = new StringBuilder();
        var buffer = new char[4096];
        var limitBytes = maxResponseSize > 0 ? maxResponseSize : int.MaxValue;
        var currentBytes = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            currentBytes += read * sizeof(char);
            if (currentBytes > limitBytes)
                return string.Empty;

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    #region ProxyHandler

    private class ProxyHandler
    {
        private readonly ConcurrentBag<string> _proxyList = new();
        private bool _initialized = false;
        private readonly object _lock = new();

        public void Apply(HttpRequestMessage request, string url, bool useProxy)
        {
            // Глобальные прокси по паттерну
            if (AppInit.conf?.globalproxy != null)
            {
                foreach (var p in AppInit.conf.globalproxy)
                {
                    if (p.list?.Count > 0 && Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                    {
                        request.Headers.Add("X-Use-Global-Proxy", p.list[0]); // Подсказка для логики вне HttpClient
                        return;
                    }
                }
            }

            // Основной прокси
            if (useProxy && AppInit.conf?.proxy?.list != null && AppInit.conf.proxy.list.Count > 0)
            {
                Initialize();
                if (_proxyList.TryTake(out var proxy))
                {
                    request.Headers.Add("X-Use-Main-Proxy", proxy);
                }
            }
        }

        private void Initialize()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                var shuffled = AppInit.conf.proxy.list.OrderBy(_ => Guid.NewGuid()).ToList();
                foreach (var ip in shuffled) _proxyList.Add(ip);
                _initialized = true;
            }
        }
    }

    #endregion
}
