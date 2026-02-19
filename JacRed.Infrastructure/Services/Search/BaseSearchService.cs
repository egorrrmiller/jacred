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
    private readonly HttpService _httpService;
    protected readonly ICacheService CacheService;
    protected readonly Config Config;

    protected BaseSearchService(Config config, HttpService httpService, ICacheService cacheService)
    {
        Config = config;
        _httpService = httpService;
        CacheService = cacheService;
    }

    protected bool IsTrackerSearchEnabled(TorrentDetails torrentDetails)
    {
        return Enum.Parse<TrackerType>(torrentDetails.TrackerName, true).IsSearchEnabled(Config);
    }

    protected IEnumerable<TorrentDetails> ApplyFilters(IEnumerable<TorrentDetails> source, string? type,
        string? tracker, long relased, long quality, string? videotype, string? voice, long season)
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

    protected string BuildTrackerQuery(string? search, string? altname)
    {
        return $"{search} {altname}".Trim();
    }
}