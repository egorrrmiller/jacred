using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

/// <summary>
///     Периодически обновляет устаревшие торренты: если update_time старше порога, повторно ищет их на исходном трекере.
/// </summary>
public sealed class StaleTorrentRefreshService : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan Threshold = TimeSpan.FromHours(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly ILogger<StaleTorrentRefreshService> _logger;

    private readonly ITorrentRepository _torrentRepository;
    private readonly ITrackerSearchService _trackerSearchService;

    public StaleTorrentRefreshService(
        ITorrentRepository torrentRepository,
        ITrackerSearchService trackerSearchService,
        ILogger<StaleTorrentRefreshService> logger)
    {
        _torrentRepository = torrentRepository;
        _trackerSearchService = trackerSearchService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stale torrent refresh iteration failed");
            }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var stale = await _torrentRepository.GetStaleAsync(Threshold, BatchSize);
        if (stale.Count == 0)
            return;

        var grouped = stale
            .Select(t => (tracker: TryParseTracker(t.TrackerName), torrent: t))
            .Where(x => x.tracker.HasValue)
            .GroupBy(x => x.tracker!.Value);

        foreach (var group in grouped)
        {
            var tracker = group.Key;

            foreach (var (_, torrent) in group)
            {
                var query = torrent.Name;
                if (string.IsNullOrWhiteSpace(query))
                    query = torrent.OriginalName;
                if (string.IsNullOrWhiteSpace(query))
                    query = torrent.Title;
                if (string.IsNullOrWhiteSpace(query))
                    continue;

                try
                {
                    var refreshed = await _trackerSearchService.SearchAsync(
                        query,
                        [tracker]);

                    if (refreshed.Count == 0)
                        continue;

                    await _torrentRepository.AddOrUpdateAsync(refreshed);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to refresh torrent {Url}", torrent.Url);
                }
            }
        }
    }

    private static TrackerType? TryParseTracker(string trackerName)
    {
        return Enum.TryParse<TrackerType>(trackerName, true, out var t) ? t : null;
    }
}