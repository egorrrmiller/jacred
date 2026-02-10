using System.Text.Json;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Database;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using JacRed.Infrastructure.Migrations.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JacRed.Infrastructure.Persistence.Repositories;

/// <summary>
///     Репозиторий торрентов на PostgreSQL/Dapper: upsert коллекций и чтение по ключам.
/// </summary>
public class TorrentRepository : ITorrentRepository
{
    private const string Schema = DbSchema.Name;
    private readonly ICacheService _cache;
    private readonly Config _config;
    private readonly string _connectionString;
    private readonly IKeyGenerator _keyGenerator;
    private readonly ILogger<TorrentRepository> _logger;
    private readonly ITorrentEnricher _torrentEnricher;

    public TorrentRepository(
        ICacheService cache,
        IKeyGenerator keyGenerator,
        ITorrentEnricher torrentEnricher,
        ILogger<TorrentRepository> logger,
        string connectionString,
        IOptions<Config> config)
    {
        _cache = cache;
        _keyGenerator = keyGenerator;
        _torrentEnricher = torrentEnricher;
        _logger = logger;
        _connectionString = connectionString;
        _config = config.Value;
    }

    /// <summary>
    ///     Добавляет или обновляет торренты, группируя по ключу name:originalname.
    /// </summary>
    public async Task AddOrUpdateAsync(IReadOnlyCollection<TorrentDetails> torrents)
    {
        foreach (var group in torrents.GroupBy(t => _keyGenerator.Build(t.Name, t.OriginalName)))
        {
            var key = group.Key;

            foreach (var torrent in group) await UpsertTorrent(torrent);

            await _cache.InvalidateAsync($"collection:{key}");
        }
    }

    /// <summary>
    ///     Добавляет/обновляет торренты с дополнительной проверкой через предикат.
    /// </summary>
    public async Task AddOrUpdateAsync<T>(
        IReadOnlyCollection<T> torrents,
        Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate)
        where T : TorrentDetails
    {
        foreach (var group in torrents.GroupBy(t => _keyGenerator.Build(t.Name, t.OriginalName)))
        {
            var key = group.Key;
            var currentData = await GetCollectionAsync(key);

            foreach (var torrent in group)
            {
                if (predicate != null && !await predicate(torrent, currentData))
                    continue;

                var enriched = await _torrentEnricher.EnrichAndConvertAsync(torrent);
                await UpsertTorrent(enriched);
            }

            await _cache.InvalidateAsync($"collection:{key}");
        }
    }

    public async Task<List<TorrentDetails>> GetStaleAsync(TimeSpan olderThan, int limit)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var cutoff = DateTime.UtcNow - olderThan;

        var sql = $@"
            SELECT 
                id                      AS ""Id"",
                tracker_name            AS ""TrackerName"",
                types                   AS ""Types"",
                url                     AS ""Url"",
                title                   AS ""Title"",
                sid                     AS ""Sid"",
                pir                     AS ""Pir"",
                size_name               AS ""SizeName"",
                create_time             AS ""CreateTime"",
                update_time             AS ""UpdateTime"",
                check_time              AS ""CheckTime"",
                magnet                  AS ""Magnet"",
                name                    AS ""Name"",
                original_name           AS ""OriginalName"",
                relased                 AS ""Relased"",
                languages               AS ""Languages"",
                source_season_number    AS ""SourceSeasonNumber"",
                source_season_order     AS ""SourceSeasonOrder"",
                size                    AS ""Size"",
                quality                 AS ""Quality"",
                video_type              AS ""VideoType"",
                voices                  AS ""Voices"",
                seasons                 AS ""Seasons"",
                ffprobe                 AS ""Ffprobe"",
                ffprobe_attempts        AS ""FfprobeAttempts""
            FROM {Schema}.torrents
            WHERE update_time < @Cutoff
            ORDER BY tracker_name, update_time ASC
            LIMIT @Limit";

        var rows = await connection.QueryAsync<Torrent>(sql,
            new { Cutoff = cutoff, Limit = limit });

        var list = new List<TorrentDetails>();
        foreach (var row in rows)
        {
            var model = MapToDomainModel(row);
            if (model != null)
                list.Add(model);
        }

        return list;
    }

    /// <summary>
    ///     Возвращает торренты по трекеру. Можно дополнительно отфильтровать по давности (check_time &lt; now - olderThan) и
    ///     ограничить количество.
    /// </summary>
    public async Task<List<TorrentDetails>> GetByTrackerAsync(
        string trackerName,
        TimeSpan? olderThan = null,
        int? limit = null)
    {
        if (string.IsNullOrWhiteSpace(trackerName))
            return [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            SELECT 
                id                      AS ""Id"",
                tracker_name            AS ""TrackerName"",
                types                   AS ""Types"",
                url                     AS ""Url"",
                title                   AS ""Title"",
                sid                     AS ""Sid"",
                pir                     AS ""Pir"",
                size_name               AS ""SizeName"",
                create_time             AS ""CreateTime"",
                update_time             AS ""UpdateTime"",
                check_time              AS ""CheckTime"",
                magnet                  AS ""Magnet"",
                name                    AS ""Name"",
                original_name           AS ""OriginalName"",
                relased                 AS ""Relased"",
                languages               AS ""Languages"",
                source_season_number    AS ""SourceSeasonNumber"",
                source_season_order     AS ""SourceSeasonOrder"",
                size                    AS ""Size"",
                quality                 AS ""Quality"",
                video_type              AS ""VideoType"",
                voices                  AS ""Voices"",
                seasons                 AS ""Seasons"",
                ffprobe                 AS ""Ffprobe"",
                ffprobe_attempts        AS ""FfprobeAttempts""
            FROM {Schema}.torrents
            WHERE tracker_name = @TrackerName
              AND (@UseOlderThan IS FALSE OR check_time < @Cutoff)
            ORDER BY check_time ASC
            {(limit.HasValue ? "LIMIT @Limit" : string.Empty)}";

        var rows = await connection.QueryAsync<Torrent>(sql, new
        {
            TrackerName = trackerName,
            UseOlderThan = olderThan.HasValue,
            Cutoff = olderThan.HasValue ? DateTime.UtcNow - olderThan.Value : DateTime.MinValue,
            Limit = limit
        });

        var result = new List<TorrentDetails>();
        foreach (var db in rows)
        {
            var model = MapToDomainModel(db);
            if (model != null)
                result.Add(model);
        }

        return result;
    }

    public async Task<IReadOnlyCollection<string>> GetSearchQueriesAsync(int limit)
    {
        if (limit <= 0)
            return [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            SELECT query
            FROM {Schema}.search_queries
            ORDER BY last_seen DESC, hits DESC
            LIMIT @Limit";

        var rows = await connection.QueryAsync<string>(sql, new { Limit = limit });

        return rows
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task TrackSearchQueryAsync(string query)
    {
        var normalized = StringConvert.SearchName(query) ?? query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            INSERT INTO {Schema}.search_queries (query, created_at, last_seen, hits)
            VALUES (@Query, now(), now(), 1)
            ON CONFLICT (query)
            DO UPDATE SET
                last_seen = now(),
                hits = {Schema}.search_queries.hits + 1";

        await connection.ExecuteAsync(sql, new { Query = normalized });
    }

    public async Task<List<TorrentDetails>> GetForMediaProbeAsync(
        int limit,
        int maxAttempts,
        IReadOnlyCollection<string>? excludedTypes = null)
    {
        if (limit <= 0)
            return [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var excluded = excludedTypes?.ToArray() ?? [];

        var sql = $@"
            SELECT 
                id                      AS ""Id"",
                tracker_name            AS ""TrackerName"",
                types                   AS ""Types"",
                url                     AS ""Url"",
                title                   AS ""Title"",
                sid                     AS ""Sid"",
                pir                     AS ""Pir"",
                size_name               AS ""SizeName"",
                create_time             AS ""CreateTime"",
                update_time             AS ""UpdateTime"",
                check_time              AS ""CheckTime"",
                magnet                  AS ""Magnet"",
                name                    AS ""Name"",
                original_name           AS ""OriginalName"",
                relased                 AS ""Relased"",
                languages               AS ""Languages"",
                source_season_number    AS ""SourceSeasonNumber"",
                source_season_order     AS ""SourceSeasonOrder"",
                size                    AS ""Size"",
                quality                 AS ""Quality"",
                video_type              AS ""VideoType"",
                voices                  AS ""Voices"",
                seasons                 AS ""Seasons"",
                ffprobe                 AS ""Ffprobe"",
                ffprobe_attempts        AS ""FfprobeAttempts""
            FROM {Schema}.torrents
            WHERE magnet IS NOT NULL
              AND ffprobe IS NULL
              AND ffprobe_attempts < @MaxAttempts
              AND sid > 0
              AND (@ExcludedCount = 0 OR NOT (types && @ExcludedTypes))
            ORDER BY sid DESC, pir DESC, update_time DESC
            LIMIT @Limit";

        var rows = await connection.QueryAsync<Torrent>(sql, new
        {
            Limit = limit,
            MaxAttempts = maxAttempts,
            ExcludedTypes = excluded,
            ExcludedCount = excluded.Length
        });

        var list = new List<TorrentDetails>();
        foreach (var row in rows)
        {
            var model = MapToDomainModel(row);
            if (model != null)
                list.Add(model);
        }

        return list;
    }

    public async Task UpdateMediaProbeAsync(string url, List<FfStream> ffprobe, HashSet<string>? languages)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var ffprobeJson = SerializeFfprobe(ffprobe);
        if (string.IsNullOrWhiteSpace(ffprobeJson))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            UPDATE {Schema}.torrents SET
                ffprobe = @Ffprobe::jsonb,
                languages = @Languages,
                ffprobe_attempts = 0
            WHERE url = @Url";

        await connection.ExecuteAsync(sql, new
        {
            Url = url,
            Ffprobe = ffprobeJson,
            Languages = languages?.ToArray()
        });
    }

    public async Task IncrementMediaProbeAttemptsAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            UPDATE {Schema}.torrents
            SET ffprobe_attempts = COALESCE(ffprobe_attempts, 0) + 1
            WHERE url = @Url";

        await connection.ExecuteAsync(sql, new { Url = url });
    }

    #region Private Methods

    /// <summary>
    ///     Возвращает коллекцию торрентов по ключу, при необходимости обновляя кэш.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key,
        bool updateCache = false)
    {
        var cacheKey = $"collection:{key}";

        if (updateCache)
            await _cache.InvalidateAsync(cacheKey);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async () => await LoadCollectionFromDbAsync(key),
            TimeSpan.FromMinutes(30)
        );
    }

    /// <summary>
    ///     Добавляет или обновляет одну запись в таблице torrents.
    /// </summary>
    private async Task UpsertTorrent(TorrentDetails src)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var details = src;
        var now = DateTime.UtcNow;

        var fetchSql = $@"SELECT types, name, original_name, languages FROM {Schema}.torrents WHERE url = @Url";
        var existing =
            await connection
                .QueryFirstOrDefaultAsync<(string[] Types, string Name, string OriginalName, string[] Languages)?>(
                    fetchSql,
                    new { src.Url });
        var exists = existing != null;

        if (exists && existing.HasValue)
        {
            if ((src.Types == null || src.Types.Length == 0) && existing.Value.Types is { Length: > 0 })
                src.Types = existing.Value.Types;

            if (string.IsNullOrWhiteSpace(src.Name) && !string.IsNullOrWhiteSpace(existing.Value.Name))
                src.Name = existing.Value.Name;

            if (string.IsNullOrWhiteSpace(src.OriginalName) && !string.IsNullOrWhiteSpace(existing.Value.OriginalName))
                src.OriginalName = existing.Value.OriginalName;

            if ((src.Languages == null || src.Languages.Count == 0) &&
                existing.Value.Languages is { Length: > 0 })
                src.Languages = existing.Value.Languages.ToHashSet();
        }

        if (exists)
        {
            var updateSql = $@"
                UPDATE {Schema}.torrents SET
                    tracker_name        = @TrackerName,
                    types               = @Types,
                    title               = @Title,
                    sid                 = @Sid,
                    pir                 = @Pir,
                    size_name           = @SizeName,
                    create_time         = @CreateTime,
                    update_time         = @UpdateTime,
                    check_time          = @CheckTime,
                    magnet              = @Magnet,
                    name                = @Name,
                    original_name       = @OriginalName,
                    relased             = @Relased,
                    languages           = @Languages,
                    source_season_number = @SourceSeasonNumber,
                    source_season_order  = @SourceSeasonOrder,
                    size                = @Size,
                    quality             = @Quality,
                    video_type          = @VideoType,
                    voices              = @Voices,
                    seasons             = @Seasons,
                    search_name         = @SearchName,
                    original_search_name = @OriginalSearchName
                WHERE url = @Url";

            await connection.ExecuteAsync(updateSql, MapToDbModel(src, now));
        }
        else
        {
            var insertSql = $@"
                INSERT INTO {Schema}.torrents
                (id, tracker_name, types, url, title, sid, pir, size_name, 
                 create_time, update_time, check_time, magnet, name, original_name, 
                 relased, languages, source_season_number, 
                 source_season_order, size, quality, video_type, voices, seasons, search_name, original_search_name)
                VALUES
                (@Id, @TrackerName, @Types, @Url, @Title, @Sid, @Pir, @SizeName,
                 @CreateTime, @UpdateTime, @CheckTime, @Magnet, @Name, @OriginalName,
                 @Relased, @Languages, @SourceSeasonNumber,
                 @SourceSeasonOrder, @Size, @Quality, @VideoType, @Voices, @Seasons, @SearchName, @OriginalSearchName)";

            await connection.ExecuteAsync(insertSql, MapToDbModel(src, now, true));
        }
    }

    /// <summary>
    ///     Загружает коллекцию торрентов из БД по ключу.
    /// </summary>
    private async Task<Dictionary<string, TorrentDetails>> LoadCollectionFromDbAsync(string key)
    {
        var terms = ExtractKeyTerms(key);
        if (terms.Length == 0)
            return new Dictionary<string, TorrentDetails>();

        var patterns = terms.Select(term => $"%{term}%").ToArray();

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            SELECT *
            FROM {Schema}.torrents
            WHERE (
                search_name LIKE ANY(@Patterns)
                OR original_search_name LIKE ANY(@Patterns)
            )
            ORDER BY sid DESC, update_time DESC
            LIMIT @MaxRead";

        var torrents = await connection.QueryAsync<Torrent>(sql, new
        {
            Patterns = patterns,
            MaxRead = _config.MaxResultCount
        });

        var dict = new Dictionary<string, TorrentDetails>();

        foreach (var db in torrents)
        {
            var model = MapToDomainModel(db);
            if (model != null)
                dict[db.Url] = model;
        }

        return dict;
    }

    /// <summary>
    ///     Разбивает ключ name:originalname на части для LIKE-поиска.
    /// </summary>
    private static string[] ExtractKeyTerms(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return [];

        return key
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    ///     Маппинг доменной модели в БД-модель.
    /// </summary>
    private Torrent MapToDbModel(TorrentDetails src, DateTime now, bool isNew = false)
    {
        var details = src;
        var searchName = StringConvert.SearchName(src.Name) ?? StringConvert.SearchName(src.Title);
        var originalSearchName = StringConvert.SearchName(src.OriginalName) ?? StringConvert.SearchName(src.Title);

        return new Torrent
        {
            Id = isNew ? Guid.NewGuid() : details?.Id ?? Guid.NewGuid(),
            TrackerName = src.TrackerName,
            Types = src.Types,
            Url = src.Url,
            Title = src.Title,
            Sid = src.Sid,
            Pir = src.Pir,
            SizeName = src.SizeName,
            CreateTime = src.CreateTime,
            UpdateTime = src.UpdateTime,
            CheckTime = now,
            Magnet = src.Magnet,
            Name = src.Name,
            OriginalName = src.OriginalName,
            Relased = src.Relased,
            Languages = src.Languages?.ToArray(),
            SourceSeasonNumber = details?.SourceSeasonNumber,
            SourceSeasonOrder = details?.SourceSeasonOrder,
            Size = details?.Size ?? 0,
            Quality = details?.Quality ?? 0,
            VideoType = details?.VideoType,
            Voices = details?.Voices?.ToArray(),
            Seasons = details?.Seasons?.ToArray(),
            Ffprobe = SerializeFfprobe(details?.Ffprobe),
            FfprobeAttempts = details?.FfprobeAttempts ?? 0,
            SearchName = searchName,
            OriginalSearchName = originalSearchName
        };
    }

    /// <summary>
    ///     Маппинг БД-модели в доменную с обработкой ошибок.
    /// </summary>
    private TorrentDetails? MapToDomainModel(Torrent db)
    {
        try
        {
            var model = new TorrentDetails
            {
                Url = db.Url,
                TrackerName = db.TrackerName,
                Types = db.Types,
                Title = db.Title,
                Sid = db.Sid,
                Pir = db.Pir,
                SizeName = db.SizeName,
                CreateTime = db.CreateTime,
                UpdateTime = db.UpdateTime,
                CheckTime = db.CheckTime,
                Magnet = db.Magnet,
                Name = db.Name,
                OriginalName = db.OriginalName,
                Relased = db.Relased,
                Languages = db.Languages?.ToHashSet(),
                SourceSeasonNumber = db.SourceSeasonNumber,
                SourceSeasonOrder = db.SourceSeasonOrder,
                Size = db.Size,
                Quality = db.Quality,
                VideoType = db.VideoType,
                Voices = db.Voices?.ToHashSet(),
                Seasons = db.Seasons?.ToHashSet(),
                Ffprobe = DeserializeFfprobe(db.Ffprobe),
                FfprobeAttempts = db.FfprobeAttempts,
                Id = db.Id
            };

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map torrent {Url}", db.Url);
            return null;
        }
    }

    private static string? SerializeFfprobe(List<FfStream>? ffprobe)
    {
        if (ffprobe == null || ffprobe.Count == 0)
            return null;

        return JsonSerializer.Serialize(ffprobe);
    }

    private static List<FfStream>? DeserializeFfprobe(string? ffprobeJson)
    {
        if (string.IsNullOrWhiteSpace(ffprobeJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<FfStream>>(ffprobeJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    #endregion
}