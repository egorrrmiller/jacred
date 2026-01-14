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
///     Сервис для управления глобальным каталогом торрентов (key → TorrentInfo).
/// </summary>
public class ContentCatalogService : IContentCatalog
{
    private readonly ICacheService _cache;
    private readonly ILogger<ContentCatalogService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly string _connectionString;

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
    ///     Возвращает весь каталог (key → TorrentInfo).
    ///     Кэшируется в памяти на 30 минут. При отсутствии — загружается из БД.
    /// </summary>
    public ConcurrentDictionary<string, TorrentInfo>? GetAllKeys()
    {
        const string cacheKey = "catalog:all_keys";

        if (_memoryCache.TryGetValue<ConcurrentDictionary<string, TorrentInfo>>(cacheKey, out var value))
            return value;

        value = LoadAllFromDatabase();
        var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
        _memoryCache.Set(cacheKey, value, cacheEntryOptions);

        return value;
    }

    /// <summary>
    ///     Возвращает быстрый индекс для поиска: подстрока → список ключей.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false)
    {
        const string cacheKey = "catalog:fast_index";

        if (forceUpdate)
            await _cache.InvalidateAsync(cacheKey);

        return await _cache.GetOrCreateAsync(cacheKey, () =>
        {
            var fastdb = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var allKeys = GetAllKeys();

            BuildFastIndex(fastdb, allKeys);

            _logger.LogInformation("Создан быстрый индекс: {Count} уникальных ключей", fastdb.Count);
            return Task.FromResult(fastdb);
        }, TimeSpan.FromMinutes(30));
    }

    #region Private Methods

    private ConcurrentDictionary<string, TorrentInfo> LoadAllFromDatabase()
    {
        const string sql = @"SELECT ""Key"", ""UpdateTime"", ""FileTime"" FROM public.master_db";

        using var connection = new NpgsqlConnection(_connectionString);
        var result = connection.Query<MasterDb>(sql);

        var dict = new ConcurrentDictionary<string, TorrentInfo>();

        foreach (var item in result)
        {
            dict.TryAdd(item.Key, new TorrentInfo
            {
                updateTime = item.UpdateTime,
                fileTime = item.FileTime
            });
        }

        _logger.LogInformation("Загружено {Count} записей из master_db", dict.Count);
        return dict;
    }

    /// <summary>
    ///     Построение индекса — избегает Span в async-контексте.
    ///     Передаётся только строка, а не Span — безопасно для yield/await.
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
