using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Api.Controllers.CRON;

[Route("/cron/rutor/[action]")]
public class RutorController : BaseController
{
	private static readonly Dictionary<string, List<TaskParse>> taskParse = new();
	private readonly ITorrentRepository	_torrentRepository;

	static RutorController()
	{
		if (IO.File.Exists("Data/temp/rutor_taskParse.json"))
		{
			taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
				IO.File.ReadAllText("Data/temp/rutor_taskParse.json"));
		}
	}

	#region UpdateTasksParse

	public async Task<string> UpdateTasksParse()
	{
		foreach (var cat in new List<string>
				{
					"1",
					"5",
					"4",
					"16",
					"12",
					"6",
					"7",
					"10",
					"17",
					"13",
					"15"
				})
		{
			var html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/0/{cat}/0/0",
				useproxy: AppInit.conf.Rutor.useproxy);

			if (html == null)
			{
				continue;
			}

			// Максимальное количиство страниц
			int.TryParse(Regex.Match(html,
					"<a href=\"/browse/([0-9]+)/[0-9]+/[0-9]+/[0-9]+\"><b>[0-9]+&nbsp;-&nbsp;[0-9]+</b></a></p>")
				.Groups[1].Value, out var maxpages);

			// Загружаем список страниц в список задач
			for (var page = 0; page <= maxpages; page++)
			{
				try
				{
					if (!taskParse.ContainsKey(cat))
					{
						taskParse.Add(cat, new());
					}

					var val = taskParse[cat];

					if (val.FirstOrDefault(i => i.page == page) == null)
					{
						val.Add(new(page));
					}
				}
				catch
				{
				}
			}
		}

		IO.File.WriteAllText("Data/temp/rutor_taskParse.json", JsonConvert.SerializeObject(taskParse));

		return "ok";
	}

	#endregion

	#region parsePage

	private async Task<bool> parsePage(string cat, int page)
	{
		var html = await HttpClient.Get($"{AppInit.conf.Rutor.rqHost()}/browse/{page}/{cat}/0/0",
			useproxy: AppInit.conf.Rutor.useproxy);

		if (html == null)
		{
			return false;
		}

		var torrents = new List<TorrentBaseDetails>();

		foreach (var row in Regex.Split(Regex.Replace(html, "[\n\r\t]+", ""), "<tr class=\"(gai|tum)\">")
					.Skip(1))
		{
			#region Локальный метод - Match

			string Match(string pattern, int index = 1)
			{
				var res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row)
					.Groups[index]
					.Value.Trim());

				res = Regex.Replace(res, "[\n\r\t ]+", " ");

				return res.Replace(" ", " ")
					.Trim(); // Меняем непонятный символ похожий на проблел, на обычный проблел
			}

			#endregion

			if (string.IsNullOrWhiteSpace(row) || !row.Contains("magnet:?xt=urn"))
			{
				continue;
			}

			#region createTime

			var createTime =
				tParse.ParseCreateTime(Match("<td>([^<]+)</td><td([^>]+)?><a class=\"downgif\""), "dd.MM.yy");

			if (createTime == default)
			{
				continue;
			}

			#endregion

			#region Данные раздачи

			var url = Match("<a href=\"/(torrent/[^\"]+)\">");
			var title = Match("<a href=\"/torrent/[^\"]+\">([^<]+)</a>");
			var _sid = Match("<span class=\"green\"><img [^>]+>&nbsp;([0-9]+)</span>");
			var _pir = Match("<span class=\"red\">&nbsp;([0-9]+)</span>");
			var sizeName = Match("<td align=\"right\">([^<]+)</td>");
			var magnet = Match("href=\"(magnet:\\?xt=[^\"]+)\"");

			if (string.IsNullOrWhiteSpace(url)
				|| string.IsNullOrWhiteSpace(title)
				|| title.ToLower()
					.Contains("трейлер")
				|| string.IsNullOrWhiteSpace(_sid)
				|| string.IsNullOrWhiteSpace(_pir)
				|| string.IsNullOrWhiteSpace(sizeName)
				|| string.IsNullOrWhiteSpace(magnet))
			{
				continue;
			}

			if (cat == "17" && !title.Contains(" UKR"))
			{
				continue;
			}

			if (title.Contains(" КПК"))
			{
				continue;
			}

			url = $"{AppInit.conf.Rutor.host}/{url}";

			#endregion

			#region Парсим раздачи

			var relased = 0;

			string name = null,
				originalname = null;

			if (cat == "1" || cat == "17")
			{
				#region Зарубежные фильмы

				var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
					.Groups;

				if (!string.IsNullOrWhiteSpace(g[1].Value)
					&& !string.IsNullOrWhiteSpace(g[2].Value)
					&& !string.IsNullOrWhiteSpace(g[3].Value))
				{
					name = g[1].Value;
					originalname = g[3].Value;

					if (int.TryParse(g[4].Value, out var _yer))
					{
						relased = _yer;
					}
				} else
				{
					g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
						.Groups;

					name = g[1].Value;
					originalname = g[2].Value;

					if (int.TryParse(g[3].Value, out var _yer))
					{
						relased = _yer;
					}
				}

				#endregion
			} else if (cat == "5")
			{
				#region Наши фильмы

				var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)")
					.Groups;

				name = g[1].Value;

				if (int.TryParse(g[2].Value, out var _yer))
				{
					relased = _yer;
				}

				#endregion
			} else if (cat == "4")
			{
				#region Зарубежные сериалы

				var g = Regex.Match(title, "^([^/]+) / [^/]+ / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
					.Groups;

				if (!string.IsNullOrWhiteSpace(g[1].Value)
					&& !string.IsNullOrWhiteSpace(g[2].Value)
					&& !string.IsNullOrWhiteSpace(g[3].Value))
				{
					name = g[1].Value;
					originalname = g[2].Value;

					if (int.TryParse(g[3].Value, out var _yer))
					{
						relased = _yer;
					}
				} else
				{
					g = Regex.Match(title, "^([^/]+) / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
						.Groups;

					if (!string.IsNullOrWhiteSpace(g[1].Value)
						&& !string.IsNullOrWhiteSpace(g[2].Value)
						&& !string.IsNullOrWhiteSpace(g[3].Value))
					{
						name = g[1].Value;
						originalname = g[2].Value;

						if (int.TryParse(g[3].Value, out var _yer))
						{
							relased = _yer;
						}
					} else
					{
						g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
							.Groups;

						name = g[1].Value;
						originalname = g[2].Value;

						if (int.TryParse(g[3].Value, out var _yer))
						{
							relased = _yer;
						}
					}
				}

				#endregion
			} else if (cat == "16")
			{
				#region Наши сериалы

				var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)")
					.Groups;

				name = g[1].Value;

				if (int.TryParse(g[2].Value, out var _yer))
				{
					relased = _yer;
				}

				#endregion
			} else if (cat == "12" || cat == "6" || cat == "7" || cat == "10" || cat == "15" || cat == "13")
			{
				#region Научно-популярные фильмы / Телевизор / Мультипликация / Аниме / Юмор / Спорт и Здоровье

				if (title.Contains(" / "))
				{
					if (title.Contains("[") && title.Contains("]"))
					{
						var g = Regex.Match(title,
								"^([^/]+) / ([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
							.Groups;

						if (!string.IsNullOrWhiteSpace(g[1].Value)
							&& !string.IsNullOrWhiteSpace(g[2].Value)
							&& !string.IsNullOrWhiteSpace(g[3].Value))
						{
							name = g[1].Value;
							originalname = g[3].Value;

							if (int.TryParse(g[4].Value, out var _yer))
							{
								relased = _yer;
							}
						} else
						{
							g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
								.Groups;

							name = g[1].Value;
							originalname = g[2].Value;

							if (int.TryParse(g[3].Value, out var _yer))
							{
								relased = _yer;
							}
						}
					} else
					{
						var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
							.Groups;

						if (!string.IsNullOrWhiteSpace(g[1].Value)
							&& !string.IsNullOrWhiteSpace(g[2].Value)
							&& !string.IsNullOrWhiteSpace(g[3].Value))
						{
							name = g[1].Value;
							originalname = g[3].Value;

							if (int.TryParse(g[4].Value, out var _yer))
							{
								relased = _yer;
							}
						} else
						{
							g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
								.Groups;

							name = g[1].Value;
							originalname = g[2].Value;

							if (int.TryParse(g[3].Value, out var _yer))
							{
								relased = _yer;
							}
						}
					}
				} else
				{
					if (title.Contains("[") && title.Contains("]"))
					{
						var g = Regex.Match(title, "^([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)")
							.Groups;

						name = g[1].Value;

						if (int.TryParse(g[2].Value, out var _yer))
						{
							relased = _yer;
						}
					} else
					{
						var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)")
							.Groups;

						name = g[1].Value;

						if (int.TryParse(g[2].Value, out var _yer))
						{
							relased = _yer;
						}
					}
				}

				#endregion
			}

			#endregion

			if (string.IsNullOrWhiteSpace(name))
			{
				name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0]
					.Trim();
			}

			if (!string.IsNullOrWhiteSpace(name))
			{
				#region types

				string[] types = null;

				switch (cat)
				{
					case "1":
					case "5":
					case "17":
						types = new[]
						{
							"movie"
						};

						break;

					case "4":
					case "16":
						types = new[]
						{
							"serial"
						};

						break;

					case "12":
						types = new[]
						{
							"docuserial",
							"documovie"
						};

						break;

					case "6":
					case "15":
						types = new[]
						{
							"tvshow"
						};

						break;

					case "7":
						types = new[]
						{
							"multfilm",
							"multserial"
						};

						break;

					case "10":
						types = new[]
						{
							"anime"
						};

						break;

					case "13":
						types = new[]
						{
							"sport"
						};

						break;
				}

				if (types == null)
				{
					continue;
				}

				#endregion

				int.TryParse(_sid, out var sid);
				int.TryParse(_pir, out var pir);

				torrents.Add(new()
				{
					trackerName = "rutor",
					types = types,
					url = url,
					title = title,
					sid = sid,
					pir = pir,
					sizeName = sizeName,
					magnet = magnet,
					createTime = createTime,
					name = name,
					originalname = originalname,
					relased = relased
				});
			}
		}

		await _torrentRepository.AddOrUpdateAsync(torrents);

		return torrents.Count > 0;
	}

	#endregion

	#region Parse

	private static bool _workParse;

	public RutorController(IMemoryCache memoryCache, ITorrentRepository torrentRepository) : base(memoryCache)
	{
		_torrentRepository = torrentRepository;
	}

	public async Task<string> Parse(int page)
	{
		if (_workParse)
		{
			return "work";
		}

		_workParse = true;
		var log = "";

		try
		{
			// 1  - Зарубежные фильмы          | Фильмы
			// 5  - Наши фильмы                | Фильмы
			// 4  - Зарубежные сериалы         | Сериалы
			// 16 - Наши сериалы               | Сериалы
			// 12 - Научно-популярные фильмы   | Док. сериалы, Док. фильмы
			// 6  - Телевизор                  | ТВ Шоу
			// 7  - Мультипликация             | Мультфильмы, Мультсериалы
			// 10 - Аниме                      | Аниме
			// 17 - Иностранные релизы         | Фильмы (UKR)
			// 13 - Спорт и Здоровье           | Спорт
			// 15 - Юмор                       | ТВ Шоу
			foreach (var cat in new List<string>
					{
						"1",
						"5",
						"4",
						"16",
						"12",
						"6",
						"7",
						"10",
						"17",
						"13",
						"15"
					})
			{
				var res = await parsePage(cat, page);
				log += $"{cat} - {page} / {res}\n";
			}
		}
		catch
		{
		}
		finally
		{
			_workParse = false;
		}

		return string.IsNullOrWhiteSpace(log)
			? "ok"
			: log;
	}

	#endregion

	#region ParseAllTask

	private static bool _parseAllTaskWork;

	public async Task<string> ParseAllTask()
	{
		if (_parseAllTaskWork)
		{
			return "work";
		}

		_parseAllTaskWork = true;

		try
		{
			foreach (var task in taskParse.ToArray())
			{
				foreach (var val in task.Value.ToArray())
				{
					if (DateTime.Today == val.updateTime)
					{
						continue;
					}

					await Task.Delay(AppInit.conf.Rutor.parseDelay);

					var res = await parsePage(task.Key, val.page);

					if (res)
					{
						val.updateTime = DateTime.Today;
					}
				}
			}
		}
		catch
		{
		}

		_parseAllTaskWork = false;

		return "ok";
	}

	#endregion
}