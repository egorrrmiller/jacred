using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Database;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;
using TorrentInfo = JacRed.Core.Models.Api.TorrentInfo;

namespace JacRed.Infrastructure.Services.Search;

public class JackettFacadeService : IJackettFacadeService
{
    private readonly ICacheService _cacheService;
    private readonly Config _config;
    private readonly ITorrentMergerService _mergeService;
    private readonly ITorrentSearchPipeline _searchPipeline;
    private readonly ILocalSearchService _searchService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IRemoteSearchService _remoteSearchService;
    private readonly ISearchHistoryRepository _searchHistoryRepository;

    public JackettFacadeService(
        ICacheService cacheService,
        ITorrentMergerService mergeService,
        ILocalSearchService searchService,
        ITorrentSearchPipeline searchPipeline,
        IRemoteSearchService remoteSearchService,
        ITorrentRepository torrentRepository,
        IOptionsSnapshot<Config> config, ISearchHistoryRepository searchHistoryRepository)
    {
        _cacheService = cacheService;
        _mergeService = mergeService;
        _searchService = searchService;
        _searchPipeline = searchPipeline;
        _remoteSearchService = remoteSearchService;
        _torrentRepository = torrentRepository;
        _searchHistoryRepository = searchHistoryRepository;
        _config = config.Value;
    }

    /// <summary>Поиск для Jackett v2 с кэшированием результатов.</summary>
    public async Task<RootObject> SearchJackettAsync(TorrentSearchRequest request)
    {
        if (!_config.Cache.Enable)
            return await BuildJackettAsync(request);

        var cacheKey = GenerateCacheKey(request.Query, request.Title, request.TitleOriginal, request.Year, request.Categories, request.IsSerial);
        return await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => BuildJackettAsync(request),
            TimeSpan.FromMinutes(5));
    }

    /// <summary>Поиск для API v1.0 с применением пайплайна и кэша.</summary>
    public async Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(TorrentSearchRequest request)
    {
        var cacheKey = BuildV1CacheKey(request.Title, request.TitleOriginal, request.Exact, request.Type, request.Sort,
            request.Tracker, request.Voice, request.VideoType, request.Year, request.Quality,
            request.Season);

        var pipelineResult = await _searchPipeline.SearchAsync(request);

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
        
        await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));

        return response;
    }

    private async Task<List<Result>> BuildJackettResults(IEnumerable<TorrentDetails> torrents, bool isNumRequest)
    {
        var results = new List<Result>();

        foreach (var t in torrents)
        {
            var ffprobe = isNumRequest ? null : t.Ffprobe;
            var languages = t.Languages?.Count > 0
                ? new HashSet<string>(t.Languages)
                : ExtractLanguagesFromFfprobe(ffprobe) ?? new HashSet<string>();

            var categoryIds = GetCategoryIds(t, out var categoryDesc);
            var details = t.Url?.StartsWith("http") == true ? t.Url : null;

            results.Add(new Result
            {
                Tracker = t.TrackerName,
                Details = details,
                Title = t.Title,
                Size = t.Size,
                PublishDate = t.CreateTime,
                Category = categoryIds,
                CategoryDesc = categoryDesc,
                Seeders = t.Sid,
                Peers = t.Pir,
                MagnetUri = t.Magnet ?? string.Empty,
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

    private bool IsAllowedTracker(TorrentDetails t)
    {
        if (!Enum.TryParse<TrackerType>(t.TrackerName, true, out var trackerType))
            return true;

        return IsTrackerSearchEnabled(trackerType);
    }

    private static HashSet<string>? ExtractLanguagesFromFfprobe(List<FfStream>? streams)
    {
        if (streams == null || streams.Count == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stream in streams)
        {
            if (!string.Equals(stream.CodecType, "audio", StringComparison.OrdinalIgnoreCase))
                continue;

            var lang = stream.Tags?.Language;
            if (!string.IsNullOrWhiteSpace(lang))
                set.Add(lang);
        }

        return set.Count > 0 ? set : null;
    }

    private HashSet<int> GetCategoryIds(TorrentDetails t, out string? desc)
    {
        desc = null;

        if (t.Types == null || t.Types.Length == 0)
            return new HashSet<int>();

        var set = new HashSet<int>();

        foreach (var type in t.Types)
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

        return set;
    }

    private string GenerateCacheKey(string? query, string title, string orig, int year, Dictionary<string, string> cat,
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

    private bool IsNumRequest(string? query, string? userAgent, string? queryString)
    {
        return query != null &&
               userAgent ==
               "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36" &&
               !string.IsNullOrEmpty(queryString) &&
               !queryString.Contains("&is_serial=");
    }

    private (string, string, int) ApplyNumQueryHeuristic(string? query, string title, string orig, int year, bool isNum)
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

    private async Task<RootObject> BuildJackettAsync(TorrentSearchRequest request)
    {
        var query = request.Query;
        var userAgent = request.UserAgent;
        var queryString = request.QueryString;
        var isSerial = request.IsSerial;
        var category = request.Categories;
        var title = request.Title;
        var titleOriginal = request.TitleOriginal;
        var year = request.Year;
        
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

        torrents = await GetResult(query, title, titleOriginal, year, contentType, torrents);

        var shouldMerge = (!isNumRequest && _config.MergeDuplicates) ||
                          (isNumRequest && _config.MergeNumDuplicates);
        var result = shouldMerge ? await _mergeService.MergeAsync(torrents) : torrents;

        var jResult = await BuildJackettResults(result, isNumRequest);
        return new RootObject { Results = jResult, Error = null };
    }

    private async Task<List<TorrentDetails>> GetResult(string? query, string title, string titleOriginal, int year, int? contentType, List<TorrentDetails> torrents)
    {
        var trackerQuery = BuildTrackerQuery(query, title, titleOriginal);

        if (!string.IsNullOrWhiteSpace(trackerQuery))
        {
            var normalizedQuery = StringConvert.SearchName(trackerQuery);
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
                    torrents = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(titleOriginal)
                        ? await _searchService.SearchByQueryAsync(query, contentType)
                        : await _searchService.SearchByTitleAsync(title, titleOriginal, year, contentType, true);

                    torrents = torrents
                        .Where(IsAllowedTracker)
                        .Where(t => t.Types != null && !(t.Title?.Contains(" КПК") ?? false))
                        .ToList();
                }

                await _searchHistoryRepository.AddOrUpdateAsync(normalizedQuery, DateTime.UtcNow, currentTrackersHash);
            }
        }

        return torrents;
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

    private static string BuildTrackerQuery(string? query, string title, string titleOriginal)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return query.Trim();

        return $"{title} {titleOriginal}".Trim();
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