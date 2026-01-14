using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using TorrentInfo = JacRed.Core.Models.Api.TorrentInfo;

namespace JacRed.Api.Services;

public class JackettFacadeService : IJackettFacadeService
{
    private readonly IMemoryCache _cache;
    private readonly ICacheService _cacheService;
    private readonly IContentCatalog _contentCatalog;
    private readonly HttpService _httpService;
    private readonly ITorrentMergerService _mergeService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentSearchService _searchService;
    private readonly ITrackerSearchService _trackerSearchService;
    private readonly ITracksDatabase _tracksDatabase;

    public JackettFacadeService(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        ICacheService cacheService,
        IMemoryCache cache,
        ITorrentSearchService searchService,
        ITorrentMergerService mergeService,
        ITracksDatabase tracksDatabase,
        ITrackerSearchService trackerSearchService,
        HttpService httpService)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _cacheService = cacheService;
        _cache = cache;
        _searchService = searchService;
        _mergeService = mergeService;
        _tracksDatabase = tracksDatabase;
        _trackerSearchService = trackerSearchService;
        _httpService = httpService;
    }

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
        var isNumRequest = IsNumRequest(query, userAgent, queryString);
        var cacheKey = GenerateCacheKey(query, title, titleOriginal, year, category, isSerial);

        if (_cache.TryGetValue(cacheKey, out List<Result> cached))
            return new RootObject { Results = cached };

        var contentType = DetermineContentType(isSerial, category) ?? -1;
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

        var torrents = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal)
            ? await _searchService.SearchByQueryAsync(query, contentType)
            : await _searchService.SearchByTitleAsync(title, titleOriginal, year, contentType, true);

        var result = await _mergeService.MergeAsync(torrents);

        if (apikey == "rus")
            result = result.Where(t => t.Languages?.Contains("rus") == true ||
                                       t.Types?.Intersect(new[] { "sport", "tvshow", "docuserial" }).Any() == true)
                .ToList();

        var jResult = await BuildJackettResults(result, isNumRequest);

        if (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
            _cache.Set(cacheKey, jResult, TimeSpan.FromMinutes(5));

        return new RootObject { Results = jResult };
    }

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
        (search, altname) = await ResolveKpImdb(search, altname);

        var cacheKey = BuildV1CacheKey(search, altname, exact, type, sort, tracker, voice, videotype, relased, quality,
            season);

        var (torrents, usedFallback) = await SearchWithFallbackAsync(search, altname, TypeToId(type), exact,
            cancellationToken);

        var query = torrents.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tracker))
            query = query.Where(t => t.TrackerName == tracker);

        if (relased > 0)
            query = query.Where(t => t.Relased == relased);

        if (quality > 0)
            query = query.Where(t => t.Quality == quality);

        if (!string.IsNullOrWhiteSpace(videotype))
            query = query.Where(t => t.VideoType == videotype);

        if (!string.IsNullOrWhiteSpace(voice))
            query = query.Where(t => t.Voices?.Contains(voice) == true);

        if (season > 0)
            query = query.Where(t => t.Seasons?.Contains((int)season) == true);

        query = sort?.ToLower() switch
        {
            "sid" => query.OrderByDescending(t => t.Sid),
            "pir" => query.OrderByDescending(t => t.Pir),
            "size" => query.OrderByDescending(t => t.Size),
            "create" => query.OrderByDescending(t => t.CreateTime),
            _ => query.OrderByDescending(t => t.CreateTime)
        };

        var result = await _mergeService.MergeAsync(query);

        var response = result.Take(2000).Select(t => new V1TorrentResponse
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

        if (usedFallback)
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));

        return response;
    }

    public async Task<Dictionary<string, Dictionary<int, TorrentQuality>>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type,
        int page,
        int take)
    {
        return await _searchService.GetQualityInfoAsync(name, originalName, type, page, take);
    }

    public DateTime GetLastUpdateDb()
    {
        var db = _contentCatalog.GetAllKeys();
        return db.Values.MaxBy(i => i.updateTime)?.updateTime ?? new DateTime(2000, 1, 1);
    }

    private async Task<(List<TorrentDetails> torrents, bool usedFallback)> SearchWithFallbackAsync(
        string search,
        string altname,
        int? mediaType,
        bool exact,
        CancellationToken cancellationToken)
    {
        var torrents = await SearchLocalAsync(search, altname, mediaType, exact);
        if (torrents.Count > 0)
            return (torrents, false);

        var trackerQuery = BuildTrackerQuery(search, altname);
        if (string.IsNullOrWhiteSpace(trackerQuery))
            return (torrents, false);

        var fetched = await _trackerSearchService.SearchAsync(
            trackerQuery,
            _trackerSearchService.GetSupportedTrackers(),
            cancellationToken);

        if (fetched.Count == 0)
            return (torrents, false);

        await _torrentRepository.AddOrUpdateAsync(fetched);
        torrents = await SearchLocalAsync(search, altname, mediaType, exact);

        return (torrents, true);
    }

    private async Task<List<TorrentDetails>> SearchLocalAsync(
        string search,
        string altname,
        int? mediaType,
        bool exact)
    {
        if (exact)
            return await _searchService.SearchByTitleAsync(search, altname, mediaType: mediaType, exact: true);

        return await _searchService.SearchByQueryAsync($"{search} {altname}".Trim(), mediaType);
    }

    private async Task<(string, string)> ResolveKpImdb(string search, string altname)
    {
        if (string.IsNullOrWhiteSpace(search) || !Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            return (search, altname);

        var cacheKey = $"api/v1.0/torrents:{search}";
        if (!_cache.TryGetValue(cacheKey, out (string original, string name) cache))
        {
            var uri = search.StartsWith("kp")
                ? $"&kp={search[2..]}"
                : $"&imdb={search}";

            var response =
                await _httpService.Get<JObject>($"https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1{uri}",
                    timeoutSeconds: 8);
            var data = response?.Value<JObject>("data");
            cache = (data?.Value<string>("original_name"), data?.Value<string>("name"));
            _cache.Set(cacheKey, cache, TimeSpan.FromDays(1));
        }

        return !string.IsNullOrWhiteSpace(cache.original) && !string.IsNullOrWhiteSpace(cache.name)
            ? (cache.original, cache.name)
            : (cache.original ?? cache.name, altname);
    }

    private async Task<List<Result>> BuildJackettResults(IEnumerable<TorrentDetails> torrents, bool isNumRequest)
    {
        var results = new List<Result>();

        foreach (var t in torrents)
        {
            var languages = new HashSet<string>();
            var ffprobe = isNumRequest ? null : await GetFfprobe(t, languages);

            var categoryIds = GetCategoryIds(t, out var categoryDesc);

            results.Add(new Result
            {
                Tracker = t.TrackerName,
                Details = t.Url?.StartsWith("http") == true ? t.Url : null,
                Title = t.Title,
                Size = t.Size,
                PublishDate = t.CreateTime,
                Category = categoryIds,
                CategoryDesc = categoryDesc,
                Seeders = t.Sid,
                Peers = t.Pir,
                MagnetUri = t.Magnet,
                Ffprobe = ffprobe,
                Languages = languages,
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

    private async Task<List<ffStream>> GetFfprobe(TorrentDetails t, HashSet<string> languages)
    {
        if (!AppInit.conf.tracks || t.Ffprobe?.Count > 0)
        {
            var langs = _tracksDatabase.GetLanguages(t, t.Ffprobe);
            if (langs?.Any() == true)
                languages.UnionWith(langs);
            return t.Ffprobe;
        }

        if (t.Types?.Length == 0)
            t.Types = [];

        var streams = _tracksDatabase.GetStreams(t.Magnet, t.Types);
        var streamLangs = _tracksDatabase.GetLanguages(t, streams);
        if (streamLangs?.Any() == true)
            languages.UnionWith(streamLangs);

        return streams;
    }

    private HashSet<int> GetCategoryIds(TorrentDetails t, out string? desc)
    {
        desc = null;

        if (t.Types == null || t.Types.Length == 0)
            return new HashSet<int>();

        switch (t.Types[0])
        {
            case "movie":
            case "multfilm":
            case "documovie":
                desc = "Movies";
                return new HashSet<int> { 2000 };

            case "serial":
            case "multserial":
                desc = "TV";
                return new HashSet<int> { 5000 };

            case "docuserial":
                desc = "TV/Documentary";
                return new HashSet<int> { 5080 };

            case "tvshow":
                desc = "TV/Foreign";
                return new HashSet<int> { 5020, 2010 };

            case "anime":
                desc = "TV/Anime";
                return new HashSet<int> { 5070 };

            default:
                return new HashSet<int>();
        }
    }

    private string GenerateCacheKey(string query, string title, string orig, int year, Dictionary<string, string> cat,
        int serial)
    {
        var catKey = cat?.Count > 0
            ? string.Join(",", cat.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))
            : "none";

        return $"jackett:{query}:{title}:{orig}:{year}:{catKey}:{serial}";
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
        return $"api:v1.0:torrents:{search ?? string.Empty}:{altname ?? string.Empty}:{exact}:{type ?? string.Empty}:" +
               $"{sort ?? string.Empty}:{tracker ?? string.Empty}:{voice ?? string.Empty}:{videotype ?? string.Empty}:" +
               $"{relased}:{quality}:{season}";
    }

    private string BuildTrackerQuery(string search, string altname)
    {
        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(altname))
            return string.Empty;

        return $"{search} {altname}".Trim();
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
        if (category?.Count > 0)
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

    private int? TypeToId(string? type)
    {
        return type switch
        {
            "movie" => 1,
            "serial" => 2,
            "tvshow" => 3,
            "docuserial" => 4,
            "anime" => 5,
            _ => null
        };
    }
}
