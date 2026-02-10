using System.Security.Cryptography;
using System.Text;
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

namespace JacRed.Infrastructure.Services.Search;

public class TorrentSearchPipeline : ITorrentSearchPipeline
{
    private readonly ICacheService _cacheService;
    private readonly Config _config;
    private readonly HttpService _httpService;
    private readonly ILocalSearchService _searchService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IRemoteSearchService _remoteSearchService;
    private readonly ISearchHistoryRepository _searchHistoryRepository;

    public TorrentSearchPipeline(
        ILocalSearchService searchService,
        ITorrentRepository torrentRepository,
        IRemoteSearchService remoteSearchService,
        ICacheService cacheService,
        HttpService httpService,
        IOptions<Config> config,
        ISearchHistoryRepository searchHistoryRepository)
    {
        _searchService = searchService;
        _torrentRepository = torrentRepository;
        _remoteSearchService = remoteSearchService;
        _cacheService = cacheService;
        _httpService = httpService;
        _config = config.Value;
        _searchHistoryRepository = searchHistoryRepository;
    }

    /// <summary>Единый пайплайн поиска: локально → трекеры → фильтры → сортировка → merge.</summary>
    public async Task<TorrentSearchPipelineResult> SearchAsync(
        TorrentSearchRequest request)
    {
        var (search, altname) = await ResolveKpImdb(request.Title, request.TitleOriginal);

        var torrents = await SearchLocalAsync(search, altname, null, request.Exact);

        torrents = torrents.Where(IsAllowedTracker).ToList();

        if (request.Exact && torrents.Count == 0)
        {
            torrents = await SearchLocalAsync(search, altname, null, false);
            torrents = torrents.Where(IsAllowedTracker).ToList();
        }

        var trackerQuery = BuildTrackerQuery(search, altname);

        if (!string.IsNullOrWhiteSpace(trackerQuery))
        {
            var normalizedQuery = StringConvert.SearchName(trackerQuery) ?? trackerQuery.Trim();
            var currentTrackersHash = GetTrackersHash();
            var history = await _searchHistoryRepository.GetAsync(normalizedQuery);

            if (torrents.Count == 0 || 
                history == null || 
                (DateTime.UtcNow - history.LastSearchTime) > TimeSpan.FromHours(12) ||
                history.TrackersHash != currentTrackersHash)
            {
                var fetched = await _remoteSearchService.SearchAsync(
                    trackerQuery,
                    _remoteSearchService.GetSupportedTrackers());

                if (fetched.Count > 0)
                {
                    await _torrentRepository.AddOrUpdateAsync(fetched);

                    torrents = await SearchLocalAsync(search, altname, null, request.Exact);
                    torrents = torrents.Where(IsAllowedTracker).ToList();

                    if (request.Exact && torrents.Count == 0)
                    {
                        torrents = await SearchLocalAsync(search, altname, null, false);
                        torrents = torrents.Where(IsAllowedTracker).ToList();
                    }
                }
                
                await _searchHistoryRepository.AddOrUpdateAsync(normalizedQuery, DateTime.UtcNow, currentTrackersHash);
            }
        }

        var filtered = ApplyFilters(
            torrents,
            request.Type,
            request.Tracker,
            request.Year,
            request.Quality,
            request.VideoType,
            request.Voice,
            request.Season);

        var sorted = ApplySort(filtered, request.Sort);

        return new TorrentSearchPipelineResult
        {
            Items = sorted.ToList()
        };
    }

    private string GetTrackersHash()
    {
        var enabledTrackers = Enum.GetValues<TrackerType>()
            .Where(IsTrackerSearchEnabled)
            .OrderBy(t => t)
            .Select(t => t.ToString());
            
        var key = string.Join(",", enabledTrackers);
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes);
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

    private bool IsAllowedTracker(TorrentDetails t)
    {
        if (!Enum.TryParse<TrackerType>(t.TrackerName, true, out var trackerType))
            return false;

        return IsTrackerSearchEnabled(trackerType);
    }

    private bool IsTrackerSearchEnabled(TrackerType type)
    {
        return type switch
        {
            TrackerType.Rutracker => _config.RuTracker.EnableSearch,
            TrackerType.AnimeLayer => _config.AnimeLayer.EnableSearch,
            TrackerType.NNMClub => _config.NNMClub.EnableSearch,
            TrackerType.Rutor => _config.RuTor.EnableSearch,
            TrackerType.Aniliberty => _config.Aniliberty.EnableSearch,
            TrackerType.Kinozal => _config.Kinozal.EnableSearch,
            _ => true // Если трекер не описан в конфиге явно, считаем его включенным
        };
    }
}