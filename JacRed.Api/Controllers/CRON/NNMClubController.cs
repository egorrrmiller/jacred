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

[Route("/cron/nnmclub/[action]")]
public class NNMClubController : BaseController
{
	private static readonly Dictionary<string, List<TaskParse>> taskParse = new();
	private readonly ITorrentRepository	_torrentRepository;

	static NNMClubController()
	{
		if (IO.File.Exists("Data/temp/nnmclub_taskParse.json"))
		{
			taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
				IO.File.ReadAllText("Data/temp/nnmclub_taskParse.json"));
		}
	}

	#region UpdateTasksParse

	public async Task<string> UpdateTasksParse()
	{
		foreach (var cat in new List<string>
				{
					"10",
					"13",
					"6",
					"4",
					"3",
					"22",
					"23",
					"1",
					"7",
					"11"
				})
		{
			var html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}",
				Encoding.GetEncoding(1251), timeoutSeconds: 10, useproxy: AppInit.conf.NNMClub.useproxy);

			if (html == null || !html.Contains("NNM-Club</title>"))
			{
				continue;
			}

			// Максимальное количиство страниц
			int.TryParse(Regex.Match(html, "<a href=\"[^\"]+\">([0-9]+)</a>[^<\n\r]+<a href=\"[^\"]+\">След.</a>")
				.Groups[1]
				.Value, out var maxpages);

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

		IO.File.WriteAllText("Data/temp/nnmclub_taskParse.json", JsonConvert.SerializeObject(taskParse));

		return "ok";
	}

	#endregion

	#region parsePage

	private async Task<bool> parsePage(string cat, int page)
	{
		var html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}&start={page * 20}",
			Encoding.GetEncoding(1251), useproxy: AppInit.conf.NNMClub.useproxy);

		if (html == null || !html.Contains("NNM-Club</title>"))
		{
			return false;
		}

		var container = new Regex("<td valign=\"top\" width=\"[0-9]+%\">(.*)<div class=\"paginport nav\">")
			.Match(Regex.Replace(html, "(\n|\r|\t)", ""))
			.Groups[1].Value;

		if (string.IsNullOrWhiteSpace(container))
		{
			return false;
		}

		var torrents = new List<TorrentBaseDetails>();

		foreach (var row in MediaNameUtils.Normalize(container)
					.Split("<table width=\"100%\" class=\"pline\">"))
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

			// Магнет ссылка
			var magnet = new Regex("\"(magnet:[^\"]+)\"").Match(row)
				.Groups[1].Value;

			if (string.IsNullOrWhiteSpace(magnet))
			{
				continue;
			}

			#region createTime

			var createTime =
				MediaNameUtils.ParseDate(Match("\\| ([0-9]+ [^ ]+ [0-9]{4} [^<]+)</span> \\| <span class=\"tit\""),
					"dd.MM.yyyy HH:mm:ss");

			if (createTime == default)
			{
				continue;
			}

			#endregion

			#region Данные раздачи

			var url = Match("<a class=\"pgenmed\" href=\"(viewtopic.php[^\"]+)\"");
			var title = Match(">([^<]+)</a></h2></td>");
			var _sid = Match("title=\"Раздаюших\">&nbsp;([0-9]+)</span>");
			var _pir = Match("title=\"Качают\">&nbsp;([0-9]+)</span>");
			var sizeName = Match("<span class=\"pcomm bold\">([^<]+)</span>");

			if (string.IsNullOrWhiteSpace(url)
				|| string.IsNullOrWhiteSpace(title)
				|| string.IsNullOrWhiteSpace(_sid)
				|| string.IsNullOrWhiteSpace(_pir)
				|| string.IsNullOrWhiteSpace(sizeName))
			{
				continue;
			}

			url = $"{AppInit.conf.NNMClub.host}/forum/{url}";

			#endregion

			#region Парсим раздачи

			var relased = 0;

			string name = null,
				originalname = null;

			if (cat == "10" || cat == "6" || cat == "3" || cat == "22" || cat == "23" || cat == "11")
			{
				#region Новинки кино / Зарубежное кино / Зарубежные сериалы / Док. TV-бренды / Док. и телепередачи

				// Крестная мама (Наркомама) / La Daronne / Mama Weed (2020)
				var g = Regex.Match(title,
						"^([^/\\(\\|]+) \\([^\\)]+\\) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
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
					// Связанный груз / Белые рабыни-девственницы / Bound Cargo / White Slave Virgins (2003) DVDRip
					g = Regex.Match(title,
							"^([^/\\(\\|]+) / [^/\\(\\|]+ / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
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
						// Академия монстров / Escuela de Miedo / Cranston Academy: Monster Zone (2020)
						g = Regex.Match(title,
								"^([^/\\(\\|]+) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
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
							// Воображаемая реальность (Долина богов) / Valley of the Gods (2019)
							g = Regex.Match(title,
									"^([^/\\(\\|]+) \\([^\\)]+\\) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
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
								// Страна грёз / Dreamland (2019)
								g = Regex.Match(title, "^([^/\\(\\|]+) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
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
									// Тайны анатомии (Мозг) (2020)
									g = Regex.Match(title, "^([^/\\(\\|]+) \\([^\\)]+\\) \\(([0-9]{4})(-[0-9]{4})?\\)")
										.Groups;

									if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
									{
										name = g[1].Value;

										if (int.TryParse(g[2].Value, out var _yer))
										{
											relased = _yer;
										}
									} else
									{
										// Презумпция виновности (2020)
										g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
											.Groups;

										name = g[1].Value;

										if (int.TryParse(g[2].Value, out var _yer))
										{
											relased = _yer;
										}
									}
								}
							}
						}
					}
				}

				#endregion
			} else if (cat == "13")
			{
				#region Наше кино

				var g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})\\)")
					.Groups;

				name = g[1].Value;

				if (int.TryParse(g[2].Value, out var _yer))
				{
					relased = _yer;
				}

				#endregion
			} else if (cat == "4")
			{
				#region Наши сериалы

				// Теория вероятности / Игрок (2020)
				var g = Regex.Match(title, "^([^/\\(\\|]+) / [^/\\(\\|]+ \\(([0-9]{4})(-[0-9]{4})?\\)")
					.Groups;

				if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
				{
					name = g[1].Value;

					if (int.TryParse(g[2].Value, out var _yer))
					{
						relased = _yer;
					}
				} else
				{
					// Тайны следствия (2020)
					g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
						.Groups;

					name = g[1].Value;

					if (int.TryParse(g[2].Value, out var _yer))
					{
						relased = _yer;
					}
				}

				#endregion
			} else if (cat == "1")
			{
				#region Аниме и Манга

				// Black Clover (2017) | Чёрный клевер (часть 2) [2017(-2021)?,
				var g = Regex.Match(title,
						"^([^/\\[\\(]+) \\([0-9]{4}\\) \\| ([^/\\[\\(]+) \\([^\\)]+\\) \\[([0-9]{4})(-[0-9]{4})?,")
					.Groups;

				if (!string.IsNullOrWhiteSpace(g[1].Value)
					&& !string.IsNullOrWhiteSpace(g[2].Value)
					&& !string.IsNullOrWhiteSpace(g[3].Value))
				{
					name = g[2].Value;
					originalname = g[1].Value;

					if (int.TryParse(g[3].Value, out var _yer))
					{
						relased = _yer;
					}
				} else
				{
					// Black Clover (2017) | Чёрный клевер [2017(-2021)?,
					g = Regex.Match(title, "^([^/\\[\\(]+) \\([0-9]{4}\\) \\| ([^/\\[\\(]+) \\[([0-9]{4})(-[0-9]{4})?,")
						.Groups;

					if (!string.IsNullOrWhiteSpace(g[1].Value)
						&& !string.IsNullOrWhiteSpace(g[2].Value)
						&& !string.IsNullOrWhiteSpace(g[3].Value))
					{
						name = g[2].Value;
						originalname = g[1].Value;

						if (int.TryParse(g[3].Value, out var _yer))
						{
							relased = _yer;
						}
					} else
					{
						// Tunshi Xingkong | Swallowed Star | Пожиратель звёзд | Поглощая звезду [2020(-2021)?,
						// Tunshi Xingkong | Swallowed Star | Пожиратель звёзд | Поглощая звезду [ТВ-1] [2020(-2021)?,
						g = Regex.Match(title,
								"^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
							.Groups;

						if (!string.IsNullOrWhiteSpace(g[1].Value)
							&& !string.IsNullOrWhiteSpace(g[2].Value)
							&& !string.IsNullOrWhiteSpace(g[5].Value))
						{
							name = g[2].Value;
							originalname = g[1].Value;

							if (int.TryParse(g[5].Value, out var _yer))
							{
								relased = _yer;
							}
						} else
						{
							// Uzaki-chan wa Asobitai! | Uzaki-chan Wants to Hang Out! | Узаки хочет тусоваться! (Удзаки хочет погулять!) [2020(-2021)?,
							// Uzaki-chan wa Asobitai! | Uzaki-chan Wants to Hang Out! | Узаки хочет тусоваться! (Удзаки хочет погулять!) [ТВ-1] [2020(-2021)?,
							g = Regex.Match(title,
									"^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) \\([^\\)]+\\) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
								.Groups;

							if (!string.IsNullOrWhiteSpace(g[1].Value)
								&& !string.IsNullOrWhiteSpace(g[2].Value)
								&& !string.IsNullOrWhiteSpace(g[5].Value))
							{
								name = g[2].Value;
								originalname = g[1].Value;

								if (int.TryParse(g[5].Value, out var _yer))
								{
									relased = _yer;
								}
							} else
							{
								// Kanojo, Okarishimasu | Rent-A-Girlfriend | Девушка на час [ТВ-1] [2020(-2021)?,
								// Kusoge-tte Iuna! | Don`t Call Us a Junk Game! | Это вам не трешовая игра! [2020(-2021)?,
								g = Regex.Match(title,
										"^([^/\\[\\(]+) \\| [^/\\[\\(]+ \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
									.Groups;

								if (!string.IsNullOrWhiteSpace(g[1].Value)
									&& !string.IsNullOrWhiteSpace(g[2].Value)
									&& !string.IsNullOrWhiteSpace(g[5].Value))
								{
									name = g[2].Value;
									originalname = g[1].Value;

									if (int.TryParse(g[5].Value, out var _yer))
									{
										relased = _yer;
									}
								} else
								{
									// Re:Zero kara Hajimeru Isekai Seikatsu 2nd Season | Re: Жизнь в альтернативном мире с нуля [ТВ-2] [2020(-2021)?,
									// Hortensia Saga | Сага о гортензии [2021(-2021)?,
									g = Regex.Match(title,
											"^([^/\\[\\(]+) \\| ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
										.Groups;

									if (!string.IsNullOrWhiteSpace(g[1].Value)
										&& !string.IsNullOrWhiteSpace(g[2].Value)
										&& !string.IsNullOrWhiteSpace(g[5].Value))
									{
										name = g[2].Value;
										originalname = g[1].Value;

										if (int.TryParse(g[5].Value, out var _yer))
										{
											relased = _yer;
										}
									} else
									{
										// Shingeki no Kyojin: The Final Season / Attack on Titan Final Season / Атака титанов. Последний сезон [TV-4] [2020(-2021)?,
										g = Regex.Match(title,
												"^([^/\\[\\(]+) / [^/\\[\\(]+ / ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
											.Groups;

										if (!string.IsNullOrWhiteSpace(g[1].Value)
											&& !string.IsNullOrWhiteSpace(g[2].Value)
											&& !string.IsNullOrWhiteSpace(g[5].Value))
										{
											name = g[2].Value;
											originalname = g[1].Value;

											if (int.TryParse(g[5].Value, out var _yer))
											{
												relased = _yer;
											}
										} else
										{
											// Shingeki no Kyojin: The Final Season / Атака титанов. Последний сезон [TV-4] [2020(-2021)?,
											g = Regex.Match(title,
													"^([^/\\[\\(]+) / ([^/\\[\\(]+) (\\[(ТВ|TV)-[0-9]+\\] )?\\[([0-9]{4})(-[0-9]{4})?,")
												.Groups;

											if (!string.IsNullOrWhiteSpace(g[1].Value)
												&& !string.IsNullOrWhiteSpace(g[2].Value)
												&& !string.IsNullOrWhiteSpace(g[5].Value))
											{
												name = g[2].Value;
												originalname = g[1].Value;

												if (int.TryParse(g[5].Value, out var _yer))
												{
													relased = _yer;
												}
											}
										}
									}
								}
							}
						}
					}
				}

				#endregion
			} else if (cat == "7")
			{
				#region Детям и родителям

				if (!title.ToLower()
						.Contains("pdf")
					&& (row.Contains("должительность")
						|| row.ToLower()
							.Contains("мульт")))
				{
					// Академия монстров / Escuela de Miedo / Cranston Academy: Monster Zone (2020)
					var g = Regex.Match(title,
							"^([^/\\(\\|]+) / [^/\\(\\|]+ / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
						.Groups;

					if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
					{
						name = g[1].Value;
						originalname = g[2].Value;

						if (int.TryParse(g[3].Value, out var _yer))
						{
							relased = _yer;
						}
					} else
					{
						// Трансформеры: Война за Кибертрон / Transformers: War For Cybertron (2020)
						g = Regex.Match(title, "^([^/\\(\\|]+) / ([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
							.Groups;

						if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
						{
							name = g[1].Value;
							originalname = g[2].Value;

							if (int.TryParse(g[3].Value, out var _yer))
							{
								relased = _yer;
							}
						} else
						{
							// Спина к спине (2020-2021)
							g = Regex.Match(title, "^([^/\\(\\|]+) \\(([0-9]{4})(-[0-9]{4})?\\)")
								.Groups;

							name = g[1].Value;

							if (int.TryParse(g[2].Value, out var _yer))
							{
								relased = _yer;
							}
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
					case "10":
					case "13":
					case "6":
					case "11":
						types = new[]
						{
							"movie"
						};

						break;

					case "4":
					case "3":
						types = new[]
						{
							"serial"
						};

						break;

					case "22":
					case "23":
						types = new[]
						{
							"docuserial",
							"documovie"
						};

						break;

					case "7":
						types = new[]
						{
							"multfilm",
							"multserial"
						};

						break;

					case "1":
						types = new[]
						{
							"anime"
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
					TrackerName = "nnmclub",
					Types = types,
					Url = url,
					Title = title,
					Sid = sid,
					Pir = pir,
					SizeName = sizeName,
					Magnet = magnet,
					CreateTime = createTime,
					Name = name,
					OriginalName = originalname,
					Relased = relased
				});
			}
		}

		await _torrentRepository.AddOrUpdateAsync(torrents);

		return torrents.Count > 0;
	}

	#endregion

	#region Parse

	private static bool _workParse;

	public NNMClubController(IMemoryCache memoryCache) : base(memoryCache)
	{
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
			// 10 - Новинки кино          | Фильмы
			// 13 - Наше кино             | Фильмы
			// 6  - Зарубежное кино       | Фильмы
			// 4  - Наши сериалы          | Сериалы
			// 3  - Зарубежные сериалы    | Сериалы
			// 22 - Док. TV-бренды        | Док. сериалы, Док. фильмы
			// 23 - Док. и телепередачи   | Док. сериалы, Док. фильмы
			// 1  - Аниме и Манга         | Аниме
			// 7  - Детям и родителям     | Мультфильмы, Мультсериалы
			// 11 - HD, UHD и 3D Кино     | Фильмы
			foreach (var cat in new List<string>
					{
						"10",
						"13",
						"6",
						"4",
						"3",
						"22",
						"23",
						"1",
						"7",
						"11"
					})
			{
				await parsePage(cat, page);
				log += $"{cat} - {page}\n";
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

					await Task.Delay(AppInit.conf.NNMClub.parseDelay);

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