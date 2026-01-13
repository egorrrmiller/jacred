using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

/// <summary>
/// Фоновый сервис для анализа торрентов через ffprobe.
/// Обрабатывает разные группы торрентов по возрасту и активности.
/// </summary>
public class TracksAnalysisService : BackgroundService
{
    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITracksDatabase _tracksDatabase;
    private readonly ILogger<TracksAnalysisService> _logger;

    public TracksAnalysisService(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        ITracksDatabase tracksDatabase,
        ILogger<TracksAnalysisService> logger)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _tracksDatabase = tracksDatabase;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(20_000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!AppInit.conf.tracks)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            await Task.WhenAll(
                ProcessGroup(1, TimeSpan.FromHours(1),  60),  // последние 24 часа
                ProcessGroup(2, TimeSpan.FromHours(10), 180), // до 1 месяца
                ProcessGroup(3, TimeSpan.FromDays(2),   180), // до 1 года
                ProcessGroup(4, TimeSpan.FromDays(2),   180), // старше 1 года
                ProcessGroup(5, TimeSpan.FromHours(1),  180)  // активные обновления
            );

            // Случайная пауза от 60 до 180 минут
            var delay = TimeSpan.FromMinutes(Random.Shared.Next(60, 181));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProcessGroup(int group, TimeSpan maxDuration, int delayMinutes)
    {
        if (AppInit.conf.tracksmod == 1 && (group is 3 or 4))
            return;

        var startTime = DateTime.Now;
        var torrents = await CollectTorrents(group);

        foreach (var t in torrents.OrderByDescending(t => t.UpdateTime))
        {
            if (CancellationToken.None.IsCancellationRequested)
                return;

            if (group == 2 && DateTime.Now > startTime.Add(maxDuration))
                break;
            if ((group is 3 or 4) && DateTime.Now > startTime.Add(maxDuration))
                break;
            if ((group is 3 or 4 or 5) && t.FfprobeTryCount >= 3)
                continue;

            if (_tracksDatabase.GetStreams(t.Magnet, t.Types).Count != 0)
            {
                try
                {
                    t.FfprobeTryCount++;
                    await _tracksDatabase.AddAsync(t.Magnet, t.Types);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze torrent: {InfoHash}", ExtractInfoHash(t.Magnet));
                }
            }
        }
    }

    private async Task<List<TorrentDetails>> CollectTorrents(int group)
    {
        var result = new List<TorrentDetails>();
        var now = DateTime.UtcNow;
        var db = await _contentCatalog.GetAllKeysAsync();

        foreach (var item in db)
        {
            var collection = await _torrentRepository.GetCollectionAsync(item.Key, false);

            foreach (var t in collection.Values.Where(IsValidForAnalysis))
            {
                var isMatch = group switch
                {
                    1 => t.CreateTime >= now.AddDays(-1),
                    2 => t.CreateTime < now.AddDays(-1) && t.CreateTime >= now.AddMonths(-1),
                    3 => t.CreateTime < now.AddMonths(-1) && t.CreateTime >= now.AddYears(-1),
                    4 => t.CreateTime < now.AddYears(-1),
                    5 => t.UpdateTime >= now.AddMonths(-1),
                    _ => false
                };

                if (isMatch && (group == 1 || (t.Sid > 0 && t.UpdateTime > DateTime.Today.AddDays(-20))))
                    result.Add(t);
            }
        }

        return result;
    }

    private bool IsValidForAnalysis(TorrentDetails t) =>
        !string.IsNullOrEmpty(t.Magnet) && t.Ffprobe == null && !_tracksDatabase.IsExcludedType(t.Types);

    private static string ExtractInfoHash(string magnet)
    {
        try
        {
            return MonoTorrent.MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
        }
        catch
        {
            return "unknown";
        }
    }
}