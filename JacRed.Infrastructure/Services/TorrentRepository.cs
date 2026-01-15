using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Database;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Репозиторий торрентов на PostgreSQL/Dapper: upsert коллекций и чтение по ключам.
/// </summary>
public class TorrentRepository : ITorrentRepository
{
    private readonly ICacheService _cache;
    private readonly string _connectionString;
    private readonly IKeyGenerator _keyGenerator;
    private readonly ILogger<TorrentRepository> _logger;
    private readonly ITorrentEnricher _torrentEnricher;

    public TorrentRepository(
        ICacheService cache,
        IKeyGenerator keyGenerator,
        ITorrentEnricher torrentEnricher,
        ILogger<TorrentRepository> logger,
        string connectionString)
    {
        _cache = cache;
        _keyGenerator = keyGenerator;
        _torrentEnricher = torrentEnricher;
        _logger = logger;
        _connectionString = connectionString;
    }

    /// <summary>
    ///     Добавляет или обновляет торренты, группируя по ключу name:originalname.
    /// </summary>
    public async Task AddOrUpdateAsync(IReadOnlyCollection<TorrentDetails> torrents)
    {
        foreach (var group in torrents.GroupBy(t => _keyGenerator.Build(t.Name, t.OriginalName)))
        {
            var key = group.Key;

            await UpsertMasterDb(key);

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
            var currentData = await GetCollectionAsync(key, false);

            await UpsertMasterDb(key);

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

    /// <summary>
    ///     Возвращает коллекцию торрентов по ключу, при необходимости обновляя кэш.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key,
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

    #region Private Methods

    /// <summary>
    ///     Обновляет master_db (timestamp/filetime) для указанного ключа.
    /// </summary>
    private async Task UpsertMasterDb(string key)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        const string upsertSql = @"
            INSERT INTO public.master_db (key, update_time, file_time)
            VALUES (@Key, @UpdateTime, @FileTime)
            ON CONFLICT (key) 
            DO UPDATE SET update_time = EXCLUDED.update_time, file_time = EXCLUDED.file_time";

        await connection.ExecuteAsync(upsertSql, new
        {
            Key = key,
            UpdateTime = DateTime.UtcNow,
            FileTime = DateTime.UtcNow.ToFileTimeUtc()
        });
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

        const string existsSql = @"SELECT 1 FROM public.torrents WHERE url = @Url";
        var exists = await connection.QueryFirstOrDefaultAsync<int?>(existsSql, new { src.Url }) == 1;

        if (exists)
        {
            const string updateSql = @"
                UPDATE public.torrents SET
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
                    ffprobe             = @Ffprobe,
                    ffprobe_try_count   = @FfprobeTryCount,
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
            const string insertSql = @"
                INSERT INTO public.torrents
                (id, tracker_name, types, url, title, sid, pir, size_name, 
                 create_time, update_time, check_time, magnet, name, original_name, 
                 relased, languages, ffprobe, ffprobe_try_count, source_season_number, 
                 source_season_order, size, quality, video_type, voices, seasons, search_name, original_search_name)
                VALUES
                (@Id, @TrackerName, @Types, @Url, @Title, @Sid, @Pir, @SizeName,
                 @CreateTime, @UpdateTime, @CheckTime, @Magnet, @Name, @OriginalName,
                 @Relased, @Languages, @Ffprobe, @FfprobeTryCount, @SourceSeasonNumber,
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

        const string sql = @"
            SELECT *
            FROM public.torrents
            WHERE EXISTS (
                SELECT 1 FROM public.master_db
                WHERE key = @Key
            )
            AND (
                coalesce(search_name, regexp_replace(lower(coalesce(name, '')), '[^a-z0-9]+', '', 'g')) LIKE ANY(@Patterns)
                OR coalesce(original_search_name, regexp_replace(lower(coalesce(original_name, '')), '[^a-z0-9]+', '', 'g')) LIKE ANY(@Patterns)
            )";

        var torrents = await connection.QueryAsync<Torrent>(sql, new
        {
            Key = key,
            Patterns = patterns
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
            return Array.Empty<string>();

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
            Ffprobe = src.Ffprobe != null ? JToken.FromObject(src.Ffprobe) : null,
            FfprobeTryCount = details?.FfprobeTryCount ?? 0,
            SourceSeasonNumber = details?.SourceSeasonNumber,
            SourceSeasonOrder = details?.SourceSeasonOrder,
            Size = details?.Size ?? 0,
            Quality = details?.Quality ?? 0,
            VideoType = details?.VideoType,
            Voices = details?.Voices?.ToArray(),
            Seasons = details?.Seasons?.ToArray(),
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
                Ffprobe = db.Ffprobe?.ToObject<List<ffStream>>(),
                FfprobeTryCount = db.FfprobeTryCount,
                SourceSeasonNumber = db.SourceSeasonNumber,
                SourceSeasonOrder = db.SourceSeasonOrder,
                Size = db.Size,
                Quality = db.Quality,
                VideoType = db.VideoType,
                Voices = db.Voices?.ToHashSet(),
                Seasons = db.Seasons?.ToHashSet(),
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

    #endregion
}
