using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace JacRed.Core.Utils;

/// <summary>
///     Сервис для HTTP-запросов.
///     Прокси теперь настраивается глобально через HttpClientHandler при регистрации в DI,
///     либо через именованные клиенты, если нужна разная логика.
///     В данном классе убрана самописная логика "X-Use-Main-Proxy", так как она не работала.
/// </summary>
public class HttpService
{
    public static readonly string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpService> _logger;
    private HttpClient? _httpClientNoRedirect;

    public HttpService(HttpClient httpClient, ILogger<HttpService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
        bool useProxy = false, // Параметр оставлен для совместимости, но реальное использование зависит от DI
        int httpVersion = 1,
        bool allowRedirect = true)
    {
        var result = await BaseGetAsync(
            url, encoding, cookie, referer, timeoutSeconds, maxResponseSize, addHeaders, useProxy, httpVersion,
            allowRedirect);

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
        bool useProxy = false,
        bool allowRedirect = true)
    {
        try
        {
            var html = await Get(url, encoding, cookie, referer, timeoutSeconds, maxResponseSize, addHeaders, useProxy,
                allowRedirect: allowRedirect);
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
        int httpVersion = 1,
        bool allowRedirect = true)
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

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            // Выбираем клиент: обычный или без редиректов.
            // Прокси будет использоваться, если он настроен в HttpClientHandler при создании клиента.
            using var response = await GetClient(allowRedirect)
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

    private HttpClient GetClient(bool allowRedirect)
    {
        if (allowRedirect)
            return _httpClient;

        if (_httpClientNoRedirect == null)
            _httpClientNoRedirect = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        return _httpClientNoRedirect;
    }

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode code, string url, HttpMethod? method = null)
    {
        return new HttpResponseMessage
        {
            StatusCode = code,
            RequestMessage = new HttpRequestMessage(method ?? HttpMethod.Get, url)
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
            true,
            8192,
            true);

        var builder = new StringBuilder();
        var buffer = new char[4096];
        var limitBytes = maxResponseSize > 0 ? maxResponseSize : int.MaxValue;
        var currentBytes = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            currentBytes += read * sizeof(char);
            if (currentBytes > limitBytes)
                return string.Empty;

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    #region Post

    public ValueTask<string> Post(
        string url,
        string data,
        string? cookie = null,
        int maxResponseSize = 10_000_000,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        bool allowRedirect = true)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy, null, maxResponseSize, allowRedirect);
    }

    public ValueTask<HttpResponseMessage> PostResponse(
        string url,
        string data,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        bool allowRedirect = true)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return PostResponse(url, content, cookie, timeoutSeconds, addHeaders, useProxy, allowRedirect);
    }

    public async ValueTask<HttpResponseMessage> PostResponse(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        bool allowRedirect = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("cookie", cookie);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            return await GetClient(allowRedirect)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSeconds > 0)
        {
            return CreateErrorResponse(HttpStatusCode.RequestTimeout, url, HttpMethod.Post);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("PostResponse failed for {Url}: {Error}", url, ex.Message);
            return CreateErrorResponse(HttpStatusCode.InternalServerError, url, HttpMethod.Post);
        }
    }

    public async ValueTask<string> Post(
        string url,
        HttpContent content,
        string? cookie = null,
        int timeoutSeconds = 15,
        List<(string name, string val)>? addHeaders = null,
        bool useProxy = false,
        Encoding? encoding = null,
        int maxResponseSize = 10_000_000,
        bool allowRedirect = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.Add("cookie", cookie);
        if (addHeaders != null)
            foreach (var (name, val) in addHeaders)
                request.Headers.Add(name, val);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var response = await GetClient(allowRedirect).SendAsync(request, cts.Token).ConfigureAwait(false);

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
        int maxResponseSize = 10_000_000,
        bool allowRedirect = true)
    {
        var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        return await Post<T>(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding,
            ignoreDeserializeErrors, maxResponseSize, allowRedirect);
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
        int maxResponseSize = 10_000_000,
        bool allowRedirect = true)
    {
        try
        {
            var json = await Post(url, content, cookie, timeoutSeconds, addHeaders, useProxy, encoding, maxResponseSize,
                allowRedirect);
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
}