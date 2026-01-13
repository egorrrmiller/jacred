using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Sync.v2;
using JacRed.Core.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace JacRed.Api.Services;

public class TorrentSyncService : BackgroundService
{
    private readonly IContentCatalog _contentCatalog;
    private readonly HttpService _httpService;
    private readonly ILogger<TorrentSyncService> _logger;

    private readonly SyncState _syncState = new("lastsync.txt", "starsync.txt");
    private readonly ITorrentRepository _torrentRepository;

    public TorrentSyncService(
        ILogger<TorrentSyncService> logger,
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository, HttpService httpService)
    {
        _logger = logger;
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(20_000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                var useV2 = await IsSyncV2Enabled(stoppingToken);
                var syncMethod = useV2 ? SyncV2Loop : (Func<CancellationToken, Task>)SyncV1Loop;

                await syncMethod(stoppingToken);

                await _contentCatalog.SaveToFileAsync();
                await _syncState.SaveAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync: error");
                await TrySaveStateOnFailure();
            }

            var delay = TimeSpan.FromSeconds(Random.Shared.Next(60, 300) + Math.Max(20, AppInit.conf.timeSync) * 60);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<bool> IsSyncV2Enabled(CancellationToken ct)
    {
        try
        {
            var conf = await _httpService.Get<JObject>($"{AppInit.conf.syncapi}/sync/conf");
            return conf?.Value<bool>("fbd") == true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SyncV2Loop(CancellationToken ct)
    {
        var reset = true;
        var lastSave = DateTime.Now;

        while (!ct.IsCancellationRequested)
        {
            var url =
                $"{AppInit.conf.syncapi}/sync/fdb/torrents?time={_syncState.LastSync}&start={_syncState.StartSync}";
            var root = await _httpService.Get<RootObject>(url, timeoutSeconds: 300);

            if (root?.collections == null || root.collections.Count == 0)
            {
                if (reset)
                {
                    reset = false;
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                    continue;
                }

                break;
            }

            var torrents = new List<TorrentBaseDetails>();
            foreach (var collection in root.collections)
            foreach (var torrent in collection.Value.torrents.Values)
            {
                if (ShouldSkipTorrent(torrent)) continue;
                torrents.Add(torrent);
            }

            if (torrents.Count > 0)
                await _torrentRepository.AddOrUpdateAsync(torrents);

            // ✅ Безопасное получение последнего элемента
            var lastCollection = root.collections.Last();
            _syncState.LastSync = lastCollection.Value.fileTime;

            if (root.nextread)
            {
                if (DateTime.Now > lastSave.AddMinutes(5))
                {
                    lastSave = DateTime.Now;
                    await _contentCatalog.SaveToFileAsync();
                    await _syncState.SaveAsync(ct);
                }

                continue;
            }

            _syncState.StartSync = _syncState.LastSync;
            await _syncState.SaveAsync(ct);
            break;
        }
    }

    private async Task SyncV1Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var url = $"{AppInit.conf.syncapi}/sync/torrents?time={_syncState.LastSync}";
            var root = await _httpService.Get<Core.Models.Sync.v1.RootObject>(url, timeoutSeconds: 300);

            if (root?.torrents == null || root.torrents.Count == 0) break;

            var torrents = root.torrents.Select(t => t.value).ToList();
            await _torrentRepository.AddOrUpdateAsync(torrents);

            _syncState.LastSync = root.torrents.Last().value.UpdateTime.ToFileTimeUtc();

            if (root.take != root.torrents.Count) break;
        }
    }

    private bool ShouldSkipTorrent(TorrentBaseDetails torrent)
    {
        return (AppInit.conf.synctrackers != null && !string.IsNullOrEmpty(torrent.TrackerName) &&
                !AppInit.conf.synctrackers.Contains(torrent.TrackerName)) ||
               (!AppInit.conf.syncsport && torrent.Types?.Contains("sport") == true);
    }

    private async Task TrySaveStateOnFailure()
    {
        try
        {
            if (_syncState.LastSync > 0)
            {
                await _contentCatalog.SaveToFileAsync();
                await _syncState.SaveAsync();
            }
        }
        catch
        {
        }
    }
}

// Вспомогательный класс для управления состоянием синхронизации
internal class SyncState
{
    private readonly string _lastSyncFile;
    private readonly string _startSyncFile;

    public SyncState(string lastSyncFile, string startSyncFile)
    {
        _lastSyncFile = lastSyncFile;
        _startSyncFile = startSyncFile;

        Load();
    }

    public long LastSync { get; set; } = -1;
    public long StartSync { get; set; } = -1;

    private void Load()
    {
        if (File.Exists(_lastSyncFile)) LastSync = long.Parse(File.ReadAllText(_lastSyncFile));
        if (File.Exists(_startSyncFile)) StartSync = long.Parse(File.ReadAllText(_startSyncFile));
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(_lastSyncFile, LastSync.ToString(), ct);
        await File.WriteAllTextAsync(_startSyncFile, StartSync.ToString(), ct);
    }
}