using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.AniLibria;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers.CRON.RIP;

[Route("/cron/anilibria/[action]")]
public class AniLibriaController : BaseController
{
	private static bool workParse;

	private readonly ITorrentRepository	_torrentRepository;

	public AniLibriaController(IMemoryCache memoryCache, ITorrentRepository torrentRepository) : base(memoryCache)
	{
		_torrentRepository = torrentRepository;
	}

	public async Task<string> Parse(int limit)
	{
		if (workParse)
		{
			return "work";
		}

		workParse = true;

		try
		{
			for (var after = 0; after <= limit; after++)
			{
				after = after + 40;

				var roots = await HttpClient.Get<List<RootObject>>(
					$"{AppInit.conf.Anilibria.rqHost()}/v2/getUpdates?limit=40&after={after - 40}&include=raw_torrent",
					IgnoreDeserializeObject: true, useproxy: AppInit.conf.Anilibria.useproxy);

				if (roots == null || roots.Count == 0)
				{
					continue;
				}

				foreach (var root in roots)
				{
					var torrents = new List<TorrentBaseDetails>();

					var createTime =
						new DateTime(1970, 1, 1, 0, 0,
							0, 0).AddSeconds(root.last_change > root.updated
							? root.last_change
							: root.updated);

					foreach (var torrent in root.torrents.list)
					{
						if (string.IsNullOrWhiteSpace(root.code)
							|| (480 >= torrent.quality.resolution
								&& string.IsNullOrWhiteSpace(torrent.quality
									.encoder)
								&& string.IsNullOrWhiteSpace(torrent.url)))
						{
							continue;
						}

						// Данные раздачи
						var url = $"anilibria.tv:{root.code}:{torrent.quality.resolution}:{torrent.quality.encoder}";

						var title =
							$"{root.names.ru} / {root.names.en} {root.season.year} (s{root.season.code}, e{torrent.series.@string}) [{torrent.quality.@string}]";

						#region Получаем/Обновляем магнет

						if (string.IsNullOrWhiteSpace(torrent.raw_base64_file))
						{
							continue;
						}

						var _t = Convert.FromBase64String(torrent.raw_base64_file);
						var magnet = BencodeTo.Magnet(_t);
						var sizeName = BencodeTo.SizeName(_t);

						if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName))
						{
							continue;
						}

						#endregion

						torrents.Add(new()
						{
							trackerName = "anilibria",
							types = new[]
							{
								"anime"
							},
							url = url,
							title = title,
							sid = torrent.seeders,
							pir = torrent.leechers,
							createTime = createTime,
							magnet = magnet,
							sizeName = sizeName,
							name = tParse.ReplaceBadNames(root.names.ru),
							originalname = tParse.ReplaceBadNames(root.names.en),
							relased = root.season.year
						});
					}

					await _torrentRepository.AddOrUpdateAsync(torrents);
				}

				roots = null;
			}
		}
		catch
		{
		}
		finally
		{
			workParse = false;
		}

		return "ok";
	}
}