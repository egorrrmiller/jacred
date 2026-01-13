using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers.CRON;

using HttpClient = Core.Utils.HttpClient;

[Route("/cron/baibako/[action]")]
public class BaibakoController : BaseController
{
	private readonly ITorrentRepository _torrentRepository;

	#region parsePage

	private async Task<bool> parsePage(int page)
	{
		var html = await HttpClient.Get($"{AppInit.conf.Baibako.host}/browse.php?page={page}",
			Encoding.GetEncoding(1251), Cookie(MemoryCache));

		if (html == null || !html.Contains("id=\"navtop\""))
		{
			return false;
		}

		var torrents = new List<BaibakoDetails>();

		foreach (var row in tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html.Replace("&nbsp;", "")))
					.Split("<tr")
					.Skip(1))
		{
			#region Локальный метод - Match

			string Match(string pattern, int index = 1)
			{
				var res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row)
					.Groups[index]
					.Value.Trim();

				res = Regex.Replace(res, "[\n\r\t ]+", " ");

				return res.Trim();
			}

			#endregion

			if (string.IsNullOrWhiteSpace(row))
			{
				continue;
			}

			// Дата создания
			var createTime = tParse.ParseCreateTime(Match("<small>Загружена: ([0-9]+ [^ ]+ [0-9]{4}) в [^<]+</small>"),
				"dd.MM.yyyy");

			if (createTime == default)
			{
				if (page != 0)
				{
					continue;
				}

				createTime = DateTime.UtcNow;
			}

			#region Данные раздачи

			var gurl = Regex.Match(row, "<a href=\"/?(details.php\\?id=[0-9]+)[^\"]+\">([^<]+)</a>")
				.Groups;

			var url = gurl[1].Value;
			var title = gurl[2].Value;

			title = title.Replace("(Обновляемая)", "")
				.Replace("(Золото)", "")
				.Replace("(Оновлюється)", "");

			title = Regex.Replace(title, "/( +| )?$", "")
				.Trim();

			if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || !Regex.IsMatch(title, "(1080p|720p)"))
			{
				continue;
			}

			url = $"{AppInit.conf.Baibako.host}/{url}";

			#endregion

			#region name / originalname

			string name = null,
				originalname = null;

			// 9-1-1 /9-1-1 /s04e01-13 /WEBRip XviD
			var g = Regex.Match(title, "([^/\\(]+)[^/]+/([^/\\(]+)")
				.Groups;

			if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
			{
				name = g[1]
					.Value.Trim();

				originalname = g[2]
					.Value.Trim();
			}

			#endregion

			if (string.IsNullOrWhiteSpace(name))
			{
				name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0]
					.Trim();
			}

			if (!string.IsNullOrWhiteSpace(name))
			{
				var download = Match("href=\"/?(download.php\\?id=([0-9]+))\"");

				if (string.IsNullOrWhiteSpace(download))
				{
					continue;
				}

				torrents.Add(new()
				{
					trackerName = "baibako",
					types = new[]
					{
						"serial"
					},
					url = url,
					title = title,
					sid = 1,
					createTime = createTime,
					name = name,
					originalname = originalname,
					downloadUri = $"{AppInit.conf.Baibako.host}/{download}"
				});
			}
		}

		await _torrentRepository.AddOrUpdateAsync(torrents, async (t, db) =>
		{
			if (db.TryGetValue(t.url, out var _tcache) && _tcache.title == t.title)
			{
				return true;
			}

			var torrent = await HttpClient.Download(t.downloadUri, Cookie(MemoryCache),
				$"{AppInit.conf.Baibako.host}/browse.php");

			var magnet = BencodeTo.Magnet(torrent);
			var sizeName = BencodeTo.SizeName(torrent);

			if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
			{
				t.magnet = magnet;
				t.sizeName = sizeName;

				return true;
			}

			return false;
		});

		return torrents.Count > 0;
	}

	#endregion

	#region TakeLogin

	public BaibakoController(IMemoryCache memoryCache, ITorrentRepository torrentRepository) : base(memoryCache) =>
		_torrentRepository = torrentRepository;

	private static string Cookie(IMemoryCache memoryCache)
	{
		if (memoryCache.TryGetValue("baibako:cookie", out string cookie))
		{
			return cookie;
		}

		return null;
	}

	public async Task<bool> TakeLogin()
	{
		try
		{
			var clientHandler = new HttpClientHandler
			{
				AllowAutoRedirect = false
			};

			using (var client = new System.Net.Http.HttpClient(clientHandler))
			{
				client.MaxResponseContentBufferSize = 2000000; // 2MB

				client.DefaultRequestHeaders.Add("user-agent",
					"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

				var postParams = new Dictionary<string, string>
				{
					{
						"username", AppInit.conf.Baibako.login.u
					},
					{
						"password", AppInit.conf.Baibako.login.p
					}
				};

				using (var postContent = new FormUrlEncodedContent(postParams))
				{
					using (var response =
							await client.PostAsync($"{AppInit.conf.Baibako.host}/takelogin.php", postContent))
					{
						if (response.Headers.TryGetValues("Set-Cookie", out var cook))
						{
							string sessid = null,
								pass = null,
								uid = null;

							foreach (var line in cook)
							{
								if (string.IsNullOrWhiteSpace(line))
								{
									continue;
								}

								if (line.Contains("PHPSESSID="))
								{
									sessid = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line)
										.Groups[1].Value;
								}

								if (line.Contains("pass="))
								{
									pass = new Regex("pass=([^;]+)(;|$)").Match(line)
										.Groups[1].Value;
								}

								if (line.Contains("uid="))
								{
									uid = new Regex("uid=([^;]+)(;|$)").Match(line)
										.Groups[1].Value;
								}
							}

							if (!string.IsNullOrWhiteSpace(sessid) && !string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
							{
								MemoryCache.Set("baibako:cookie", $"PHPSESSID={sessid}; uid={uid}; pass={pass}",
									DateTime.Now.AddDays(1));

								return true;
							}
						}
					}
				}
			}
		}
		catch
		{
		}

		return false;
	}

	#endregion

	#region Parse

	private static bool workParse;

	public async Task<string> Parse(int maxpage)
	{
		#region Авторизация

		if (Cookie(MemoryCache) == null)
		{
			if (!await TakeLogin())
			{
				return "Не удалось авторизоваться";
			}
		}

		#endregion

		if (workParse)
		{
			return "work";
		}

		workParse = true;

		try
		{
			for (var page = 0; page <= maxpage; page++)
			{
				if (page > 1)
				{
					await Task.Delay(AppInit.conf.Baibako.parseDelay);
				}

				await parsePage(page);
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

	#endregion
}