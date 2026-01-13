using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace JacRed.Api.Services;

public class StatsCronService : BackgroundService
{
    private readonly IContentCatalog _contentCatalog;
    private readonly ILogger<StatsCronService> _logger;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITracksDatabase _tracksDatabase;

    public StatsCronService(
        ILogger<StatsCronService> logger,
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        ITracksDatabase tracksDatabase)
    {
        _logger = logger;
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _tracksDatabase = tracksDatabase;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(20_000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (AppInit.conf.timeStatsUpdate == -1)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            await Task.Delay(TimeSpan.FromMinutes(AppInit.conf.timeStatsUpdate), stoppingToken);

            try
            {
                var stats = await CollectStats();
                await SaveStats(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StatsCron: error");
            }
        }
    }

    private async Task<Dictionary<string, TrackerStats>> CollectStats()
    {
        var today = DateTime.Today - (DateTime.Now - DateTime.UtcNow);
        var stats = new Dictionary<string, TrackerStats>();

        var keys = await _contentCatalog.GetAllKeysAsync();
        foreach (var key in keys)
        {
            var torrents = await _torrentRepository.GetCollectionAsync(key.Key, false);
            foreach (var t in torrents.Values)
            {
                if (string.IsNullOrEmpty(t.TrackerName)) continue;

                var s = stats.GetValueOrDefault(t.TrackerName, new TrackerStats { LastNewTor = t.CreateTime });

                s.AllTorrents++;
                if (t.CreateTime > s.LastNewTor) s.LastNewTor = t.CreateTime;
                if (t.CreateTime >= today) s.NewTor++;
                if (t.UpdateTime >= today) s.Update++;
                if (t.CheckTime >= today) s.Check++;

                if (!_tracksDatabase.IsExcludedType(t.Types) && !string.IsNullOrEmpty(t.Magnet))
                {
                    var streams = _tracksDatabase.GetStreams(t.Magnet, t.Types);

                    if (t.FfprobeTryCount >= 3)
                        s.TrError++;
                    else if (streams != null || t.Ffprobe != null)
                        s.TrConfirm++;
                    else
                        s.TrWait++;
                }

                stats[t.TrackerName] = s;
            }
        }

        return stats;
    }

    private async Task SaveStats(Dictionary<string, TrackerStats> stats)
    {
        var result = stats
            .OrderByDescending(i => i.Value.AllTorrents)
            .Select(i => new
            {
                trackerName = i.Key,
                lastnewtor = i.Value.LastNewTor.ToString("dd.MM.yyyy"),
                i.Value.NewTor,
                i.Value.Update,
                i.Value.Check,
                i.Value.AllTorrents,
                tracks = new
                {
                    wait = i.Value.TrWait,
                    confirm = i.Value.TrConfirm,
                    skip = i.Value.TrError
                }
            });

        var json = JsonConvert.SerializeObject(result, Formatting.Indented);
        var dir = Path.GetDirectoryName("Data/temp/stats.json");

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync("Data/temp/stats.json", json, Encoding.UTF8);
    }

    private record TrackerStats
    {
        public DateTime LastNewTor { get; set; }
        public int NewTor { get; set; }
        public int Update { get; set; }
        public int Check { get; set; }
        public int AllTorrents { get; set; }
        public int TrWait { get; set; }
        public int TrConfirm { get; set; }
        public int TrError { get; set; }
    }
}