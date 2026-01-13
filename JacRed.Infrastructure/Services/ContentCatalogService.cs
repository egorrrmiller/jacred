using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Сервис для управления глобальным каталогом торрентов (key → TorrentInfo).
///     Хранит данные в IMemoryCache и сохраняет их в сжатый файл Data/masterDb.bz.
///     Оптимизирован для минимизации потребления RAM и предотвращения утечек.
/// </summary>
public class ContentCatalogService : IContentCatalog
{
    private readonly ICacheService _cache;
    private readonly ILogger<ContentCatalogService> _logger;
    private readonly IMemoryCache _memoryCache;

    public ContentCatalogService(
        ICacheService cache,
        ILogger<ContentCatalogService> logger,
        IMemoryCache memoryCache)
    {
        _cache = cache;
        _logger = logger;
        _memoryCache = memoryCache;
    }

    /// <summary>
    ///     Возвращает весь каталог (key → TorrentInfo).
    ///     Синхронный метод — нет смысла в асинхронности при работе с IMemoryCache.
    /// </summary>
    public ConcurrentDictionary<string, TorrentInfo> GetAllKeys()
    {
        const string cacheKey = "catalog:all_keys";
        if (_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(cacheKey, out var value))
            return value;

        return new ConcurrentDictionary<string, TorrentInfo>();
    }

    /// <summary>
    ///     Возвращает быстрый индекс для поиска: подстрока → список ключей.
    ///     Кэшируется на 30 минут.
    ///     🔥 Оптимизирован: работает с копиями строк, избегает Span в async.
    /// </summary>
    public Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false)
    {
        const string cacheKey = "catalog:fast_index";

        return _cache.GetOrCreateAsync(cacheKey, () =>
        {
            var fastdb = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allKeys = GetAllKeys();

            BuildFastIndex(fastdb, allKeys);

            _logger.LogInformation("Создан быстрый индекс: {Count} уникальных ключей", fastdb.Count);
            return Task.FromResult(fastdb);
        }, TimeSpan.FromMinutes(30));
    }

    /// <summary>
    ///     Сохраняет текущий каталог в файл Data/masterDb.bz.
    ///     Создаёт дневной бэкап и удаляет бэкапы старше 3 дней.
    /// </summary>
    public async Task SaveToFileAsync()
    {
        const string cacheKey = "catalog:all_keys";
        if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(cacheKey, out var data) ||
            data.Count == 0)
        {
            _logger.LogWarning("Нечего сохранять: каталог пуст или не загружен");
            return;
        }

        try
        {
            // Основной файл
            await Task.Run(() =>
            {
                using var fileStream = File.Create("Data/masterDb.bz");
                JsonStream.Write(fileStream, data);
            });
            _logger.LogInformation("Сохранено {Count} записей в Data/masterDb.bz", data.Count);

            // Бэкап: masterDb_dd-MM-yyyy.bz
            var backupFile = $"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz";
            if (!File.Exists(backupFile))
            {
                File.Copy("Data/masterDb.bz", backupFile);
                _logger.LogInformation("Создан бэкап: {BackupFile}", backupFile);
            }

            // Удаление старых бэкапов (старше 3 дней)
            var oldBackup = $"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz";
            if (File.Exists(oldBackup))
            {
                File.Delete(oldBackup);
                _logger.LogInformation("Удалён старый бэкап: {OldBackup}", oldBackup);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении masterDb");
        }
    }

    /// <summary>
    ///     Построение индекса — избегает Span в async-контексте.
    ///     Передаётся только строка, а не Span — безопасно для yield/await.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildFastIndex(Dictionary<string, List<string>> fastdb,
        ConcurrentDictionary<string, TorrentInfo> allKeys)
    {
        foreach (var item in allKeys)
        foreach (var part in item.Key.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyPart = part.ToLowerInvariant();

            if (!fastdb.TryGetValue(keyPart, out var list))
            {
                list = new List<string>();
                fastdb[keyPart] = list;
            }

            list.Add(item.Key);
        }
    }
}