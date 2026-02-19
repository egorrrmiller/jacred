using System.Text;

namespace JacRed.Core.Utils;

public class RequestOptions
{
    public Dictionary<string, string>? Headers { get; set; }
    public string? Cookie { get; set; }
    public string? Referer { get; set; }
    public string? ContentType { get; set; }
    
    /// <summary>
    /// Таймаут запроса в секундах. По умолчанию 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Максимальный размер ответа в байтах. По умолчанию 10 МБ.
    /// </summary>
    public int MaxResponseSizeBytes { get; set; } = 10_000_000;
    
    public Encoding Encoding { get; set; } = Encoding.UTF8;
    
    public bool AllowAutoRedirect { get; set; } = true;
    
    public CancellationToken CancellationToken { get; set; } = default;

    public static RequestOptions Default => new();
}