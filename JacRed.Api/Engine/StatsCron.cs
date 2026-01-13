using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Api.Engine.Tracks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Newtonsoft.Json;

namespace JacRed.Api.Engine;

public class StatsCron
{
	private readonly IContentCatalog _contentCatalog;

	private readonly ITorrentRepository _torrentRepository;

	public StatsCron(IContentCatalog contentCatalog, ITorrentRepository torrentRepository)
	{
		_contentCatalog = contentCatalog;
		_torrentRepository = torrentRepository;
	}

	public async Task Run()
	{
		await Task.Delay(20_000);

		while (true)
		{
			if (AppInit.conf.timeStatsUpdate == -1)
			{
				await Task.Delay(TimeSpan.FromMinutes(1));

				continue;
			}

			await Task.Delay(TimeSpan.FromMinutes(AppInit.conf.timeStatsUpdate));

			try
			{
				var today = DateTime.Today - (DateTime.Now - DateTime.UtcNow);

				var stats =
					new Dictionary<string, (DateTime lastnewtor, int newtor, int update, int check, int alltorrents, int
						trkconfirm, int trkwait, int trerror)>();

				foreach (var item in await _contentCatalog.GetAllKeysAsync())
				{
					foreach (var t in (await _torrentRepository.GetCollectionAsync(item.Key, false))
								.Values)
					{
						if (string.IsNullOrEmpty(t.trackerName))
						{
							continue;
						}

						try
						{
							if (!stats.TryGetValue(t.trackerName, out var val))
							{
								stats.Add(t.trackerName, (t.createTime, 0, 0, 0, 0, 0, 0, 0));
							}

							var s = stats[t.trackerName];
							s.alltorrents = s.alltorrents + 1;

							if (t.createTime > s.lastnewtor)
							{
								s.lastnewtor = t.createTime;
							}

							if (t.createTime >= today)
							{
								s.newtor = s.newtor + 1;
							}

							if (t.updateTime >= today)
							{
								s.update = s.update + 1;
							}

							if (t.checkTime >= today)
							{
								s.check = s.check + 1;
							}

							if (!TracksDB.theBad(t.types) && !string.IsNullOrEmpty(t.magnet))
							{
								if (t.ffprobe_tryingdata >= 3)
								{
									s.trerror = s.trerror + 1;
								} else if (TracksDB.Get(t.magnet) != null || t.ffprobe != null)
								{
									s.trkconfirm = s.trkconfirm + 1;
								} else
								{
									s.trkwait = s.trkwait + 1;
								}
							}

							stats[t.trackerName] = s;
						}
						catch
						{
						}
					}
				}

				File.WriteAllText("Data/temp/stats.json", JsonConvert.SerializeObject(stats
					.OrderByDescending(i => i.Value.alltorrents)
					.Select(i => new
					{
						trackerName = i.Key,
						lastnewtor = i.Value.lastnewtor.ToString("dd.MM.yyyy"),
						i.Value.newtor,
						i.Value.update,
						i.Value.check,
						i.Value.alltorrents,
						tracks = new
						{
							wait = i.Value.trkwait,
							confirm = i.Value.trkconfirm,
							skip = i.Value.trerror
						}
					}), Formatting.Indented));
			}
			catch
			{
			}
		}
	}
}