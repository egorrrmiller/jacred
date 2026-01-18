using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using TorrentInfo = JacRed.Core.Models.Api.TorrentInfo;

namespace JacRed.Api.Services;

public class JackettFacadeService : IJackettFacadeService
{
    private readonly ICacheService _cacheService;
    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentMergerService _mergeService;
    private readonly ITorrentSearchPipeline _searchPipeline;
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITrackerSearchService _trackerSearchService;

    public JackettFacadeService(
        IContentCatalog contentCatalog,
        ICacheService cacheService,
        ITorrentMergerService mergeService,
        ITorrentSearchService searchService,
        ITorrentSearchPipeline searchPipeline,
        ITrackerSearchService trackerSearchService,
        ITorrentRepository torrentRepository)
    {
        _contentCatalog = contentCatalog;
        _cacheService = cacheService;
        _mergeService = mergeService;
        _searchService = searchService;
        _searchPipeline = searchPipeline;
        _trackerSearchService = trackerSearchService;
        _torrentRepository = torrentRepository;
    }

    /// <summary>Поиск для Jackett v2 с кэшированием результатов.</summary>
    public async Task<RootObject> SearchJackettAsync(
        string apikey,
        string query,
        string title,
        string titleOriginal,
        int year,
        Dictionary<string, string> category,
        int isSerial,
        string? userAgent,
        string queryString)
    {
        if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour != 0)
            return await BuildJackettAsync(apikey, query, title, titleOriginal, year, category, isSerial, userAgent,
                queryString);

        var cacheKey = GenerateCacheKey(query, title, titleOriginal, year, category, isSerial);
        return await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => BuildJackettAsync(apikey, query, title, titleOriginal, year, category, isSerial, userAgent,
                queryString),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>Поиск для API v1.0 с применением пайплайна и кэша.</summary>
    public async Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(
        string search,
        string altname,
        bool exact,
        string? type,
        string? sort,
        string? tracker,
        string? voice,
        string? videotype,
        long relased,
        long quality,
        long season,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildV1CacheKey(search, altname, exact, type, sort, tracker, voice, videotype, relased, quality,
            season);

        var pipelineResult = await _searchPipeline.SearchAsync(new TorrentSearchRequest
        {
            Search = search,
            AltName = altname,
            Exact = exact,
            Type = type,
            Sort = sort,
            Tracker = tracker,
            Voice = voice,
            VideoType = videotype,
            Relased = relased,
            Quality = quality,
            Season = season
        }, cancellationToken);

        var response = pipelineResult.Items.Take(2000).Select(t => new V1TorrentResponse
        {
            tracker = t.TrackerName,
            url = t.Url?.StartsWith("http") == true ? t.Url : null,
            title = t.Title,
            size = t.Size,
            sizeName = t.SizeName,
            createTime = t.CreateTime,
            sid = t.Sid,
            pir = t.Pir,
            magnet = t.Magnet,
            name = t.Name,
            originalname = t.OriginalName,
            relased = t.Relased,
            videotype = t.VideoType,
            quality = t.Quality,
            voices = t.Voices?.ToArray(),
            seasons = t.Seasons?.ToArray(),
            types = t.Types
        }).ToList();

        if (pipelineResult.UsedTrackerFallback)
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));

        return response;
    }

    /// <summary>Возвращает время последнего обновления master_db.</summary>
    public DateTime GetLastUpdateDb()
    {
        var db = _contentCatalog.GetAllKeys();
        return db.Values.MaxBy(i => i.updateTime)?.updateTime ?? new DateTime(2000, 1, 1);
    }

    private async Task<List<Result>> BuildJackettResults(IEnumerable<TorrentDetails> torrents, bool isNumRequest)
    {
        var results = new List<Result>();

        foreach (var t in torrents)
        {
            var languages = new HashSet<string>(t.Languages ?? []);

            var categoryIds = GetCategoryIds(t, out var categoryDesc);
            var infoHash = ExtractInfoHash(t.Magnet);
            var guid = !string.IsNullOrWhiteSpace(t.Magnet) ? t.Magnet : t.Url ?? t.Title;
            var link = !string.IsNullOrWhiteSpace(t.Magnet) ? t.Magnet : t.Url ?? string.Empty;
            var details = t.Url?.StartsWith("http") == true ? t.Url : null;

            results.Add(new Result
            {
                TrackerId = t.TrackerName,
                TrackerType = "public",
                Tracker = t.TrackerName,
                Guid = guid ?? string.Empty,
                Link = link,
                Comments = details ?? string.Empty,
                Details = details ?? string.Empty,
                Title = t.Title,
                Size = t.Size,
                PublishDate = t.CreateTime,
                Category = categoryIds,
                CategoryDesc = categoryDesc,
                Seeders = t.Sid,
                Peers = t.Pir,
                Grabs = 0,
                InfoHash = infoHash,
                MagnetUri = t.Magnet,
                Languages = languages,
                Description = t.Title,
                DownloadVolumeFactor = 0,
                UploadVolumeFactor = 1,
                MinimumRatio = 0,
                MinimumSeedTime = 0,
                Info = isNumRequest
                    ? null
                    : new TorrentInfo
                    {
                        name = t.Name,
                        originalname = t.OriginalName,
                        sizeName = t.SizeName,
                        relased = t.Relased,
                        videotype = t.VideoType,
                        quality = t.Quality,
                        voices = t.Voices,
                        seasons = t.Seasons?.Count > 0 ? t.Seasons : null,
                        types = t.Types
                    }
            });
        }

        return results;
    }

    private static bool IsAllowedTracker(TorrentDetails t)
    {
        if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.TrackerName))
            return false;

        if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.TrackerName))
            return false;

        return true;
    }

    private HashSet<int> GetCategoryIds(TorrentDetails t, out string? desc)
    {
        desc = null;

        if (t.Types == null || t.Types.Length == 0)
            return new HashSet<int>();

        var set = new HashSet<int>();

        foreach (var type in t.Types)
        {
            switch (type)
            {
                case "movie":
                case "multfilm":
                    desc ??= "Movies";
                    set.Add(2000);
                    break;

                case "serial":
                case "multserial":
                    desc ??= "TV";
                    set.Add(5000);
                    break;

                case "documovie":
                case "docuserial":
                    desc ??= "TV/Documentary";
                    set.Add(5080);
                    break;

                case "tvshow":
                    desc ??= "TV/Foreign";
                    set.Add(5020);
                    set.Add(2010);
                    break;

                case "anime":
                    desc ??= "TV/Anime";
                    set.Add(5070);
                    break;
            }
        }

        return set;
    }

    private string GenerateCacheKey(string query, string title, string orig, int year, Dictionary<string, string> cat,
        int serial)
    {
        var normalizedCat = CacheKeyBuilder.NormalizeCategory(cat);
        return CacheKeyBuilder.Build("jackett",
            query,
            title,
            orig,
            year.ToString(),
            normalizedCat,
            serial.ToString());
    }

    private string BuildV1CacheKey(
        string search,
        string altname,
        bool exact,
        string? type,
        string? sort,
        string? tracker,
        string? voice,
        string? videotype,
        long relased,
        long quality,
        long season)
    {
        return CacheKeyBuilder.Build(
            "api",
            "v1.0",
            "torrents",
            search,
            altname,
            exact.ToString(),
            type,
            sort,
            tracker,
            voice,
            videotype,
            relased.ToString(),
            quality.ToString(),
            season.ToString());
    }

    private bool IsNumRequest(string query, string? userAgent, string queryString)
    {
        return query != null &&
               userAgent ==
               "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36" &&
               !queryString.Contains("&is_serial=");
    }

    private (string, string, int) ApplyNumQueryHeuristic(string query, string title, string orig, int year, bool isNum)
    {
        if (!isNum || query == null) return (title, orig, year);

        var m = Regex.Match(query, @"^([^a-z-A-Z]+) ([^а-я-А-я]+)(?: ([0-9]{4}))?$");
        if (!m.Success) return (title, orig, year);

        var g = m.Groups.Values.Skip(1).ToArray();
        if (g.Length < 2) return (title, orig, year);

        if (Regex.IsMatch(g[1].Value, "[a-zA-Z0-9]{2}"))
            return (g[0].Value, g[1].Value, g.Length > 2 ? int.Parse(g[2].Value) : year);

        return (title, orig, year);
    }

    private int? DetermineContentType(int isSerial, Dictionary<string, string> category)
    {
        // Старое поведение: категории влияют только когда is_serial == 0,
        // -1 иные значения не трогаем.
        if (isSerial == 0 && category?.Count > 0)
        {
            var cat = category.First().Value;
            if (cat.Contains("5020") || cat.Contains("2010")) return 3;
            if (cat.Contains("5080")) return 4;
            if (cat.Contains("5070")) return 5;
            if (cat.StartsWith("20")) return 1;
            if (cat.StartsWith("50")) return 2;
        }

        return isSerial switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            _ => null
        };
    }

    private async Task<RootObject> BuildJackettAsync(
        string apikey,
        string query,
        string title,
        string titleOriginal,
        int year,
        Dictionary<string, string> category,
        int isSerial,
        string? userAgent,
        string queryString)
    {
        var isNumRequest = IsNumRequest(query, userAgent, queryString);
        var contentType = DetermineContentType(isSerial, category);
        (title, titleOriginal, year) = ApplyNumQueryHeuristic(query, title, titleOriginal, year, isNumRequest);

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal) &&
            !string.IsNullOrWhiteSpace(query))
        {
            var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && !parts[0].Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я')))
                title = parts[0];
            else
                title = query;
        }

        var hasTitles = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(titleOriginal);

        var torrents = hasTitles
            ? await _searchService.SearchByTitleAsync(title, titleOriginal, year, contentType, true)
            : await _searchService.SearchByQueryAsync(query, contentType);

        if (hasTitles && torrents.Count == 0)
        {
            var combined = $"{title} {titleOriginal}".Trim();
            torrents = await _searchService.SearchByQueryAsync(combined, contentType);
        }

        torrents = torrents
            .Where(IsAllowedTracker)
            .Where(t => t.Types != null && !(t.Title?.Contains(" КПК") ?? false))
            .ToList();

        if (torrents.Count == 0)
        {
            var trackerQuery = BuildTrackerQuery(query, title, titleOriginal);
            if (!string.IsNullOrWhiteSpace(trackerQuery))
            {
                var fetched = await _trackerSearchService.SearchAsync(
                    trackerQuery,
                    _trackerSearchService.GetSupportedTrackers(),
                    CancellationToken.None);

                if (fetched.Count > 0)
                {
                    await _torrentRepository.AddOrUpdateAsync(fetched);
                    torrents = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal)
                        ? await _searchService.SearchByQueryAsync(query, contentType)
                        : await _searchService.SearchByTitleAsync(title, titleOriginal, year, contentType, true);

                    torrents = torrents
                        .Where(IsAllowedTracker)
                        .Where(t => t.Types != null && !(t.Title?.Contains(" КПК") ?? false))
                        .ToList();
                }
            }
        }

        var result = await _mergeService.MergeAsync(torrents);

        if (apikey == "rus")
            result = result.Where(t => t.Languages?.Contains("rus") == true ||
                                       t.Types?.Intersect(new[] { "sport", "tvshow", "docuserial" }).Any() == true)
                .ToList();

        var jResult = await BuildJackettResults(result, isNumRequest);
        return new RootObject { Results = jResult, Error = null };
    }

    private static string? ExtractInfoHash(string? magnet)
    {
        if (string.IsNullOrWhiteSpace(magnet))
            return null;

        var m = Regex.Match(magnet, "xt=urn:btih:([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            return m.Groups[1].Value.ToUpperInvariant();

        return null;
    }

    private static string BuildTrackerQuery(string query, string title, string titleOriginal)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return query.Trim();

        return $"{title} {titleOriginal}".Trim();
    }
}
