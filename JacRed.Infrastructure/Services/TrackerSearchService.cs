using System.Collections.Concurrent;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services;

public class TrackerSearchService : ITrackerSearchService
{
    private readonly ICacheService _cacheService;
    private readonly Config _config;
    private readonly ILogger<TrackerSearchService> _logger;
    private readonly IReadOnlyDictionary<TrackerType, ITrackerSearch> _providers;

    public TrackerSearchService(
        ICacheService cacheService,
        IEnumerable<ITrackerSearch> providers,
        ILogger<TrackerSearchService> logger, IOptions<Config> config)
    {
        _cacheService = cacheService;
        _logger = logger;
        _config = config.Value;
        _providers = providers.ToDictionary(p => p.Tracker, p => p);
    }

    public IReadOnlyCollection<TrackerType> GetSupportedTrackers()
    {
        return _providers.Keys.OrderBy(t => t).ToArray();
    }

    public async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        IReadOnlyCollection<TrackerType>? trackers = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var targetTrackers = ResolveTrackers(trackers);
        if (targetTrackers.Count == 0)
            return [];

        var normalizedQuery = StringConvert.SearchName(query) ?? query.Trim();
        var trackerKey = string.Join(",", targetTrackers.OrderBy(t => t).Select(t => t.ToString()));
        var cacheKey = CacheKeyBuilder.Build("tracker-search", normalizedQuery, trackerKey);

        return await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => SearchUncachedAsync(query, targetTrackers),
            TimeSpan.FromMinutes(5));
    }

    private IReadOnlyCollection<TrackerType> ResolveTrackers(IReadOnlyCollection<TrackerType>? trackers)
    {
        var candidates = trackers == null || trackers.Count == 0
            ? _providers.Keys
            : trackers.Where(t => _providers.ContainsKey(t));

        return candidates.Distinct().ToArray();
    }

    private async Task<IReadOnlyCollection<TorrentDetails>> SearchUncachedAsync(
        string query,
        IReadOnlyCollection<TrackerType> trackers)
    {
        var bag = new ConcurrentBag<IReadOnlyCollection<TorrentDetails>>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(trackers, options, async (tracker, _) =>
        {
            var res = await SearchTrackerSafeAsync(tracker, query);
            if (res.Count > 0)
                bag.Add(res);
        });

        var merged = new List<TorrentDetails>();
        foreach (var list in bag)
            if (list.Count > 0)
                merged.AddRange(list);

        return merged;
    }

    private async Task<IReadOnlyCollection<TorrentDetails>> SearchTrackerSafeAsync(
        TrackerType tracker,
        string query)
    {
        if (!_providers.TryGetValue(tracker, out var provider))
            return [];

        try
        {
            return await provider.SearchAsync(query);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Tracker search timeout for {Tracker}", tracker);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tracker search failed for {Tracker}", tracker);
        }

        return [];
    }
}