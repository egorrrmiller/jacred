using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

public class TrackerSearchService : ITrackerSearchService
{
    private const int TrackerTimeoutSeconds = 7;
    private readonly ICacheService _cacheService;
    private readonly HashSet<string> _disabledTrackers;
    private readonly ILogger<TrackerSearchService> _logger;
    private readonly IReadOnlyDictionary<TrackerType, ITrackerSearchProvider> _providers;

    public TrackerSearchService(
        ICacheService cacheService,
        IEnumerable<ITrackerSearchProvider> providers,
        ILogger<TrackerSearchService> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
        _providers = providers.ToDictionary(p => p.Tracker, p => p);
        _disabledTrackers = new HashSet<string>(
            AppInit.conf.disable_trackers ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Возвращает список поддерживаемых трекеров.</summary>
    public IReadOnlyCollection<TrackerType> GetSupportedTrackers()
    {
        return _providers.Keys.OrderBy(t => t).ToArray();
    }

    /// <summary>Ищет на выбранных трекерах и кэширует результат.</summary>
    public async Task<IReadOnlyCollection<TorrentBaseDetails>> SearchAsync(
        string query,
        IReadOnlyCollection<TrackerType>? trackers = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<TorrentBaseDetails>();

        var targetTrackers = ResolveTrackers(trackers);
        if (targetTrackers.Count == 0)
            return Array.Empty<TorrentBaseDetails>();

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

    private async Task<IReadOnlyCollection<TorrentBaseDetails>> SearchUncachedAsync(
        string query,
        IReadOnlyCollection<TrackerType> trackers,
        CancellationToken cancellationToken)
    {
        var tasks = trackers.Select(tracker => SearchTrackerSafeAsync(tracker, query, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks);

        var merged = new List<TorrentBaseDetails>();
        foreach (var list in results)
        {
            if (list.Count > 0)
                merged.AddRange(list);
        }

        return merged;
    }

    private async Task<IReadOnlyCollection<TorrentBaseDetails>> SearchTrackerSafeAsync(
        TrackerType tracker,
        string query,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(tracker, out var provider))
            return Array.Empty<TorrentBaseDetails>();

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

        return Array.Empty<TorrentBaseDetails>();
    }
}
