using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

public sealed class PopularSearchPrefetchService : BackgroundService
{
    private const int DefaultIntervalMinutes = 10;
    private const int DefaultTopQueries = 50;
    private const int PerQueryDelayMs = 200;

    private readonly IQueryStatsService _queryStatsService;
    private readonly ITrackerSearchService _trackerSearchService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ILogger<PopularSearchPrefetchService> _logger;

    public PopularSearchPrefetchService(
        IQueryStatsService queryStatsService,
        ITrackerSearchService trackerSearchService,
        ITorrentRepository torrentRepository,
        ILogger<PopularSearchPrefetchService> logger)
    {
        _queryStatsService = queryStatsService;
        _trackerSearchService = trackerSearchService;
        _torrentRepository = torrentRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(DefaultIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PrefetchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Popular search prefetch failed");
            }
        }
    }

    private async Task PrefetchOnceAsync(CancellationToken cancellationToken)
    {
        var queries = await _queryStatsService.GetTopQueriesAsync(DefaultTopQueries, cancellationToken);
        if (queries.Count == 0)
            return;

        var trackers = _trackerSearchService.GetSupportedTrackers();
        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query))
                continue;

            var fetched = await _trackerSearchService.SearchAsync(query, trackers, cancellationToken);
            if (fetched.Count > 0)
                await _torrentRepository.AddOrUpdateAsync(fetched);

            await Task.Delay(PerQueryDelayMs, cancellationToken);
        }
    }
}
