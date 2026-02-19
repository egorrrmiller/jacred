using System.Net;
using System.Text;
using JacRed.Core.Models.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace JacRed.Core.Utils;

public class HttpService
{
    private readonly HttpClient _defaultClient;
    private readonly ILogger<HttpService> _logger;
    private readonly Config _config;
    private HttpClient? _noRedirectClient;

    // Используем современный User-Agent по умолчанию
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public HttpService(HttpClient httpClient, ILogger<HttpService> logger, IOptions<Config> config)
    {
        _defaultClient = httpClient;
        _logger = logger;
        _config = config.Value;
    }

    /// <summary>
    /// Выполняет GET запрос и возвращает строку.
    /// </summary>
    public async Task<string> GetStringAsync(string url, RequestOptions? options = null)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null, options);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        return await ReadContentAsync(response, options);
    }

    /// <summary>
    /// Выполняет GET запрос и возвращает десериализованный объект.
    /// </summary>
    public async Task<T?> GetJsonAsync<T>(string url, RequestOptions? options = null)
    {
        var json = await GetStringAsync(url, options);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON from {Url}", url);
            return default;
        }
    }

    /// <summary>
    /// Выполняет GET запрос и возвращает байты.
    /// </summary>
    public async Task<byte[]?> GetBytesAsync(string url, RequestOptions? options = null)
    {
        using var response = await SendAsync(HttpMethod.Get, url, null, options);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsByteArrayAsync(options?.CancellationToken ?? default);
    }

    /// <summary>
    /// Выполняет GET запрос и возвращает HttpResponseMessage (для стриминга или кастомной обработки).
    /// Вызывающий код обязан освободить ресурс (Dispose).
    /// </summary>
    public async Task<HttpResponseMessage> GetResponseAsync(string url, RequestOptions? options = null)
    {
        return await SendAsync(HttpMethod.Get, url, null, options, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// Выполняет POST запрос с контентом.
    /// </summary>
    public async Task<string> PostAsync(string url, HttpContent content, RequestOptions? options = null)
    {
        using var response = await SendAsync(HttpMethod.Post, url, content, options);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        return await ReadContentAsync(response, options);
    }
    
    /// <summary>
    /// Выполняет POST запрос и возвращает HttpResponseMessage.
    /// </summary>
    public async Task<HttpResponseMessage> PostResponseAsync(string url, HttpContent? content, RequestOptions? options = null)
    {
        return await SendAsync(HttpMethod.Post, url, content, options, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// Универсальный метод отправки запроса.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, 
        string url, 
        HttpContent? content, 
        RequestOptions? options,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        options ??= RequestOptions.Default;
        
        var request = new HttpRequestMessage(method, url);
        
        if (content != null)
            request.Content = content;

        var client = GetClient(options.AllowAutoRedirect);

        // Настройка заголовков
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

        if (!string.IsNullOrEmpty(options.Cookie))
            request.Headers.TryAddWithoutValidation("Cookie", options.Cookie);
            
        if (!string.IsNullOrEmpty(options.Referer))
            request.Headers.TryAddWithoutValidation("Referer", options.Referer);

        if (options.Headers != null)
        {
            foreach (var header in options.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        try
        {
            // Если таймаут задан в опциях, используем CancellationTokenSource
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            return await client.SendAsync(request, completionOption, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request to {Url} timed out after {Seconds}s", url, options.TimeoutSeconds);
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout) { RequestMessage = request };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request to {Url} failed", url);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request };
        }
    }

    private HttpClient GetClient(bool allowRedirect)
    {
        if (allowRedirect)
            return _defaultClient;

        if (_noRedirectClient == null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                CheckCertificateRevocationList = false,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            };

            if (_config.Proxy?.List?.Count > 0)
            {
                var proxyUrl = _config.Proxy.List[Random.Shared.Next(_config.Proxy.List.Count)];
                var proxy = new WebProxy(proxyUrl);

                if (_config.Proxy.UseAuth && !string.IsNullOrEmpty(_config.Proxy.Username))
                    proxy.Credentials = new NetworkCredential(_config.Proxy.Username, _config.Proxy.Password);

                proxy.BypassProxyOnLocal = _config.Proxy.BypassOnLocal;
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            _noRedirectClient = new HttpClient(handler);
        }

        return _noRedirectClient;
    }

    /// <summary>
    /// Читает контент ответа с учетом кодировки и лимита размера.
    /// </summary>
    private async Task<string> ReadContentAsync(HttpResponseMessage response, RequestOptions? options)
    {
        options ??= RequestOptions.Default;
        
        try
        {
            // Если контент слишком большой, даже не начинаем читать (проверка по заголовку)
            if (response.Content.Headers.ContentLength > options.MaxResponseSizeBytes)
            {
                _logger.LogWarning("Response from {Url} is too large ({Size} bytes)", 
                    response.RequestMessage?.RequestUri, response.Content.Headers.ContentLength);
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(options.CancellationToken);
            
            // Используем StreamReader для корректной работы с кодировкой
            using var reader = new StreamReader(stream, options.Encoding);
            
            // Читаем блоками, чтобы контролировать размер
            var buffer = new char[4096];
            var sb = new StringBuilder();
            var totalRead = 0;
            int read;

            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += read; // Это символы, не байты, но для грубой оценки пойдет
                if (totalRead > options.MaxResponseSizeBytes) // Грубая защита
                {
                    _logger.LogWarning("Response limit exceeded while reading from {Url}", response.RequestMessage?.RequestUri);
                    return string.Empty;
                }
                
                sb.Append(buffer, 0, read);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read content from {Url}", response.RequestMessage?.RequestUri);
            return string.Empty;
        }
    }
}