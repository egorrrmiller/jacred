using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Api.Controllers.CRON;

[Route("/cron/megapeer/[action]")]
public class MegapeerController : BaseController
{
	private static readonly Dictionary<string, List<TaskParse>> taskParse = new();
	private readonly ITorrentRepository	_torrentRepository;

	static MegapeerController()
	{
		if (IO.File.Exists("Data/temp/megapeer_taskParse.json"))
		{
			taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
				IO.File.ReadAllText("Data/temp/megapeer_taskParse.json"));
		}
	}

	#region UpdateTasksParse

	public async Task<string> UpdateTasksParse()
	{
		foreach (var cat in new List<string>
				{
					"174",
					"79",
					"6",
					"5",
					"55",
					"57",
					"76"
				})
		{
			var html = await HttpClient.Get($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}",
				Encoding.GetEncoding(1251), useproxy: AppInit.conf.Megapeer.useproxy,
				addHeaders: new()
				{
					("dnt", "1"),
					("pragma", "no-cache"),
					("referer", $"{AppInit.conf.Megapeer.rqHost()}/cat/{cat}"),
					("sec-fetch-dest", "document"),
					("sec-fetch-mode", "navigate"),
					("sec-fetch-site", "same-origin"),
					("sec-fetch-user", "?1"),
					("upgrade-insecure-requests", "1")
				});

			if (html == null)
			{
				continue;
			}

			// Максимальное количиство страниц
			int.TryParse(Regex.Match(html, ">Всего: ([0-9]+)")
				.Groups[1].Value, out var maxpages);

			maxpages = maxpages / 50;

			if (maxpages > 10)
			{
				maxpages = 10;
			}

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

		IO.File.WriteAllText("Data/temp/megapeer_taskParse.json", JsonConvert.SerializeObject(taskParse));

		return "ok";
	}

	#endregion

	#region parsePage

	private async Task<bool> parsePage(string cat, int page)
	{
		var html = await HttpClient.Get($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}&page={page}",
			Encoding.GetEncoding(1251), useproxy: AppInit.conf.Megapeer.useproxy,
			addHeaders: new()
			{
				("dnt", "1"),
				("pragma", "no-cache"),
				("referer", $"{AppInit.conf.Megapeer.rqHost()}/cat/{cat}"),
				("sec-fetch-dest", "document"),
				("sec-fetch-mode", "navigate"),
				("sec-fetch-site", "same-origin"),
				("sec-fetch-user", "?1"),
				("upgrade-insecure-requests", "1")
			});

		if (html == null || !html.Contains("id=\"logo\""))
		{
			return false;
		}

		var torrents = new List<MegapeerDetails>();

		foreach (var row in html.Split("class=\"table_fon\"")
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

			#region createTime

			var createTime = MediaNameUtils.ParseDate(Match("<td>([0-9]+ [^ ]+ [0-9]+)</td><td>"), "dd.MM.yy");

			if (createTime == default)
			{
				continue;
			}

			#endregion

			#region Данные раздачи

			var url = Match("href=\"/(torrent/[0-9]+)");
			var title = Match("class=\"url\">([^<]+)</a></td>");

			//title = Regex.Replace(title, "<[^>]+>", "");

			var sizeName = Match("<td align=\"right\">([^<\n\r]+)")
				.Trim();

			if (string.IsNullOrWhiteSpace(title))
			{
				continue;
			}

			var _sid = Match("alt=\"S\"><font [^>]+>([0-9]+)</font>");
			var _pir = Match("alt=\"L\"><font [^>]+>([0-9]+)</font>");

			url = $"{AppInit.conf.Megapeer.host}/{url}";

			#endregion

			#region Парсим раздачи

			var relased = 0;

			string name = null,
				originalname = null;

			if (cat == "174")
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
			} else if (cat == "79")
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
			} else if (cat == "6")
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
			} else if (cat == "5")
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
			} else if (cat == "55" || cat == "57" || cat == "76")
			{
				#region Научно-популярные фильмы / Телевизор / Мультипликация

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

				var types = new string[]
				{
				};

				switch (cat)
				{
					case "174":
					case "79":
						types = new[]
						{
							"movie"
						};

						break;

					case "6":
					case "5":
						types = new[]
						{
							"serial"
						};

						break;

					case "55":
						types = new[]
						{
							"docuserial",
							"documovie"
						};

						break;

					case "57":
						types = new[]
						{
							"tvshow"
						};

						break;

					case "76":
						types = new[]
						{
							"multfilm",
							"multserial"
						};

						break;
				}

				#endregion

				var downloadid = Match("href=\"/?download/([0-9]+)\"");

				if (string.IsNullOrWhiteSpace(downloadid))
				{
					continue;
				}

				int.TryParse(_sid, out var sid);
				int.TryParse(_pir, out var pir);

				torrents.Add(new()
				{
					TrackerName = "megapeer",
					Types = types,
					Url = url,
					Title = title,
					Sid = sid,
					Pir = pir,
					SizeName = sizeName,
					CreateTime = createTime,
					Name = name,
					OriginalName = originalname,
					Relased = relased,
					downloadId = downloadid
				});
			}
		}

		await _torrentRepository.AddOrUpdateAsync(torrents, async (t, db) =>
		{
			if (db.TryGetValue(t.Url, out var _tcache) && _tcache.Title == t.Title)
			{
				return true;
			}

			var _t = await HttpClient.Download($"{AppInit.conf.Megapeer.host}/download/{t.downloadId}",
				referer: AppInit.conf.Megapeer.host);

			var magnet = BencodeTo.Magnet(_t);

			if (!string.IsNullOrWhiteSpace(magnet))
			{
				t.Magnet = magnet;

				return true;
			}

			return false;
		});

		return torrents.Count > 0;
	}

	#endregion

	#region Parse

	private static bool _workParse;

	public MegapeerController(IMemoryCache memoryCache, ITorrentRepository torrentRepository) : base(memoryCache)
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
			// 174 - Зарубежные фильмы          | Фильмы
			// 79  - Наши фильмы                | Фильмы
			// 6   - Зарубежные сериалы         | Сериалы
			// 5   - Наши сериалы               | Сериалы
			// 55  - Научно-популярные фильмы   | Док. сериалы, Док. фильмы
			// 57  - Телевизор                  | ТВ Шоу
			// 76  - Мультипликация             | Мультфильмы, Мультсериалы
			foreach (var cat in new List<string>
					{
						"174",
						"79",
						"6",
						"5",
						"55",
						"57",
						"76"
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

					await Task.Delay(AppInit.conf.Megapeer.parseDelay);

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