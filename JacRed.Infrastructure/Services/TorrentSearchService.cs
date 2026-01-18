using System.Text.RegularExpressions;
using Dapper;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Database;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Сервис полнотекстового и быстрого поиска по торрентам с использованием кэшей индексов.
/// </summary>
public class TorrentSearchService : ITorrentSearchService
{
    private readonly string _connectionString;
    private readonly IContentCatalog _contentCatalog;
    private readonly ILogger<TorrentSearchService> _logger;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITracksDatabase _tracksDatabase;

    public TorrentSearchService(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        ITracksDatabase tracksDatabase,
        string connectionString,
        ILogger<TorrentSearchService> logger)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _tracksDatabase = tracksDatabase;
        _connectionString = connectionString;
        _logger = logger;
    }

    /// <summary>
    ///     Поиск по названию (локализованному и оригинальному) с фильтрами по году, типу и точностью.
    /// </summary>
    public async Task<List<TorrentDetails>> SearchByTitleAsync(
        string title,
        string originalTitle,
        int? year = null,
        int? mediaType = null,
        bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(originalTitle))
            return new List<TorrentDetails>();

        var searchName = StringConvert.SearchName(title);
        var searchOriginal = StringConvert.SearchName(originalTitle);

        if (exact)
        {
            var torrents = await SearchExactByNormalizedNamesAsync(new[] { searchName, searchOriginal }, year, mediaType, $"{title} {originalTitle}");
            return torrents;
        }

        var webTerm = !string.IsNullOrWhiteSpace(title) ? title : originalTitle;
        return await SearchByFtsAndTrigramAsync(searchName, searchOriginal, webTerm, year, mediaType);
    }

    /// <summary>
    ///     Поиск по произвольной строке (быстрый индекс при exact или FTS/триграммы).
    /// </summary>
    public async Task<List<TorrentDetails>> SearchByQueryAsync(
        string query,
        int? mediaType = null,
        bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<TorrentDetails>();

        var searchQuery = StringConvert.SearchName(query);

        if (exact)
            return await SearchExactByNormalizedNamesAsync(new[] { searchQuery }, null, mediaType, query);

        return await SearchByFtsAndTrigramAsync(searchQuery, searchQuery, query, null, mediaType);
    }

    /// <summary>
    ///     Агрегирует информацию о качестве/языках раздач по найденным ключам (с пагинацией).
    /// </summary>
    public async Task<Dictionary<string, Dictionary<int, TorrentQuality>>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type = null,
        int page = 1,
        int take = 1000)
    {
        var db = _contentCatalog.GetAllKeys();
        var results = new Dictionary<string, Dictionary<int, TorrentQuality>>();

        var keys = BuildSearchKeys(name, originalName)
            .Join(db.Keys, s => s, k => k, (_, k) => k);

        if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
            keys = keys.Take(AppInit.conf.maxreadfile);

        foreach (var key in keys)
        {
            var collection = await _torrentRepository.GetCollectionAsync(key, true);
            foreach (var t in collection.Values.Where(t =>
                         t.Types != null && !t.Types.Contains("sport") && t.Relased != 0))
            {
                if (!string.IsNullOrEmpty(type) && !t.Types.Contains(type)) continue;

                var keyName = $"{StringConvert.SearchName(t.Name)}:{StringConvert.SearchName(t.OriginalName)}";
                var langs = _tracksDatabase.GetLanguages(t, t.Ffprobe ?? _tracksDatabase.GetStreams(t.Magnet, t.Types));

                var model = new TorrentQuality
                {
                    types = t.Types.ToHashSet(),
                    createTime = t.CreateTime,
                    updateTime = t.UpdateTime,
                    languages = langs,
                    qualitys = new HashSet<int> { t.Quality }
                };

                if (!results.TryGetValue(keyName, out var yearMap))
                    results[keyName] = yearMap = new Dictionary<int, TorrentQuality>();

                if (yearMap.TryGetValue(t.Relased, out var existing))
                {
                    existing.languages.UnionWith(langs);
                    existing.types.UnionWith(t.Types);
                    existing.qualitys.Add(t.Quality);
                    existing.createTime = existing.createTime < t.CreateTime ? existing.createTime : t.CreateTime;
                    existing.updateTime = existing.updateTime > t.UpdateTime ? existing.updateTime : t.UpdateTime;
                }
                else
                {
                    yearMap[t.Relased] = model;
                }
            }
        }

        if (take == -1)
            return results;

        return results
            .Skip((page - 1) * take)
            .Take(take)
            .ToDictionary(k => k.Key, v => v.Value);
    }

    #region Private Methods

    /// <summary>
    ///     Выполняет точный поиск по нормализованным именам (search_name/original_search_name) без master_db.
    /// </summary>
    private async Task<List<TorrentDetails>> SearchExactByNormalizedNamesAsync(
        IEnumerable<string?> normalizedTerms,
        int? year,
        int? mediaType,
        string? webTerm = null)
    {
        var terms = normalizedTerms.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
        if (terms.Length == 0)
            return new List<TorrentDetails>();

        var likePatterns = terms.Select(t => $"%{t}%").ToArray();

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
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
                ffprobe                 AS ""Ffprobe"",
                ffprobe_try_count       AS ""FfprobeTryCount"",
                source_season_number    AS ""SourceSeasonNumber"",
                source_season_order     AS ""SourceSeasonOrder"",
                size                    AS ""Size"",
                quality                 AS ""Quality"",
                video_type              AS ""VideoType"",
                voices                  AS ""Voices"",
                seasons                 AS ""Seasons"",
                search_tsv              AS ""SearchTsv"",
                search_name             AS ""SearchName"",
                original_search_name    AS ""OriginalSearchName""
            FROM public.torrents
            WHERE (
                (search_name IS NOT NULL AND search_name ILIKE ANY(@Patterns))
                OR (original_search_name IS NOT NULL AND original_search_name ILIKE ANY(@Patterns))
                OR regexp_replace(lower(coalesce(name, '')), '[^a-z0-9а-яё]+', '', 'g') ILIKE ANY(@Patterns)
                OR regexp_replace(lower(coalesce(original_name, '')), '[^a-z0-9а-яё]+', '', 'g') ILIKE ANY(@Patterns)
                OR (@HasWeb AND search_tsv @@ websearch_to_tsquery('russian', @WebTerm))
            )
            ORDER BY sid DESC, update_time DESC
            LIMIT @MaxRead";

        var rows = await connection.QueryAsync<Torrent>(sql, new
        {
            Patterns = likePatterns,
            MaxRead = AppInit.conf.maxreadfile,
            HasWeb = !string.IsNullOrWhiteSpace(webTerm),
            WebTerm = webTerm ?? string.Empty
        });

        var results = new List<TorrentDetails>();
        foreach (var db in rows)
        {
            var model = MapToDomainModel(db);
            if (model != null && MatchesFilters(model, year, mediaType))
                results.Add(model);
        }

        return results;
    }

    /// <summary>
    ///     Выполняет SQL-запрос с FTS/LIKE/триграммами и применяет фильтры по году/типу.
    /// </summary>
    private async Task<List<TorrentDetails>> SearchByFtsAndTrigramAsync(
        string? searchName,
        string? searchOriginal,
        string? webTerm,
        int? year,
        int? mediaType)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var patterns = new[] { searchName, searchOriginal }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => $"%{s}%")
            .Distinct()
            .ToArray();

        var hasWeb = !string.IsNullOrWhiteSpace(webTerm);

        var sql = """

                              SELECT 
                                  id                      AS "Id",
                                  tracker_name            AS "TrackerName",
                                  types                   AS "Types",
                                  url                     AS "Url",
                                  title                   AS "Title",
                                  sid                     AS "Sid",
                                  pir                     AS "Pir",
                                  size_name               AS "SizeName",
                                  create_time             AS "CreateTime",
                                  update_time             AS "UpdateTime",
                                  check_time              AS "CheckTime",
                                  magnet                  AS "Magnet",
                                  name                    AS "Name",
                                  original_name           AS "OriginalName",
                                  relased                 AS "Relased",
                                  languages               AS "Languages",
                                  ffprobe                 AS "Ffprobe",
                                  ffprobe_try_count       AS "FfprobeTryCount",
                                  source_season_number    AS "SourceSeasonNumber",
                                  source_season_order     AS "SourceSeasonOrder",
                                  size                    AS "Size",
                                  quality                 AS "Quality",
                                  video_type              AS "VideoType",
                                  voices                  AS "Voices",
                                  seasons                 AS "Seasons",
                                  search_tsv              AS "SearchTsv",
                                  search_name             AS "SearchName",
                                  original_search_name    AS "OriginalSearchName"
                              FROM public.torrents
                              WHERE 
                                  (
                                     (array_length(@Patterns, 1) IS NOT NULL AND (
                                         (search_name IS NOT NULL AND search_name LIKE ANY(@Patterns)) OR
                                         (original_search_name IS NOT NULL AND original_search_name LIKE ANY(@Patterns)) OR
                                         (search_name IS NULL AND regexp_replace(lower(coalesce(name, '')), '[^a-z0-9]+', '', 'g') LIKE ANY(@Patterns)) OR
                                         (original_search_name IS NULL AND regexp_replace(lower(coalesce(original_name, '')), '[^a-z0-9]+', '', 'g') LIKE ANY(@Patterns))
                                     ))
                                     OR (@HasWeb AND search_tsv @@ websearch_to_tsquery('russian', @WebTerm))
                                  )
                                  AND @MaxRead > 0
                              ORDER BY sid DESC, update_time DESC
                              LIMIT @MaxRead
                  """;

        var torrents = await connection.QueryAsync<Torrent>(sql, new
        {
            Patterns = patterns.Length > 0 ? patterns : Array.Empty<string>(),
            WebTerm = hasWeb ? webTerm : string.Empty,
            HasWeb = hasWeb,
            MaxRead = AppInit.conf.maxreadfile
        });

        var results = new List<TorrentDetails>();

        foreach (var db in torrents)
        {
            var model = MapToDomainModel(db);
            if (model != null && MatchesFilters(model, year, mediaType))
                results.Add(model);
        }

        return results
            .GroupBy(t => t.Url)
            .Select(g => g.OrderByDescending(t => t.Sid).First())
            .ToList();
    }

    /// <summary>
    ///     Преобразует запись БД в доменную модель TorrentDetails.
    /// </summary>
    private TorrentDetails? MapToDomainModel(Torrent db)
    {
        try
        {
            return new TorrentDetails
            {
                Id = db.Id,
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
                Seasons = db.Seasons?.ToHashSet()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to map torrent {Url}", db.Url);
            return null;
        }
    }

    /// <summary>
    ///     Проверяет, удовлетворяет ли раздача фильтрам по году и типу.
    /// </summary>
    private bool MatchesFilters(TorrentDetails t, int? year, int? mediaType)
    {
        var typeOk = MatchesType(t, mediaType);
        if (!typeOk)
            return false;

        if (mediaType == 1 && IsMovieType(t) && SeasonTitleRegex.IsMatch(t.Title ?? string.Empty))
            return false;

        return !year.HasValue || MatchesYear(t, year.Value);
    }

    /// <summary>
    ///     Проверяет принадлежность раздачи к указанному типу (фильм/сериал/аниме/док и т.п.).
    /// </summary>
    private bool MatchesType(TorrentDetails t, int? mediaType)
    {
        return mediaType switch
        {
            // Если типы неизвестны — не режем результаты, чтобы не потерять раздачи без Types.
            1 => t.Types == null || t.Types.Length == 0 || IsMovieType(t),
            2 => t.Types?.Contains("serial") == true ||
                 t.Types?.Contains("multserial") == true ||
                 t.Types?.Contains("tvshow") == true ||
                 t.Types?.Contains("anime") == true ||
                 t.Types?.Contains("docuserial") == true,
            3 => t.Types?.Contains("tvshow") == true,
            4 => t.Types?.Contains("docuserial") == true ||
                 t.Types?.Contains("documovie") == true,
            5 => t.Types?.Contains("anime") == true,
            _ => true
        };
    }

    /// <summary>
    ///     Проверяет соответствие года релиза (для кино допускает ±1 год).
    /// </summary>
    private bool MatchesYear(TorrentDetails t, int year)
    {
        var isMovieType = IsMovieType(t);

        return isMovieType
            ? t.Relased == year || t.Relased == year - 1 || t.Relased == year + 1
            : t.Relased >= year - 1;
    }

    private static bool IsMovieType(TorrentDetails t)
    {
        return t.Types?.Contains("movie") == true ||
               t.Types?.Contains("multfilm") == true ||
               t.Types?.Contains("documovie") == true ||
               t.Types?.Contains("anime") == true;
    }

    private static readonly Regex SeasonTitleRegex =
        new("(сезон|сери(и|я|й))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    ///     Строит нормализованные ключи поиска из локализованного и оригинального названия.
    /// </summary>
    private IEnumerable<string> BuildSearchKeys(string name, string original)
    {
        return new[] { name, original }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(StringConvert.SearchName)
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }

    #endregion
}
