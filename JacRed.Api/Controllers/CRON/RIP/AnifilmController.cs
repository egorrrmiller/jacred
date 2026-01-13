using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers.CRON.RIP;

[Route("/cron/anifilm/[action]")]
public class AnifilmController : BaseController
{
	#region parsePage

	private readonly ITorrentRepository	_torrentRepository;

	private async Task<bool> parsePage(string cat, int page, DateTime createTime)
	{
		var html = await HttpClient.Get($"{AppInit.conf.Anifilm.rqHost()}/releases/page/{page}?category={cat}",
			useproxy: AppInit.conf.Anifilm.useproxy);

		if (html == null || !html.Contains("id=\"ui-components\""))
		{
			return false;
		}

		var torrents = new List<TorrentBaseDetails>();

		foreach (var row in MediaNameUtils.Normalize(html)
					.Split("class=\"releases__item\"")
					.Skip(1))
		{
			#region Локальный метод - Match

			string Match(string pattern, int index = 1)
			{
				var res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row)
					.Groups[index]
					.Value.Trim());

				res = Regex.Replace(res, "[\n\r\t ]+", " ");

				return res.Trim();
			}

			#endregion

			if (string.IsNullOrWhiteSpace(row))
			{
				continue;
			}

			#region Данные раздачи

			var url = Match("<a href=\"/(releases/[^\"]+)\"");
			var name = Match("<a class=\"releases__title-russian\" [^>]+>([^<]+)</a>");
			var originalname = Match("<span class=\"releases__title-original\">([^<]+)</span>");
			var episodes = Match("([0-9]+(-[0-9]+)?) из [0-9]+ эп.,");

			if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(originalname))
			{
				continue;
			}

			if (cat != "movies" && string.IsNullOrWhiteSpace(episodes))
			{
				continue;
			}

			url = $"{AppInit.conf.Anifilm.host}/{url}";
			var title = $"{name} / {originalname}";

			if (!string.IsNullOrWhiteSpace(episodes))
			{
				title += $" ({episodes})";
			}

			name = name.Split("(")[0]
				.Trim();

			#endregion

			// Год выхода
			if (!int.TryParse(Match("<a href=\"/releases/releases/[^\"]+\">([0-9]{4})</a> г\\."), out var relased) || relased == 0)
			{
				continue;
			}

			torrents.Add(new TorrentDetails
			{
				TrackerName = "anifilm",
				Types = new[]
				{
					"anime"
				},
				Url = url,
				Title = title,
				Sid = 1,
				CreateTime = createTime,
				Name = name,
				OriginalName = originalname,
				Relased = relased
			});
		}

		await _torrentRepository.AddOrUpdateAsync(torrents, async (t, db) =>
		{
			if (db.TryGetValue(t.Url, out var _tcache) && _tcache.Title.Replace(" [1080p]", "") == t.Title)
			{
				return true;
			}

			var fullNews = await HttpClient.Get(t.Url, useproxy: AppInit.conf.Anifilm.useproxy);

			if (fullNews != null)
			{
				string tid = null;
				var title = t.Title;
				var releasetorrents = fullNews.Split("<li class=\"release__torrents-item\">");

				var _rnews = releasetorrents.FirstOrDefault(i =>
					i.Contains("href=\"/releases/download-torrent/") && i.Contains(" 1080p "));

				if (!string.IsNullOrWhiteSpace(_rnews))
				{
					tid = Regex.Match(_rnews, "href=\"/(releases/download-torrent/[0-9]+)\">скачать</a>")
						.Groups[1]
						.Value;

					if (!string.IsNullOrWhiteSpace(tid) && !title.Contains(" [1080p]"))
					{
						title += " [1080p]";
					}
				}

				if (string.IsNullOrWhiteSpace(tid))
				{
					tid = Regex.Match(fullNews, "href=\"/(releases/download-torrent/[0-9]+)\">скачать</a>")
						.Groups[1]
						.Value;
				}

				if (!string.IsNullOrWhiteSpace(tid))
				{
					var torrent = await HttpClient.Download($"{AppInit.conf.Anifilm.host}/{tid}", referer: t.Url,
						useproxy: AppInit.conf.Anifilm.useproxy);

					var magnet = BencodeTo.Magnet(torrent);

					if (!string.IsNullOrWhiteSpace(magnet))
					{
						t.Title = title;
						t.Magnet = magnet;
						t.SizeName = BencodeTo.SizeName(torrent);

						return true;
					}
				}
			}

			return false;
		});

		return torrents.Count > 0;
	}

	#endregion

	#region Parse

	private static bool workParse;

	public AnifilmController(IMemoryCache memoryCache, ITorrentRepository torrentRepository) : base(memoryCache)
	{
		_torrentRepository = torrentRepository;
	}

	public async Task<string> Parse(bool fullparse)
	{
		if (workParse)
		{
			return "work";
		}

		workParse = true;

		var log = "";

		try
		{
			if (fullparse)
			{
				for (var page = 1; page <= 70; page++)
				{
					await Task.Delay(AppInit.conf.Anifilm.parseDelay);
					await parsePage("serials", page, DateTime.Today.AddDays(-(2 * page)));
				}

				for (var page = 1; page <= 32; page++)
				{
					await Task.Delay(AppInit.conf.Anifilm.parseDelay);
					await parsePage("ova", page, DateTime.Today.AddDays(-(2 * page)));
				}

				for (var page = 1; page <= 2; page++)
				{
					await Task.Delay(AppInit.conf.Anifilm.parseDelay);
					await parsePage("ona", page, DateTime.Today.AddDays(-(2 * page)));
				}

				for (var page = 1; page <= 17; page++)
				{
					await Task.Delay(AppInit.conf.Anifilm.parseDelay);
					await parsePage("movies", page, DateTime.Today.AddDays(-(2 * page)));
				}
			} else
			{
				foreach (var cat in new List<string>
						{
							"serials",
							"ova",
							"ona",
							"movies"
						})
				{
					await parsePage(cat, 1, DateTime.UtcNow);
					log += $"{cat} - 1\n";
				}
			}
		}
		catch
		{
		}
		finally
		{
			workParse = false;
		}

		return string.IsNullOrWhiteSpace(log)
			? "ok"
			: log;
	}

	#endregion
}