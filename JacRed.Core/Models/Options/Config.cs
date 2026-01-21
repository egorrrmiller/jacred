using JacRed.Core.Enums;
using JacRed.Core.Models.AppConf;
using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

/// <summary>
///     Глобальная конфигурация приложения (обычно из config.yml).
/// </summary>
public class Config
{
    /// <summary>
    ///     IP-адрес для прослушивания входящих соединений (например, "0.0.0.0" или "127.0.0.1").
    ///     Значение "any" означает прослушивание всех интерфейсов.
    /// </summary>
    [ConfigurationKeyName("listen-ip")]
    public string ListenIp { get; set; } = "any";

    /// <summary>
    ///     Порт для запуска веб-сервера.
    /// </summary>
    [ConfigurationKeyName("listen-port")]
    public int ListenPort { get; set; } = 9117;

    /// <summary>
    ///     API-ключ для защиты доступа к методам API.
    ///     Если не задан, доступ открыт (или ограничен другими способами).
    /// </summary>
    [ConfigurationKeyName("api-key")]
    public string? ApiKey { get; set; }

    /// <summary>
    ///     Включить раздачу статических файлов (веб-интерфейс).
    /// </summary>
    [ConfigurationKeyName("web")]
    public bool Web { get; set; } = true;

    /// <summary>
    /// Максимальное количество результатов в выдаче
    /// </summary>
    [ConfigurationKeyName("max-result-count")]
    public int MaxResultCount { get; set; } = 250;

    /// <summary>
    ///     Включить объединение дубликатов раздач (по InfoHash) в результатах поиска.
    /// </summary>
    [ConfigurationKeyName("merge-duplicates")] 
    public bool MergeDuplicates { get; set; } = true;

    /// <summary>
    ///     Настройки для RuTracker (авторизация и т.д.).
    /// </summary>
    [ConfigurationKeyName("rutracker")]
    public Tracker RuTracker { get; set; } = new();
    
    /// <summary>
    ///     Настройки прокси-серверов для исходящих запросов к трекерам.
    /// </summary>
    [ConfigurationKeyName("proxy")]
    public ProxySettings Proxy { get; set; } = new();

    /// <summary>
    ///     Настройки кеша
    /// </summary>
    [ConfigurationKeyName("cache")]
    public Cache Cache { get; set; } = new();

    /// <summary>
    ///     Список трекеров, которые будут синхронизированы.
    /// </summary>
    [ConfigurationKeyName("sync-trackers")]
    public List<TrackerType> SyncTrackers { get; set; } = new();

    /// <summary>
    ///     Список трекеров, результаты которых будут удалены из ответа.
    /// </summary>
    [ConfigurationKeyName("disable-trackers")]
    public List<TrackerType> DisableTrackers { get; set; } = new();
}
