using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Каталог контента: кеширует список всех ключей раздач и строит быстрые индексы по ним.
/// </summary>
public class ContentCatalogService : IContentCatalog
{
    private const string AllKeysCacheKey = "catalog:all_keys";
    private const string FastIndexCacheKey = "catalog:fast_index";
    private readonly ICacheService _cache;
    private readonly string _connectionString;
    private readonly ILogger<ContentCatalogService> _logger;
    private readonly IMemoryCache _memoryCache;

    public ContentCatalogService(
        ICacheService cache,
        ILogger<ContentCatalogService> logger,
        IMemoryCache memoryCache,
        string connectionString)
    {
        _cache = cache;
        _logger = logger;
        _memoryCache = memoryCache;
        _connectionString = connectionString;
    }

    /// <summary>
    ///     Возвращает кешированный словарь всех ключей (Key → TorrentInfo), обновляя каждые 30 минут.
    /// </summary>
    public ConcurrentDictionary<string, TorrentInfo>? GetAllKeys()
    {
        if (_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(AllKeysCacheKey, out var value))
            return value;

        value = LoadAllFromDatabase();
        var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
        _memoryCache.Set(AllKeysCacheKey, value, cacheEntryOptions);

        return value;
    }

    #region Private Methods

    /// <summary>
    ///     Загружает уникальные ключи из torrents и формирует словарь
    /// </summary>
    private ConcurrentDictionary<string, TorrentInfo> LoadAllFromDatabase()
    {
        const string sql = @"
            SELECT DISTINCT
                coalesce(search_name, regexp_replace(lower(coalesce(name, '')), '[^a-z0-9а-яё]+', '', 'g')) AS key1,
                coalesce(original_search_name, regexp_replace(lower(coalesce(original_name, '')), '[^a-z0-9а-яё]+', '', 'g')) AS key2,
                MAX(update_time) AS update_time
            FROM public.torrents
            GROUP BY key1, key2";

        using var connection = new NpgsqlConnection(_connectionString);
        var result = connection.Query(sql);

        var dict = new ConcurrentDictionary<string, TorrentInfo>();

        foreach (var item in result)
        {
            var key1 = (string)item.key1;
            var key2 = (string)item.key2;
            if (string.IsNullOrWhiteSpace(key1) && string.IsNullOrWhiteSpace(key2))
                continue;

            var key = $"{key1}:{key2}";

            dict.TryAdd(key, new TorrentInfo
            {
                updateTime = (DateTime)item.update_time,
                fileTime = ((DateTime)item.update_time).ToFileTimeUtc()
            });
        }

        _logger.LogInformation("Загружено {Count} уникальных ключей из torrents", dict.Count);
        return dict;
    }

    /// <summary>
    ///     Строит быстрый индекс по частям ключей (split по ':') для ускоренного поиска.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildFastIndex(Dictionary<string, List<string>> fastdb,
        ConcurrentDictionary<string, TorrentInfo>? allKeys)
    {
        if (allKeys == null) return;
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

    #endregion
}