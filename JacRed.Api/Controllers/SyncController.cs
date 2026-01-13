using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Api.Engine;
using JacRed.Api.Engine.Tracks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Sync.v1;
using JacRed.Core.Models.Sync.v2;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers;

public class SyncController : BaseController
{
	private static Dictionary<string, TorrentInfo> masterDbCache;

	private readonly ITorrentRepository _torrentRepository;
	private readonly IContentCatalog _contentCatalog;

	public SyncController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, IContentCatalog contentCatalog) : base(memoryCache)
	{
		_torrentRepository = torrentRepository;
		_contentCatalog = contentCatalog;
	}

	public async Task Configuration()
	{
		Console.WriteLine("SyncController load");

		var db = await _contentCatalog.GetAllKeysAsync();
		masterDbCache = db.OrderBy(i => i.Value.fileTime)
			.ToDictionary(k => k.Key, v => v.Value);

		ThreadPool.QueueUserWorkItem(async _ =>
		{
			while (true)
			{
				await Task.Delay(TimeSpan.FromMinutes(10));

				try
				{
					masterDbCache = db.OrderBy(i => i.Value.fileTime)
						.ToDictionary(k => k.Key, v => v.Value);
				}
				catch
				{
				}
			}
		});
	}

	[Route("/sync/conf")]
	public JsonResult SyncConf() => Json(new
	{
		fbd = true,
		spidr = true,
		version = 2
	});

	[Route("/sync/fdb")]
	public async Task<ActionResult> FdbKey(string key)
	{
		if (!AppInit.conf.opensync)
		{
			return Content("[]", "application/json; charset=utf-8");
		}

		var db = await _contentCatalog.GetAllKeysAsync();

		return Json(db.Where(i => i.Key.Contains(key))
			.Take(20)
			.Select(async i => new
			{
				i.Key,
				i.Value.updateTime,
				i.Value.fileTime,
				path = $"Data/fdb/{HashTo.Md5(i.Key).Substring(0, 2)}/{HashTo.Md5(i.Key).Substring(2)}",
				value = await _torrentRepository.GetCollectionAsync(i.Key,  false)
			})
			.ToArray());
	}

	[Route("/sync/fdb/torrents")]
	public async Task<ActionResult> FdbTorrents(long time, long start = -1, bool spidr = false)
	{
		if (!AppInit.conf.opensync || time == 0)
		{
			return Json(new
			{
				nextread = false,
				collections = new List<Collection>()
			});
		}

		var nextread = false;

		int take = 2_000,
			countread = 0;

		var collections = new List<Collection>(take);

		foreach (var item in masterDbCache.Where(i => i.Value.fileTime > time))
		{
			var torrent = new Dictionary<string, TorrentDetails>();

			foreach (var t in await _torrentRepository.GetCollectionAsync(item.Key, false))
			{
				if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.Value.trackerName))
				{
					continue;
				}

				if (spidr || (start != -1 && start > t.Value.updateTime.ToFileTimeUtc()))
				{
					torrent.TryAdd(t.Key,
						new()
						{
							sid = t.Value.sid,
							pir = t.Value.pir,
							url = t.Value.url
						});

					continue;
				}

				if (t.Value.ffprobe == null || t.Value.languages == null)
				{
					var streams = TracksDB.Get(t.Value.magnet, t.Value.types, true);

					if (streams != null)
					{
						var _t = (TorrentDetails) t.Value.Clone();
						_t.ffprobe = streams;
						_t.languages = TracksDB.Languages(_t, streams);
						torrent.TryAdd(t.Key, _t);
					} else
					{
						torrent.TryAdd(t.Key, t.Value);
					}
				} else
				{
					torrent.TryAdd(t.Key, t.Value);
				}
			}

			if (torrent.Count > 0)
			{
				countread = countread + torrent.Count;

				collections.Add(new()
				{
					Key = item.Key,
					Value = new()
					{
						time = item.Value.updateTime,
						fileTime = item.Value.fileTime,
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

		return Json(new
		{
			nextread,
			countread,
			take,
			collections
		});
	}

	[Route("/sync/torrents")]
	public async Task<JsonResult> Torrents(long time)
	{
		if (!AppInit.conf.opensync_v1 || time == 0)
		{
			return Json(new List<string>());
		}

		var take = 2_000;
		var torrents = new List<Torrent>(take + 1);

		foreach (var item in masterDbCache.Where(i => i.Value.fileTime > time))
		{
			foreach (var torrent in await _torrentRepository.GetCollectionAsync(item.Key, false))
			{
				if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(torrent.Value.trackerName))
				{
					continue;
				}

				var _t = (TorrentDetails) torrent.Value.Clone();
				_t.updateTime = item.Value.updateTime;

				var streams = TracksDB.Get(_t.magnet, _t.types, true);

				if (streams != null)
				{
					_t.ffprobe = streams;
					_t.languages = TracksDB.Languages(_t, streams);
				}

				torrents.Add(new()
				{
					key = torrent.Key,
					value = _t
				});
			}

			if (torrents.Count > take)
			{
				take = torrents.Count;

				break;
			}
		}

		return Json(new
		{
			take,
			torrents
		});
	}
}