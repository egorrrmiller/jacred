using System.Collections.Concurrent;
using System.Diagnostics;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;
using Serilog;

namespace JacRed.Infrastructure.Services.Search;

public class RemoteSearchService : BaseSearchService, IRemoteSearchService
{
    private readonly ICacheService _cacheService;
    private readonly ILogger _logger;
    private readonly IReadOnlyDictionary<TrackerType, ITrackerSearch> _providers;
    
    public RemoteSearchService(IOptions<Config> config, HttpService httpService, ICacheService cacheService, ILogger logger,
        IEnumerable<ITrackerSearch> providers) : base(config.Value, httpService, cacheService)
    {
        _cacheService = cacheService;
        _logger = logger;
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

        return await SearchUncachedAsync(query, targetTrackers);
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

        _logger.Information("Search '{@Query}' on {@Trackers} trackers", query, trackers);

        await Parallel.ForEachAsync(trackers, options, async (tracker, _) =>
        {
            var sw = new Stopwatch();
            sw.Start();
            var res = await SearchTrackerSafeAsync(tracker, query);
            if (res.Count > 0)
                bag.Add(res);
            sw.Stop();
            _logger.Information("Tracker: {Tracker}; \tSW: {SW}ms", tracker, sw.ElapsedMilliseconds);
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
            _logger.Debug("Tracker search timeout for {Tracker}", tracker);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Tracker search failed for {Tracker}", tracker);
        }

        return [];
    }
}