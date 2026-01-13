using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Services;

/// <summary>
/// Сервис для управления глобальным каталогом торрентов (key → TorrentInfo).
/// Хранит данные в IMemoryCache и сохраняет их в сжатый файл Data/masterDb.bz.
/// </summary>
public class ContentCatalogService : IContentCatalog
{
    private readonly ICacheService _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ContentCatalogService> _logger;

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
    /// Возвращает весь каталог (key → TorrentInfo).
    /// </summary>
    public Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync()
    {
        var cacheKey = "catalog:all_keys";
        if (_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(cacheKey, out var value))
            return Task.FromResult(value);

        return Task.FromResult(new ConcurrentDictionary<string, TorrentInfo>());
    }

    /// <summary>
    /// Возвращает быстрый индекс для поиска: подстрока → список ключей.
    /// Кэшируется на 1 час.
    /// </summary>
    public Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false)
    {
        var cacheKey = "catalog:fast_index";

        return _cache.GetOrCreateAsync(cacheKey, async () =>
        {
            var fastdb = new Dictionary<string, List<string>>();
            var allKeys = await GetAllKeysAsync();

            foreach (var item in allKeys)
            {
                foreach (var part in item.Key.Split(':', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!fastdb.TryGetValue(part, out var list))
                    {
                        list = new List<string>();
                        fastdb[part] = list;
                    }

                    list.Add(item.Key);
                }
            }

            _logger.LogInformation("Создан быстрый индекс: {Count} уникальных ключей", fastdb.Count);
            return fastdb;
        }, TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Сохраняет текущий каталог в файл Data/masterDb.bz.
    /// Создаёт дневной бэкап и удаляет бэкапы старше 3 дней.
    /// </summary>
    public async Task SaveToFileAsync()
    {
        const string cacheKey = "catalog:all_keys";
        if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(cacheKey, out var data) || data.Count == 0)
        {
            _logger.LogWarning("Нечего сохранять: каталог пуст или не загружен");
            return;
        }

        try
        {
            // Основной файл
            JsonStream.Write("Data/masterDb.bz", data);
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
}