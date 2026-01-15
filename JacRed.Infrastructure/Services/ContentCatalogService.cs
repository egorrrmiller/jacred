using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Database;
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

    /// <summary>
    ///     Строит быстрые индексы по частям ключей, с возможностью принудительного обновления.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false)
    {
        if (forceUpdate)
        {
            await _cache.InvalidateAsync(FastIndexCacheKey);
            _memoryCache.Remove(AllKeysCacheKey);
        }

        return await _cache.GetOrCreateAsync(FastIndexCacheKey, () =>
        {
            var fastdb = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allKeys = GetAllKeys();

            BuildFastIndex(fastdb, allKeys);

            _logger.LogInformation("Построен быстрый индекс: {Count} сегментов ключей", fastdb.Count);
            return Task.FromResult(fastdb);
        }, TimeSpan.FromMinutes(30));
    }

    #region Private Methods

    /// <summary>
    ///     Загружает все записи из master_db и формирует словарь ключей.
    /// </summary>
    private ConcurrentDictionary<string, TorrentInfo> LoadAllFromDatabase()
    {
        const string sql = @"SELECT ""Key"", ""UpdateTime"", ""FileTime"" FROM public.master_db";

        using var connection = new NpgsqlConnection(_connectionString);
        var result = connection.Query<MasterDb>(sql);

        var dict = new ConcurrentDictionary<string, TorrentInfo>();

        foreach (var item in result)
            dict.TryAdd(item.Key, new TorrentInfo
            {
                updateTime = item.UpdateTime,
                fileTime = item.FileTime
            });

        _logger.LogInformation("Загружено {Count} записей из master_db", dict.Count);
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
