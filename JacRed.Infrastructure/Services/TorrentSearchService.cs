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
/// Сервис полнотекстового и быстрого поиска по торрентам.
/// </summary>
public class TorrentSearchService : ITorrentSearchService
{
    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITracksDatabase _tracksDatabase;
    private readonly string _connectionString;
    private readonly ILogger<TorrentSearchService> _logger;

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

    /// <summary>Поиск торрентов по названию (локализованное/оригинальное), с опциональными фильтрами.</summary>
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
            var key = $"{searchName}:{searchOriginal}";
            var cached = await _torrentRepository.GetCollectionAsync(key, updateCache: false);
            return cached.Values
                .Where(t => MatchesFilters(t, year, mediaType))
                .OrderByDescending(t => t.Sid)
                .ThenByDescending(t => t.UpdateTime)
                .ToList();
        }

        return await SearchByFtsAndTrigramAsync(searchName, searchOriginal, year, mediaType);
    }

    /// <summary>Поиск торрентов по свободному запросу.</summary>
    public async Task<List<TorrentDetails>> SearchByQueryAsync(
        string query,
        int? mediaType = null,
        bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<TorrentDetails>();

        var searchQuery = StringConvert.SearchName(query);

        if (exact)
        {
            var fastDb = await _contentCatalog.GetFastIndexes();
            if (fastDb.TryGetValue(searchQuery, out var keys))
            {
                var results = new List<TorrentDetails>();
                foreach (var key in keys)
                {
                    var parts = key.Split(':', 2);
                    var name = parts[0];
                    var original = parts.Length > 1 ? parts[1] : null;

                    var torrents = await SearchByTitleAsync(name, original, mediaType: mediaType, exact: true);
                    results.AddRange(torrents);
                }

                return results
                    .GroupBy(t => t.Url)
                    .Select(g => g.OrderByDescending(t => t.Sid).First())
                    .OrderByDescending(t => t.Sid)
                    .ThenByDescending(t => t.UpdateTime)
                    .Take(AppInit.conf.maxreadfile)
                    .ToList();
            }

            return new List<TorrentDetails>();
        }

        return await SearchByFtsAndTrigramAsync(searchQuery, searchQuery, null, mediaType);
    }

    /// <summary>Сводка по качеству/языкам для найденных ключей.</summary>
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

    private async Task<List<TorrentDetails>> SearchByFtsAndTrigramAsync(
        string searchName,
        string searchOriginal,
        int? year,
        int? mediaType)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = """

                              SELECT *
                              FROM public.torrents
                              WHERE 
                                  -- Trigram или FTS
                                  (title ILIKE '%' || @SearchTerm || '%' OR
                                   name    ILIKE '%' || @SearchTerm || '%' OR
                                   original_name ILIKE '%' || @SearchTerm || '%' OR
                                   search_tsv @@ websearch_to_tsquery('russian', @SearchTerm))
                                  AND @MaxRead > 0
                              ORDER BY ts_rank(search_tsv, websearch_to_tsquery('russian', @SearchTerm)) DESC,
                                       sid DESC, update_time DESC
                              LIMIT @MaxRead
                  """;

        var torrents = await connection.QueryAsync<Torrent>(sql, new
        {
            SearchTerm = !string.IsNullOrEmpty(searchName) ? searchName : searchOriginal,
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

    private bool MatchesFilters(TorrentDetails t, int? year, int? mediaType)
    {
        return (!year.HasValue || MatchesYear(t, year.Value)) &&
               MatchesType(t, mediaType);
    }

    private bool MatchesType(TorrentDetails t, int? mediaType)
    {
        return mediaType switch
        {
            1 => t.Types?.Contains("movie") == true ||
                 t.Types?.Contains("multfilm") == true ||
                 t.Types?.Contains("documovie") == true,
            2 => t.Types?.Contains("serial") == true ||
                 t.Types?.Contains("multserial") == true ||
                 t.Types?.Contains("tvshow") == true,
            3 => t.Types?.Contains("tvshow") == true,
            4 => t.Types?.Contains("docuserial") == true ||
                 t.Types?.Contains("documovie") == true,
            5 => t.Types?.Contains("anime") == true,
            _ => true
        };
    }

    private bool MatchesYear(TorrentDetails t, int year)
    {
        var isMovieType = t.Types?.Contains("movie") == true ||
                          t.Types?.Contains("multfilm") == true ||
                          t.Types?.Contains("documovie") == true;

        return isMovieType
            ? t.Relased == year || t.Relased == year - 1 || t.Relased == year + 1
            : t.Relased >= year - 1;
    }

    private IEnumerable<string> BuildSearchKeys(string name, string original)
    {
        return new[] { name, original }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(StringConvert.SearchName)
            .Where(s => !string.IsNullOrWhiteSpace(s));
    }

    #endregion
}

