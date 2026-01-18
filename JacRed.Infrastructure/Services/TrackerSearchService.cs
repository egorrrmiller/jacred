using JacRed.Core;
using System.Collections.Concurrent;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Services;

public class TrackerSearchService : ITrackerSearchService
{
    private const int TrackerTimeoutSeconds = 7;
    private readonly ICacheService _cacheService;
    private readonly HashSet<string> _disabledTrackers;
    private readonly ILogger<TrackerSearchService> _logger;
    private readonly IReadOnlyDictionary<TrackerType, ITrackerSearch> _providers;

    public TrackerSearchService(
        ICacheService cacheService,
        IEnumerable<ITrackerSearch> providers,
        ILogger<TrackerSearchService> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
        _providers = providers.ToDictionary(p => p.Tracker, p => p);
        _disabledTrackers = new HashSet<string>(
            AppInit.conf.disable_trackers ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<TrackerType> GetSupportedTrackers()
    {
        return _providers.Keys.OrderBy(t => t).ToArray();
    }

    public async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        IReadOnlyCollection<TrackerType>? trackers = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<TorrentDetails>();

        var targetTrackers = ResolveTrackers(trackers);
        if (targetTrackers.Count == 0)
            return Array.Empty<TorrentDetails>();

        var normalizedQuery = StringConvert.SearchName(query) ?? query.Trim();
        var trackerKey = string.Join(",", targetTrackers.OrderBy(t => t).Select(t => t.ToString()));
        var cacheKey = CacheKeyBuilder.Build("tracker-search", normalizedQuery, trackerKey);

        return await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => SearchUncachedAsync(query, targetTrackers, cancellationToken),
            TimeSpan.FromMinutes(5));
    }

    private IReadOnlyCollection<TrackerType> ResolveTrackers(IReadOnlyCollection<TrackerType>? trackers)
    {
        if (trackers == null || trackers.Count == 0)
            return _providers.Values
                .Where(p => !_disabledTrackers.Contains(p.TrackerName))
                .Select(p => p.Tracker)
                .ToArray();

        return trackers
            .Where(t => _providers.ContainsKey(t))
            .Where(t => !_disabledTrackers.Contains(_providers[t].TrackerName))
            .Distinct()
            .ToArray();
    }

    private async Task<IReadOnlyCollection<TorrentDetails>> SearchUncachedAsync(
        string query,
        IReadOnlyCollection<TrackerType> trackers,
        CancellationToken cancellationToken)
    {
        var bag = new ConcurrentBag<IReadOnlyCollection<TorrentDetails>>();

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(trackers, options, async (tracker, ct) =>
        {
            var res = await SearchTrackerSafeAsync(tracker, query, ct);
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
        string query,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(tracker, out var provider))
            return Array.Empty<TorrentDetails>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(TrackerTimeoutSeconds));

        try
        {
            return await provider.SearchAsync(query, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Tracker search timeout for {Tracker}", tracker);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tracker search failed for {Tracker}", tracker);
        }

        return Array.Empty<TorrentDetails>();
    }
}