using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services;

public class TrackersCronService : BackgroundService
{
    private readonly IContentCatalog _contentCatalog;
    private readonly ILogger<TrackersCronService> _logger;
    private readonly ITorrentRepository _torrentRepository;

    public TrackersCronService(
        ILogger<TrackersCronService> logger,
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository)
    {
        _logger = logger;
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(20_000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            try
            {
                var trackers = await CollectTrackers(stoppingToken);
                await SaveTrackers(trackers, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrackersCron: error");
            }
        }
    }

    private async Task<HashSet<string>> CollectTrackers(CancellationToken ct)
    {
        var trackers = new HashSet<string>();
        var keys = _contentCatalog.GetAllKeys();

        foreach (var key in keys)
        {
            var torrents = await _torrentRepository.GetCollectionAsync(key.Key);
            foreach (var t in torrents.Values)
            {
                if (string.IsNullOrEmpty(t.Magnet)) continue;

                try
                {
                    foreach (Match tr in Regex.Matches(t.Magnet, "tr=([^&]+)"))
                    {
                        var tracker = HttpUtility.UrlDecode(tr.Groups[1].Value.Split("?")[0]).Trim().ToLower();
                        if (!IsValidTracker(tracker)) continue;
                        if (await CheckTracker(tracker, ct)) trackers.Add(tracker);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing magnet: {Magnet}", t.Magnet);
                }
            }
        }

        return trackers;
    }

    private bool IsValidTracker(string tracker)
    {
        return !string.IsNullOrWhiteSpace(tracker) &&
               !tracker.Contains("[") &&
               !tracker.Contains(" ") &&
               !tracker.Contains("torrentsmd.eu") &&
               tracker.Replace("://", "").Contains(":") &&
               !Regex.IsMatch(tracker, "[^/]+/[^/]+/announce");
    }

    private async Task<bool> CheckTracker(string tracker, CancellationToken ct)
    {
        return tracker.StartsWith("http")
            ? await CheckHttpTracker(tracker, ct)
            : tracker.StartsWith("udp://") && await CheckUdpTracker(tracker, ct);
    }

    private async Task<bool> CheckHttpTracker(string tracker, CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
            await client.GetAsync(tracker, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }


    private async Task<bool> CheckUdpTracker(string tracker, CancellationToken ct)
    {
        try
        {
            tracker = tracker.Replace("udp://", "");
            var host = tracker.Split(':')[0].Split('/')[0];
            var port = tracker.Contains(":")
                ? int.Parse(tracker.Split(':')[1].Split('/')[0])
                : 6969;

            using var client = new UdpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(7000); // 7 секунд таймаут

            var message = Encoding.UTF8.GetBytes("GET /announce HTTP/1.1\r\nHost: " + host + "\r\n\r\n");
            var buffer = new ReadOnlyMemory<byte>(message);

            await client.SendAsync(buffer, host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SaveTrackers(HashSet<string> trackers, CancellationToken ct)
    {
        await File.WriteAllLinesAsync("wwwroot/trackers.txt", trackers, Encoding.UTF8, ct);
    }
}