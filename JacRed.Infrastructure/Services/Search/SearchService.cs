using System.Text.RegularExpressions;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;
using TorrentInfo = JacRed.Core.Models.Api.TorrentInfo;

namespace JacRed.Infrastructure.Services.Search;

public class SearchService : BaseSearchService, ISearchService
{
    private readonly ILocalSearchService _localSearch;
    private readonly ITorrentMergerService _merger;
    private readonly IRemoteSearchService _remoteSearch;
    private readonly ITorrentRepository _repository;
    private readonly IQueriesRepository _queriesRepository;
    private readonly IMediaResolverService _mediaResolver;
    private readonly Config _config;

    public SearchService(
        IOptions<Config> config,
        HttpService httpService,
        ICacheService cacheService,
        ILocalSearchService localSearch,
        IRemoteSearchService remoteSearch,
        ITorrentRepository repository,
        ITorrentMergerService merger,
        IQueriesRepository queriesRepository,
        IMediaResolverService mediaResolver) : base(config.Value, httpService, cacheService)
    {
        _localSearch = localSearch;
        _remoteSearch = remoteSearch;
        _repository = repository;
        _merger = merger;
        _queriesRepository = queriesRepository;
        _mediaResolver = mediaResolver;
        _config = config.Value;
    }

    public async Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(TorrentSearchRequest request)
    {
        var cacheKey = CacheKeyBuilder.Build("api", "v1.0", "torrents", request.Title, request.TitleOriginal,
            request.Exact.ToString(), request.Type, request.Sort, request.Tracker, request.Voice, request.VideoType,
            request.Year.ToString(), request.Quality.ToString(), request.Season.ToString());

        return await CacheService.GetOrCreateAsync(cacheKey, async () =>
        {
            var torrents = await ExecuteUnifiedSearch(request, null);

            return torrents.Take(2000).Select(t => new V1TorrentResponse
            {
                tracker = t.TrackerName,
                url = t.Url.StartsWith("http") ? t.Url : null,
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
        }, TimeSpan.FromMinutes(_config.Cache.Expiry));
    }

    public async Task<RootObject> SearchJackettAsync(TorrentSearchRequest request)
    {
        var isNumRequest = IsNumRequest(request);
        var contentType = DetermineContentType(request.IsSerial, request.Categories);
        var (title, titleOriginal, year) = ApplyNumQueryHeuristic(request.Query, request.Title,
            request.TitleOriginal, request.Year, isNumRequest);

        request.Title = title;
        request.TitleOriginal = titleOriginal;
        request.Year = year;

        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.TitleOriginal) &&
            !string.IsNullOrWhiteSpace(request.Query))
        {
            var parts = request.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            request.Title = parts.Length > 0 && !parts[0].Any(c => (c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я'))
                ? parts[0]
                : request.Query;
        }

        var torrents = await ExecuteUnifiedSearch(request, contentType);

        var shouldMerge = (!isNumRequest && Config.MergeDuplicates) || (isNumRequest && Config.MergeNumDuplicates);
        var mergedTorrents = shouldMerge ? await _merger.MergeAsync(torrents) : torrents;
        var result = new RootObject { Results = BuildJackettResults(mergedTorrents, isNumRequest), Error = null };

        var cacheKey = CacheKeyBuilder.Build("jackett", request.Query, request.Title, request.TitleOriginal,
            request.Year.ToString(), CacheKeyBuilder.NormalizeCategory(request.Categories),
            request.IsSerial.ToString());
        
        if (request.ForceSearch)
        {
            await CacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(_config.Cache.Expiry));
            return result;
        }
        
        return await CacheService.GetOrCreateAsync(cacheKey, () => Task.FromResult(result), TimeSpan.FromMinutes(_config.Cache.Expiry));
    }

    private async Task<List<TorrentDetails>> ExecuteUnifiedSearch(TorrentSearchRequest request, int? contentType)
    {
        var (search, altname) = await _mediaResolver.ResolveKpImdb(request.Title, request.TitleOriginal);
        var trackerQuery = StringConvert.ClearTitle(BuildTrackerQuery(search, altname));

        var year = request.Year > 0 ? request.Year : (int?)null;
        
        List<TorrentDetails> torrents;

        if (request.ForceSearch)
        {
            torrents = await RemoteSearchAsync(search, altname, year, contentType, trackerQuery, request.Exact);
        }
        else
        {
            torrents = await LocalSearchAsync(search, altname, year, contentType, request.Exact);

            if (torrents.Count == 0)
                if (!string.IsNullOrWhiteSpace(trackerQuery))
                    torrents = await RemoteSearchAsync(search, altname, year, contentType, trackerQuery, request.Exact);
        }
        
        var filtered = ApplyFilters(torrents, request.Type, request.Tracker, request.Year, request.Quality,
            request.VideoType, request.Voice, request.Season);
        
        torrents = ApplySort(filtered, request.Sort).ToList();
        
        return torrents;
    }

    private async Task<List<TorrentDetails>> LocalSearchAsync(string? search, string? altname, int? year,
        int? contentType, bool exact)
    {
        var torrents = await _localSearch.SearchByTitleAsync(search, altname, year, contentType, exact);

        if (exact && torrents.Count == 0)
            torrents = await _localSearch.SearchByTitleAsync(search, altname, year, contentType);

        return torrents.Where(IsTrackerSearchEnabled).ToList();
    }

    private async Task<List<TorrentDetails>> RemoteSearchAsync(string? search, string? altname, int? year,
        int? contentType, string trackerQuery, bool exact)
    {
        var fetched = await _remoteSearch.SearchAsync(trackerQuery, _remoteSearch.GetSupportedTrackers());

        await _repository.AddOrUpdateAsync(fetched);

        return await LocalSearchAsync(search, altname, year, contentType, exact);
    }

    #region Jackett Helpers

    private bool IsNumRequest(TorrentSearchRequest r)
    {
        return r.Query != null &&
               r.UserAgent ==
               "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36" &&
               !string.IsNullOrEmpty(r.QueryString) && !r.QueryString.Contains("&is_serial=");
    }

    private (string, string, int) ApplyNumQueryHeuristic(string? query, string title, string orig, int year, bool isNum)
    {
        if (!isNum || query == null) return (title, orig, year);
        var m = Regex.Match(query, @"^([^a-z-A-Z]+) ([^а-я-А-я]+)(?: ([0-9]{4}))?$");
        if (!m.Success) return (title, orig, year);
        var g = m.Groups.Values.Skip(1).ToArray();
        return g.Length < 2
            ? (title, orig, year)
            : (g[0].Value, g[1].Value, g.Length > 2 ? int.Parse(g[2].Value) : year);
    }

    private int? DetermineContentType(int isSerial, Dictionary<string, string>? category)
    {
        if (isSerial == 0 && category?.Count > 0)
        {
            var cat = category.First().Value;
            if (cat.Contains("5020") || cat.Contains("2010")) return 3;
            if (cat.Contains("5080")) return 4;
            if (cat.Contains("5070")) return 5;
            if (cat.StartsWith("20")) return 1;
            if (cat.StartsWith("50")) return 2;
        }

        return isSerial switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, _ => null };
    }

    private List<Result> BuildJackettResults(IEnumerable<TorrentDetails> torrents, bool isNumRequest)
    {
        var results = new List<Result>();
        foreach (var t in torrents)
        {
            var ffprobe = isNumRequest ? null : t.Ffprobe;
            var languages = t.Languages?.Count > 0
                ? [..t.Languages]
                : ExtractLanguagesFromFfprobe(ffprobe) ?? [];

            results.Add(new Result
            {
                Tracker = t.TrackerName,
                Details = t.Url.StartsWith("http") ? t.Url : null,
                Title = t.Title,
                Size = t.Size,
                PublishDate = t.CreateTime,
                Category = GetCategoryIds(t, out var categoryDesc),
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
                        name = t.Name, originalname = t.OriginalName, sizeName = t.SizeName, relased = t.Relased,
                        videotype = t.VideoType, quality = t.Quality, voices = t.Voices,
                        seasons = t.Seasons?.Count > 0 ? t.Seasons : null, types = t.Types
                    }
            });
        }

        return results;
    }

    private static HashSet<string>? ExtractLanguagesFromFfprobe(List<FfStream>? streams)
    {
        if (streams == null) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in streams.Where(s => string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase)))
            if (!string.IsNullOrWhiteSpace(s.Tags?.Language))
                set.Add(s.Tags.Language);
        return set.Count > 0 ? set : null;
    }

    private HashSet<int> GetCategoryIds(TorrentDetails t, out string? desc)
    {
        desc = null;
        var set = new HashSet<int>();
        if (t.Types == null) return set;
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

    #endregion
}