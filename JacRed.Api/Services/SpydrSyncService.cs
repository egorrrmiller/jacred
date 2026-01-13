using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Sync.v2;
using JacRed.Core.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace JacRed.Api.Services;

public class SpidrSyncService : BackgroundService
{
    private readonly HttpService _httpService;
    private readonly ILogger<SpidrSyncService> _logger;
    private readonly ITorrentRepository _torrentRepository;

    public SpidrSyncService(
        ILogger<SpidrSyncService> logger,
        ITorrentRepository torrentRepository, HttpService httpService)
    {
        _logger = logger;
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(AppInit.conf.syncapi) || !AppInit.conf.syncspidr)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            if (!await IsSpidrEnabled(stoppingToken)) goto delay;

            _logger.LogInformation("Sync_spidr: start");

            var lastSync = -1L;
            while (!stoppingToken.IsCancellationRequested)
            {
                var url = $"{AppInit.conf.syncapi}/sync/fdb/torrents?time={lastSync}&spidr=true";
                var root = await _httpService.Get<RootObject>(url, timeoutSeconds: 300);

                if (root?.collections == null || root.collections.Count == 0) break;

                foreach (var collection in root.collections)
                    await _torrentRepository.AddOrUpdateAsync(collection.Value.torrents.Values);

                lastSync = root.collections.Last().Value.fileTime;

                if (!root.nextread) break;
            }

            _logger.LogInformation("Sync_spidr: end");

            delay:
            var delay = TimeSpan.FromMinutes(120 + Random.Shared.Next(1, 5));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<bool> IsSpidrEnabled(CancellationToken ct)
    {
        try
        {
            var conf = await _httpService.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
            return conf?.Value<bool>("spidr") == true;
        }
        catch
        {
            return false;
        }
    }
}