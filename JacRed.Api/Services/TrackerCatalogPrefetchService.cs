using System;
using System.Collections.Generic;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Api.Services;

public sealed class TrackerCatalogPrefetchService : BackgroundService
{
    private const int DefaultIntervalMinutes = 15;

    private readonly IReadOnlyCollection<ITrackerCatalogProvider> _providers;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ILogger<TrackerCatalogPrefetchService> _logger;
    private readonly HashSet<string> _disabledTrackers;

    public TrackerCatalogPrefetchService(
        IEnumerable<ITrackerCatalogProvider> providers,
        ITorrentRepository torrentRepository,
        ILogger<TrackerCatalogPrefetchService> logger)
    {
        _providers = (providers ?? Array.Empty<ITrackerCatalogProvider>())
            .Where(p => p != null)
            .ToArray();
        _torrentRepository = torrentRepository;
        _logger = logger;
        _disabledTrackers = new HashSet<string>(
            AppInit.conf.disable_trackers ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
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
                _logger.LogWarning(ex, "Tracker catalog prefetch failed");
            }
        }
    }

    private async Task PrefetchOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            if (provider == null)
                continue;

            if (_disabledTrackers.Contains(provider.TrackerName))
                continue;

            try
            {
                var items = await provider.FetchCatalogAsync(cancellationToken) ?? Array.Empty<TorrentDetails>();
                if (items.Count == 0)
                    continue;

                if (provider is ITrackerCatalogEnricher enricher)
                {
                    await _torrentRepository.AddOrUpdateAsync(
                        items,
                        (TorrentDetails torrent, IReadOnlyDictionary<string, TorrentDetails> existing) =>
                            enricher.TryEnrichAsync(torrent, existing, cancellationToken));
                }
                else
                {
                    await _torrentRepository.AddOrUpdateAsync(items);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog prefetch failed for tracker {Tracker}", provider?.TrackerName);
            }
        }
    }
}
