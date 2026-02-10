using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Services.Search;

public abstract class BaseSearchService
{
    protected readonly Config Config;
    private readonly HttpService _httpService;
    protected readonly ICacheService CacheService;

    protected BaseSearchService(Config config, HttpService httpService, ICacheService cacheService)
    {
        Config = config;
        _httpService = httpService;
        CacheService = cacheService;
    }

    protected async Task<(string? search, string? altname)> ResolveKpImdb(string? search, string? altname)
    {
        if (string.IsNullOrWhiteSpace(search) || !Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            return (search, altname);

        var cacheKey = CacheKeyBuilder.Build("api", "v1.0", "torrents", search);
        var cache = await CacheService.GetOrCreateAsync(
            cacheKey,
            async () =>
            {
                var uri = search.StartsWith("kp") ? $"&kp={search[2..]}" : $"&imdb={search}";
                var response = await _httpService.Get<JObject>($"https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1{uri}", timeoutSeconds: 8);
                var data = response?.Value<JObject>("data");
                return (data?.Value<string>("original_name"), data?.Value<string>("name"));
            },
            TimeSpan.FromDays(1));

        return !string.IsNullOrWhiteSpace(cache.Item1) && !string.IsNullOrWhiteSpace(cache.Item2)
            ? (cache.Item1, cache.Item2)
            : (cache.Item1 ?? cache.Item2, altname);
    }

    protected string GetTrackersHash()
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

    protected bool IsTrackerSearchEnabled(TrackerType type)
    {
        return type switch
        {
            TrackerType.Rutracker => Config.RuTracker.EnableSearch,
            TrackerType.AnimeLayer => Config.AnimeLayer.EnableSearch,
            TrackerType.NNMClub => Config.NNMClub.EnableSearch,
            TrackerType.Rutor => Config.RuTor.EnableSearch,
            TrackerType.Aniliberty => Config.Aniliberty.EnableSearch,
            TrackerType.Kinozal => Config.Kinozal.EnableSearch,
            _ => true
        };
    }

    protected IEnumerable<TorrentDetails> ApplyFilters(IEnumerable<TorrentDetails> source, string? type, string? tracker, long relased, long quality, string? videotype, string? voice, long season)
    {
        var query = source;
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(t => t.Types?.Contains(type) == true);
        if (!string.IsNullOrWhiteSpace(tracker)) query = query.Where(t => t.TrackerName == tracker);
        if (relased > 0) query = query.Where(t => t.Relased == relased);
        if (quality > 0) query = query.Where(t => t.Quality == quality);
        if (!string.IsNullOrWhiteSpace(videotype)) query = query.Where(t => t.VideoType == videotype);
        if (!string.IsNullOrWhiteSpace(voice)) query = query.Where(t => t.Voices?.Contains(voice) == true);
        if (season > 0) query = query.Where(t => t.Seasons?.Contains((int)season) == true);
        return query;
    }

    protected IEnumerable<TorrentDetails> ApplySort(IEnumerable<TorrentDetails> source, string? sort)
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

    protected string BuildTrackerQuery(string? search, string? altname) => $"{search} {altname}".Trim();
}