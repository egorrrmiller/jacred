using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Services;

public class TorrentSearchPipeline : ITorrentSearchPipeline
{
    private readonly ICacheService _cacheService;
    private readonly Config _config;
    private readonly HttpService _httpService;
    private readonly ITorrentMergerService _mergeService;
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITrackerSearchService _trackerSearchService;

    public TorrentSearchPipeline(
        ITorrentSearchService searchService,
        ITorrentRepository torrentRepository,
        ITrackerSearchService trackerSearchService,
        ITorrentMergerService mergeService,
        ICacheService cacheService,
        HttpService httpService,
        IOptions<Config> config)
    {
        _searchService = searchService;
        _torrentRepository = torrentRepository;
        _trackerSearchService = trackerSearchService;
        _mergeService = mergeService;
        _cacheService = cacheService;
        _httpService = httpService;
        _config = config.Value;
    }

    /// <summary>Единый пайплайн поиска: локально → трекеры → фильтры → сортировка → merge.</summary>
    public async Task<TorrentSearchPipelineResult> SearchAsync(
        TorrentSearchRequest request)
    {
        var (search, altname) = await ResolveKpImdb(request.Search, request.AltName);

        // В старой ручке /api/v1.0/torrents типы фильтровались после выборки.
        // Чтобы не отсекать результаты (например, фильмы с пометкой "сезон"),
        // не передаем mediaType в поиск и фильтруем по type ниже.
        var torrents = await SearchLocalAsync(search, altname, null, request.Exact);
        torrents = FilterAllowedTrackers(torrents).ToList();
        var usedFallback = false;

        // Если точный поиск ничего не дал — пробуем нестрогий вариант локально (как в старой логике).
        if (request.Exact && torrents.Count == 0)
        {
            torrents = await SearchLocalAsync(search, altname, null, false);
            torrents = FilterAllowedTrackers(torrents).ToList();
        }

        if (torrents.Count == 0)
        {
            var trackerQuery = BuildTrackerQuery(search, altname);
            if (!string.IsNullOrWhiteSpace(trackerQuery))
            {
                var fetched = await _trackerSearchService.SearchAsync(
                    trackerQuery,
                    _trackerSearchService.GetSupportedTrackers());

                if (fetched.Count > 0)
                {
                    await _torrentRepository.AddOrUpdateAsync(fetched);
                    usedFallback = true;
                    torrents = await SearchLocalAsync(search, altname, null, request.Exact);
                    torrents = FilterAllowedTrackers(torrents).ToList();

                    if (request.Exact && torrents.Count == 0)
                    {
                        torrents = await SearchLocalAsync(search, altname, null, false);
                        torrents = FilterAllowedTrackers(torrents).ToList();
                    }
                }
            }
        }

        var filtered = ApplyFilters(
            torrents,
            request.Type,
            request.Tracker,
            request.Relased,
            request.Quality,
            request.VideoType,
            request.Voice,
            request.Season);

        var sorted = ApplySort(filtered, request.Sort);
        var merged = await _mergeService.MergeAsync(sorted);

        return new TorrentSearchPipelineResult
        {
            Items = merged,
            UsedTrackerFallback = usedFallback
        };
    }

    private async Task<(string? search, string? altname)> ResolveKpImdb(string? search, string? altname)
    {
        if (string.IsNullOrWhiteSpace(search) || !Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            return (search, altname);

        var cacheKey = CacheKeyBuilder.Build("api", "v1.0", "torrents", search);
        var cache = await _cacheService.GetOrCreateAsync(
            cacheKey,
            async () =>
            {
                var uri = search.StartsWith("kp")
                    ? $"&kp={search[2..]}"
                    : $"&imdb={search}";

                var response =
                    await _httpService.Get<JObject>(
                        $"https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1{uri}",
                        timeoutSeconds: 8);
                var data = response?.Value<JObject>("data");
                return (data?.Value<string>("original_name"), data?.Value<string>("name"));
            },
            TimeSpan.FromDays(1));

        return !string.IsNullOrWhiteSpace(cache.Item1) && !string.IsNullOrWhiteSpace(cache.Item2)
            ? (cache.Item1, cache.Item2)
            : (cache.Item1 ?? cache.Item2, altname);
    }

    private async Task<List<TorrentDetails>> SearchLocalAsync(
        string? search,
        string? altname,
        int? mediaType,
        bool exact)
    {
        if (exact)
            return await _searchService.SearchByTitleAsync(search, altname, mediaType: mediaType, exact: true);

        return await _searchService.SearchByQueryAsync($"{search} {altname}".Trim(), mediaType);
    }

    private static IEnumerable<TorrentDetails> ApplyFilters(
        IEnumerable<TorrentDetails> source,
        string? type,
        string? tracker,
        long relased,
        long quality,
        string? videotype,
        string? voice,
        long season)
    {
        var query = source;

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(t => t.Types?.Contains(type) == true);

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

        return query;
    }

    private static IEnumerable<TorrentDetails> ApplySort(IEnumerable<TorrentDetails> source, string? sort)
    {
        return sort?.ToLower() switch
        {
            "sid" => source.OrderByDescending(t => t.Sid),
            "pir" => source.OrderByDescending(t => t.Pir),
            "size" => source.OrderByDescending(t => t.Size),
            "create" => source.OrderByDescending(t => t.CreateTime),
            _ => source.OrderByDescending(t => t.CreateTime)
        };
    }

    private static string BuildTrackerQuery(string? search, string? altname)
    {
        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(altname))
            return string.Empty;

        return $"{search} {altname}".Trim();
    }

    private static int? TypeToId(string? type)
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

    private IEnumerable<TorrentDetails> FilterAllowedTrackers(IEnumerable<TorrentDetails> source)
    {
        return source.Where(t =>
        {
            if (!Enum.TryParse<TrackerType>(t.TrackerName, true, out var trackerType))
                return false;

            if (_config.SyncTrackers.Count > 0 &&
                !_config.SyncTrackers.Contains(trackerType))
                return false;

            return !_config.DisableTrackers.Contains(trackerType);
        });
    }
}