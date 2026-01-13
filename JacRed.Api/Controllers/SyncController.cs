using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Sync.v1;
using JacRed.Core.Models.Sync.v2;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Api.Controllers;

public class SyncController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, TorrentInfo> MasterDbCache = new();

    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITracksDatabase _tracksDatabase;

    public SyncController(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        ITracksDatabase tracksDatabase)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _tracksDatabase = tracksDatabase;
    }

    [Route("/sync/conf")]
    public IActionResult SyncConf() => Ok(new { fbd = true, spidr = true, version = 2 });

    [Route("/sync/fdb")]
    public async Task<IActionResult> FdbKey(string key)
    {
        if (!AppInit.conf.opensync)
            return Content("[]", "application/json; charset=utf-8");

        var db = await _contentCatalog.GetAllKeysAsync();
        var results = db
            .Where(i => i.Key.Contains(key))
            .Take(20)
            .Select(async i => new
            {
                i.Key,
                i.Value.updateTime,
                i.Value.fileTime,
                path = $"Data/fdb/{HashTo.Md5(i.Key).Substring(0, 2)}/{HashTo.Md5(i.Key).Substring(2)}",
                value = await _torrentRepository.GetCollectionAsync(i.Key, false)
            })
            .Select(t => t.Result)
            .ToArray();

        return Ok(results);
    }

    [Route("/sync/fdb/torrents")]
    public async Task<IActionResult> FdbTorrents(long time, long start = -1, bool spidr = false)
    {
        if (!AppInit.conf.opensync || time == 0)
            return Ok(new { nextread = false, collections = new List<Collection>() });

        var nextread = false;
        var take = 2_000;
        var countread = 0;
        var collections = new List<Collection>();

        foreach (var kvp in MasterDbCache.Where(i => i.Value.fileTime > time))
        {
            var key = kvp.Key;
            var item = kvp.Value;

            var torrent = new Dictionary<string, TorrentDetails>();

            foreach (var t in (await _torrentRepository.GetCollectionAsync(key, false)).Values)
            {
                if (AppInit.conf.disable_trackers?.Contains(t.TrackerName) == true) continue;

                if (spidr || (start != -1 && start > t.UpdateTime.ToFileTimeUtc()))
                {
                    torrent[t.Url] = new() { Sid = t.Sid, Pir = t.Pir, Url = t.Url };
                    continue;
                }

                if (t.Ffprobe.Count == 0 || t.Languages.Count == 0)
                {
                    var streams = _tracksDatabase.GetStreams(t.Magnet, t.Types);
                    if (streams != null)
                    {
                        var cloned = (TorrentDetails)t.Clone();
                        cloned.Ffprobe = streams;
                        cloned.Languages = _tracksDatabase.GetLanguages(cloned, streams);
                        torrent[t.Url] = cloned;
                    }
                    else
                    {
                        torrent[t.Url] = t;
                    }
                }
                else
                {
                    torrent[t.Url] = t;
                }
            }

            if (torrent.Count > 0)
            {
                countread += torrent.Count;
                collections.Add(new Collection
                {
                    Key = key, // ← теперь правильно
                    Value = new()
                    {
                        time = item.updateTime,
                        fileTime = item.fileTime,
                        torrents = torrent
                    }
                });
            }

            if (countread > take)
            {
                nextread = true;
                break;
            }
        }

        return Ok(new { nextread, countread, take, collections });
    }

    [Route("/sync/torrents")]
    public async Task<IActionResult> SyncTorrents(long time)
    {
        if (!AppInit.conf.opensync_v1 || time == 0)
            return Ok(new { torrents = new List<object>() });

        var take = 2_000;
        var torrents = new List<Torrent>();

        foreach (var kvp in MasterDbCache.Where(i => i.Value.fileTime > time))
        {
            var key = kvp.Key;
            var item = kvp.Value;

            foreach (var t in (await _torrentRepository.GetCollectionAsync(key, false)).Values)
            {
                if (AppInit.conf.disable_trackers?.Contains(t.TrackerName) == true) continue;

                var cloned = (TorrentDetails) t.Clone();
                cloned.UpdateTime = item.updateTime;

                var streams = _tracksDatabase.GetStreams(cloned.Magnet, cloned.Types);
                if (streams != null)
                {
                    cloned.Ffprobe = streams;
                    cloned.Languages = _tracksDatabase.GetLanguages(cloned, streams);
                }

                torrents.Add(new Torrent { key = t.Url, value = cloned });

                if (torrents.Count > take) break;
            }

            if (torrents.Count > take) break;
        }

        return Ok(new { take = torrents.Count, torrents });
    }
}