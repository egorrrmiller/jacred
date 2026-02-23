using System.Text.Json;
using System.Text.RegularExpressions;
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

namespace JacRed.Infrastructure.Services.Search;

/// <summary>
///     Сервис полнотекстового и быстрого поиска по торрентам с использованием кэшей индексов.
/// </summary>
public class LocalSearchService : BaseSearchService, ILocalSearchService
{
    private const string Schema = DbSchema.Name;
    private readonly Config _config;
    private readonly string _connectionString;
    private readonly ILogger<LocalSearchService> _logger;


    public LocalSearchService(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        string connectionString, ILogger<LocalSearchService> logger) : base(config.Value, httpService, cacheService)
    {
        _config = config.Value;
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
            return [];

        var searchName = StringConvert.SearchName(title);
        var searchOriginal = StringConvert.SearchName(originalTitle);

        if (exact)
        {
            var torrents = await SearchExactByNormalizedNamesAsync([searchName, searchOriginal], year,
                mediaType, $"{title} {originalTitle}");
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
            return [];

        var searchQuery = StringConvert.SearchName(query);

        if (exact)
            return await SearchExactByNormalizedNamesAsync([searchQuery], null, mediaType, query);

        return await SearchByFtsAndTrigramAsync(searchQuery, searchQuery, query, null, mediaType);
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
            return [];

        var suffixPattern = mediaType == 1 ? "(\\D|$)" : ".*";

        string ReplaceForRegex(string t) => t.Replace("е", "[её]").Replace("ё", "[её]").Replace("ш", "[шщ]").Replace("щ", "[шщ]");
        string ReplaceForLike(string t) => t.Replace("е", "_").Replace("ё", "_").Replace("ш", "_").Replace("щ", "_");

        var termRegexes = terms
            .Select(t => $"^{ReplaceForRegex(Regex.Escape(t))}{suffixPattern}")
            .ToArray();
        var likeTerms = terms.Select(t => $"%{ReplaceForLike(t)}%").ToArray();
        var rawTerm = string.IsNullOrWhiteSpace(webTerm) ? string.Empty : webTerm.Trim().ToLowerInvariant();
        var rawLikeTerms = string.IsNullOrWhiteSpace(rawTerm) ? Array.Empty<string>() : new[] { $"%{ReplaceForLike(rawTerm)}%" };

        using var connection = new NpgsqlConnection(_connectionString);
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
                ffprobe_attempts        AS ""FfprobeAttempts"",
                search_tsv              AS ""SearchTsv"",
                search_name             AS ""SearchName"",
                original_search_name    AS ""OriginalSearchName""
            FROM {Schema}.torrents
            WHERE (
                (array_length(@Terms, 1) IS NOT NULL AND (
                    (search_name IS NOT NULL AND (search_name = ANY(@Terms) OR search_name ~ ANY(@TermRegexes)))
                    OR (original_search_name IS NOT NULL AND (original_search_name = ANY(@Terms) OR original_search_name ~ ANY(@TermRegexes)))
                ))
                OR (array_length(@LikeTerms, 1) IS NOT NULL AND (
                    (search_name IS NOT NULL AND search_name LIKE ANY(@LikeTerms)) OR
                    (original_search_name IS NOT NULL AND original_search_name LIKE ANY(@LikeTerms))
                ))
                OR (array_length(@RawLikeTerms, 1) IS NOT NULL AND (
                    (lower(title) LIKE ANY(@RawLikeTerms))
                ))
                OR (@HasWeb AND search_tsv @@ websearch_to_tsquery('russian', @WebTerm))
            )
            ORDER BY sid DESC, update_time DESC
            LIMIT @MaxRead";

        var rows = await connection.QueryAsync<Torrent>(sql, new
        {
            Terms = terms,
            LikeTerms = likeTerms.Length > 0 ? likeTerms : [],
            RawLikeTerms = rawLikeTerms,
            TermRegexes = termRegexes,
            MaxRead = _config.MaxResultCount,
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

        var terms = new[] { searchName, searchOriginal }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToArray();
            
        string ReplaceForRegex(string t) => t.Replace("е", "[её]").Replace("ё", "[её]").Replace("ш", "[шщ]").Replace("щ", "[шщ]");
        string ReplaceForLike(string t) => t.Replace("е", "_").Replace("ё", "_").Replace("ш", "_").Replace("щ", "_");

        var suffixPattern = mediaType == 1 ? "(\\D|$)" : ".*";
        var termRegexes = terms
            .Select(t => $"^{ReplaceForRegex(Regex.Escape(t))}{suffixPattern}")
            .ToArray();
        var likeTerms = terms.Select(t => $"%{ReplaceForLike(t)}%").ToArray();
        var rawTerm = string.IsNullOrWhiteSpace(webTerm) ? string.Empty : webTerm.Trim().ToLowerInvariant();
        var rawLikeTerms = string.IsNullOrWhiteSpace(rawTerm) ? Array.Empty<string>() : new[] { $"%{ReplaceForLike(rawTerm)}%" };

        var hasWeb = !string.IsNullOrWhiteSpace(webTerm);

        var sql = $"""

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
                                   source_season_number    AS "SourceSeasonNumber",
                                   source_season_order     AS "SourceSeasonOrder",
                                   size                    AS "Size",
                                   quality                 AS "Quality",
                                   video_type              AS "VideoType",
                                   voices                  AS "Voices",
                                   seasons                 AS "Seasons",
                                   ffprobe                 AS "Ffprobe",
                                   ffprobe_attempts        AS "FfprobeAttempts",
                                   search_tsv              AS "SearchTsv",
                                   search_name             AS "SearchName",
                                   original_search_name    AS "OriginalSearchName"
                               FROM {Schema}.torrents
                               WHERE 
                                   (
                                     (array_length(@Terms, 1) IS NOT NULL AND (
                                         (search_name IS NOT NULL AND (search_name = ANY(@Terms) OR search_name ~ ANY(@TermRegexes))) OR
                                         (original_search_name IS NOT NULL AND (original_search_name = ANY(@Terms) OR original_search_name ~ ANY(@TermRegexes)))
                                     ))
                                     OR (array_length(@LikeTerms, 1) IS NOT NULL AND (
                                         (search_name IS NOT NULL AND search_name LIKE ANY(@LikeTerms)) OR
                                         (original_search_name IS NOT NULL AND original_search_name LIKE ANY(@LikeTerms))
                                     ))
                                     OR (array_length(@RawLikeTerms, 1) IS NOT NULL AND (
                                         (lower(title) LIKE ANY(@RawLikeTerms))
                                     ))
                                     OR (@HasWeb AND search_tsv @@ websearch_to_tsquery('russian', @WebTerm))
                                  )
                                  AND @MaxRead > 0
                              ORDER BY sid DESC, update_time DESC
                               LIMIT @MaxRead
                   """;

        var torrents = await connection.QueryAsync<Torrent>(sql, new
        {
            Terms = terms.Length > 0 ? terms : [],
            LikeTerms = likeTerms.Length > 0 ? likeTerms : [],
            RawLikeTerms = rawLikeTerms,
            TermRegexes = termRegexes.Length > 0 ? termRegexes : [],
            WebTerm = hasWeb ? webTerm : string.Empty,
            HasWeb = hasWeb,
            MaxRead = _config.MaxResultCount
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
                SourceSeasonNumber = db.SourceSeasonNumber,
                SourceSeasonOrder = db.SourceSeasonOrder,
                Size = db.Size,
                Quality = db.Quality,
                VideoType = db.VideoType,
                Voices = db.Voices?.ToHashSet(),
                Seasons = db.Seasons?.ToHashSet(),
                Ffprobe = DeserializeFfprobe(db.Ffprobe),
                FfprobeAttempts = db.FfprobeAttempts
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

        var treatAsMovie = mediaType == 1 && IsMovieType(t);

        if (treatAsMovie && SeasonTitleRegex.IsMatch(t.Title ?? string.Empty))
            return false;

        return !year.HasValue || MatchesYear(t, year.Value, treatAsMovie);
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
    private bool MatchesYear(TorrentDetails t, int year, bool treatAsMovie)
    {
        return treatAsMovie
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