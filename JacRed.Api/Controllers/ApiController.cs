using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Api.Engine.Tracks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MonoTorrent;
using Newtonsoft.Json.Linq;
using TorrentInfo = JacRed.Core.Models.Api.TorrentInfo;

namespace JacRed.Api.Controllers;

using TorrentInfo = TorrentInfo;

public class ApiController : BaseController
{
	private readonly IContentCatalog _contentCatalog;

	private readonly ITorrentRepository _torrentRepository;

	public ApiController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, IContentCatalog contentCatalog)
		: base(memoryCache)
	{
		_torrentRepository = torrentRepository;
		_contentCatalog = contentCatalog;
	}

	[Route("/")]
	public ActionResult Index() => File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");

	[Route("health")]
	public IActionResult Health() => Json(new Dictionary<string, string>
	{
		["status"] = "OK"
	});

	[Route("version")]
	public ActionResult Version() => Content("11", "text/plain; charset=utf-8");

	[Route("lastupdatedb")]
	public async Task<ActionResult> LastUpdateDB()
	{
		var db = await _contentCatalog.GetAllKeysAsync();

		if (db == null || db.Count == 0)
		{
			return Content("01.01.2000 01:01", "text/plain; charset=utf-8");
		}

		return Content(db.OrderByDescending(i => i.Value.updateTime)
				.First()
				.Value.updateTime.ToString("dd.MM.yyyy HH:mm"),
			"text/plain; charset=utf-8");
	}

	[Route("api/v1.0/conf")]
	public JsonResult JacRedConf(string apikey) => Json(new
	{
		apikey = string.IsNullOrWhiteSpace(AppInit.conf.apikey) || apikey == AppInit.conf.apikey
	});

	#region Jackett

	[Route("/api/v2.0/indexers/{status}/results")]
	public async Task<ActionResult> Jackett(string apikey, string query, string title, string title_original, int year,
											Dictionary<string, string> category, int is_serial = -1)
	{
		//Console.WriteLine(HttpContext.Request.Path + HttpContext.Request.QueryString.Value);

		var cachekey =
			$"api:v2.0:indexers:{query}:{title}:{title_original}:{year}:{(category != null && category.Count > 0 ? string.Join(",", category.Select(i => $"{i.Key}={i.Value}")) : "null")}:{is_serial}";

		if (MemoryCache.TryGetValue(cachekey, out List<Result> _cacheResult))
		{
			return Json(new RootObject
			{
				Results = _cacheResult
			});
		}

		var fastdb = await _contentCatalog.GetFastIndexes();
		var torrents = new Dictionary<string, TorrentDetails>();

		var rqnum = !HttpContext.Request.QueryString.Value.Contains("&is_serial=")
					&& HttpContext.Request.Headers.UserAgent.ToString()
					== "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

		#region Запрос с NUM

		if (rqnum && query != null)
		{
			var mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

			if (mNum.Success)
			{
				if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
				{
					var g = mNum.Groups;
					title = g[1].Value;
					title_original = g[2].Value;
					year = int.Parse(g[3].Value);
				}
			} else
			{
				if (Regex.IsMatch(query, "^([^a-z-A-Z]+) ((19|20)[0-9]{2})$"))
				{
					return Json(new RootObject
					{
						Results = new()
					});
				}

				mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+)$");

				if (mNum.Success)
				{
					if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
					{
						var g = mNum.Groups;
						title = g[1].Value;
						title_original = g[2].Value;
					}
				}
			}
		}

		#endregion

		#region category

		if (is_serial == 0 && category != null)
		{
			var cat = category.FirstOrDefault()
				.Value;

			if (cat != null)
			{
				if (cat.Contains("5020") || cat.Contains("2010"))
				{
					is_serial = 3; // tvshow
				} else if (cat.Contains("5080"))
				{
					is_serial = 4; // док
				} else if (cat.Contains("5070"))
				{
					is_serial = 5; // аниме
				} else if (is_serial == 0)
				{
					if (cat.StartsWith("20"))
					{
						is_serial = 1; // фильм
					} else if (cat.StartsWith("50"))
					{
						is_serial = 2; // сериал
					}
				}
			}
		}

		#endregion

		#region AddTorrents

		void AddTorrents(TorrentDetails t)
		{
			if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName))
			{
				return;
			}

			if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName))
			{
				return;
			}

			if (torrents.TryGetValue(t.url, out var val))
			{
				if (t.updateTime > val.updateTime)
				{
					torrents[t.url] = t;
				}
			} else
			{
				torrents.TryAdd(t.url, t);
			}
		}

		#endregion

		if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(title_original))
		{
			#region Точный поиск

			var _n = StringConvert.SearchName(title);
			var _o = StringConvert.SearchName(title_original);

			var keys = new HashSet<string>(20);

			void updateKeys(string k)
			{
				if (k != null && fastdb.TryGetValue(k, out var _keys))
				{
					foreach (var val in _keys)
					{
						keys.Add(val);
					}
				}
			}

			updateKeys(_n);
			updateKeys(_o);

			if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0) && keys.Count > AppInit.conf.maxreadfile)
			{
				keys = keys.Take(AppInit.conf.maxreadfile)
					.ToHashSet();
			}

			foreach (var key in keys)
			{
				foreach (var t in (await _torrentRepository.GetCollectionAsync(key, true)).Values)
				{
					if (t.types == null || t.title.Contains(" КПК"))
					{
						continue;
					}

					var name = t._sn ?? StringConvert.SearchName(t.name);
					var originalname = t._so ?? StringConvert.SearchName(t.originalname);

					// Точная выборка по name или originalname
					if ((_n != null && _n == name) || (_o != null && _o == originalname))
					{
						if (is_serial == 1)
						{
							#region Фильм

							if (t.types.Contains("movie")
								|| t.types.Contains("multfilm")
								|| t.types.Contains("anime")
								|| t.types.Contains("documovie"))
							{
								if (Regex.IsMatch(t.title, " (сезон|сери(и|я|й))", RegexOptions.IgnoreCase))
								{
									continue;
								}

								if (year > 0)
								{
									if (t.relased == year || t.relased == year - 1 || t.relased == year + 1)
									{
										AddTorrents(t);
									}
								} else
								{
									AddTorrents(t);
								}
							}

							#endregion
						} else if (is_serial == 2)
						{
							#region Сериал

							if (t.types.Contains("serial")
								|| t.types.Contains("multserial")
								|| t.types.Contains("anime")
								|| t.types.Contains("docuserial")
								|| t.types.Contains("tvshow"))
							{
								if (year > 0)
								{
									if (t.relased >= year - 1)
									{
										AddTorrents(t);
									}
								} else
								{
									AddTorrents(t);
								}
							}

							#endregion
						} else if (is_serial == 3)
						{
							#region tvshow

							if (t.types.Contains("tvshow"))
							{
								if (year > 0)
								{
									if (t.relased >= year - 1)
									{
										AddTorrents(t);
									}
								} else
								{
									AddTorrents(t);
								}
							}

							#endregion
						} else if (is_serial == 4)
						{
							#region docuserial / documovie

							if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
							{
								if (year > 0)
								{
									if (t.relased >= year - 1)
									{
										AddTorrents(t);
									}
								} else
								{
									AddTorrents(t);
								}
							}

							#endregion
						} else if (is_serial == 5)
						{
							#region anime

							if (t.types.Contains("anime"))
							{
								if (year > 0)
								{
									if (t.relased >= year - 1)
									{
										AddTorrents(t);
									}
								} else
								{
									AddTorrents(t);
								}
							}

							#endregion
						} else
						{
							#region Неизвестно

							if (year > 0)
							{
								if (t.types.Contains("movie") || t.types.Contains("multfilm") || t.types.Contains("documovie"))
								{
									if (t.relased == year || t.relased == year - 1 || t.relased == year + 1)
									{
										AddTorrents(t);
									}
								} else
								{
									if (t.relased >= year - 1)
									{
										AddTorrents(t);
									}
								}
							} else
							{
								AddTorrents(t);
							}

							#endregion
						}
					}
				}
			}

			#endregion
		} else if (!string.IsNullOrWhiteSpace(query) && query.Length > 1)
		{
			#region Обычный поиск

			var _s = StringConvert.SearchName(query);

			#region torrentsSearch

			async Task torrentsSearch(bool exact, bool exactdb)
			{
				if (_s == null)
				{
					return;
				}

				HashSet<string> keys = null;

				if (exactdb)
				{
					if (fastdb.TryGetValue(_s, out var _keys) && _keys.Count > 0)
					{
						keys = new(_keys.Count);

						foreach (var val in _keys)
						{
							keys.Add(val);
						}
					}
				} else
				{
					var mkey = $"api:torrentsSearch:{_s}";

					if (!MemoryCache.TryGetValue(mkey, out keys))
					{
						keys = new();

						foreach (var f in fastdb.Where(i => i.Key.Contains(_s)))
						{
							foreach (var k in f.Value)
							{
								keys.Add(k);
							}

							if ((!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
								&& keys.Count > AppInit.conf.maxreadfile)
							{
								break;
							}
						}

						MemoryCache.Set(mkey, keys, DateTime.Now.AddHours(1));
					}
				}

				if (keys != null && keys.Count > 0)
				{
					foreach (var key in keys)
					{
						foreach (var t in (await _torrentRepository.GetCollectionAsync(key, true)).Values)
						{
							if (exact)
							{
								if ((t._sn ?? StringConvert.SearchName(t.name)) != _s
									&& (t._so ?? StringConvert.SearchName(t.originalname)) != _s)
								{
									continue;
								}
							}

							if (t.types == null || t.title.Contains(" КПК"))
							{
								continue;
							}

							if (is_serial == 1)
							{
								if (t.types.Contains("movie")
									|| t.types.Contains("multfilm")
									|| t.types.Contains("anime")
									|| t.types.Contains("documovie"))
								{
									AddTorrents(t);
								}
							} else if (is_serial == 2)
							{
								if (t.types.Contains("serial")
									|| t.types.Contains("multserial")
									|| t.types.Contains("anime")
									|| t.types.Contains("docuserial")
									|| t.types.Contains("tvshow"))
								{
									AddTorrents(t);
								}
							} else if (is_serial == 3)
							{
								if (t.types.Contains("tvshow"))
								{
									AddTorrents(t);
								}
							} else if (is_serial == 4)
							{
								if (t.types.Contains("docuserial") || t.types.Contains("documovie"))
								{
									AddTorrents(t);
								}
							} else if (is_serial == 5)
							{
								if (t.types.Contains("anime"))
								{
									AddTorrents(t);
								}
							} else
							{
								AddTorrents(t);
							}
						}
					}
				}
			}

			#endregion

			if (is_serial == -1)
			{
				torrentsSearch(false, true);

				if (torrents.Count == 0)
				{
					torrentsSearch(false, false);
				}
			} else
			{
				torrentsSearch(true, true);

				if (torrents.Count == 0)
				{
					torrentsSearch(false, false);
				}
			}

			#endregion
		}

		#region getCategoryIds

		HashSet<int> getCategoryIds(TorrentDetails t, out string categoryDesc)
		{
			categoryDesc = null;
			var categoryIds = new HashSet<int>(t.types.Length);

			foreach (var type in t.types)
			{
				switch (type)
				{
					case "movie":
						categoryDesc = "Movies";
						categoryIds.Add(2000);

						break;

					case "serial":
						categoryDesc = "TV";
						categoryIds.Add(5000);

						break;

					case "documovie":
					case "docuserial":
						categoryDesc = "TV/Documentary";
						categoryIds.Add(5080);

						break;

					case "tvshow":
						categoryDesc = "TV/Foreign";
						categoryIds.Add(5020);
						categoryIds.Add(2010);

						break;

					case "anime":
						categoryDesc = "TV/Anime";
						categoryIds.Add(5070);

						break;
				}
			}

			return categoryIds;
		}

		#endregion

		#region Объединить дубликаты

		IEnumerable<TorrentDetails> result = null;

		if ((!rqnum && AppInit.conf.mergeduplicates) || (rqnum && AppInit.conf.mergenumduplicates))
		{
			Dictionary<string, (TorrentDetails torrent, string title, string Name, List<string> AnnounceUrls)> temp =
				new();

			foreach (var torrent in torrents.Values.OrderByDescending(i => i.createTime)
						.ThenBy(i => i.trackerName == "selezen"))
			{
				var magnetLink = MagnetLink.Parse(torrent.magnet);
				var hex = magnetLink.InfoHashes.V1OrV2.ToHex();

				if (!temp.TryGetValue(hex, out var _))
				{
					temp.TryAdd(hex,
						((TorrentDetails) torrent.Clone(), torrent.trackerName == "kinozal"
								? torrent.title
								: null,
							magnetLink.Name, magnetLink.AnnounceUrls?.ToList() ?? new List<string>()));
				} else
				{
					var t = temp[hex];

					if (!t.torrent.trackerName.Contains(torrent.trackerName))
					{
						t.torrent.trackerName += $", {torrent.trackerName}";
					}

					#region UpdateMagnet

					void UpdateMagnet()
					{
						var magnet = $"magnet:?xt=urn:btih:{hex.ToLower()}";

						if (!string.IsNullOrWhiteSpace(t.Name))
						{
							magnet += $"&dn={HttpUtility.UrlEncode(t.Name)}";
						}

						if (t.AnnounceUrls.Count > 0)
						{
							foreach (var announce in t.AnnounceUrls)
							{
								var tr = announce.Contains("/") || announce.Contains(":")
									? HttpUtility.UrlEncode(announce)
									: announce;

								if (!magnet.Contains(tr))
								{
									magnet += $"&tr={tr}";
								}
							}
						}

						t.torrent.magnet = magnet;
					}

					#endregion

					if (string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(magnetLink.Name))
					{
						t.Name = magnetLink.Name;
						temp[hex] = t;
						UpdateMagnet();
					}

					if (magnetLink.AnnounceUrls != null && magnetLink.AnnounceUrls.Count > 0)
					{
						t.AnnounceUrls.AddRange(magnetLink.AnnounceUrls);
						UpdateMagnet();
					}

					#region UpdateTitle

					void UpdateTitle()
					{
						if (string.IsNullOrWhiteSpace(t.title))
						{
							return;
						}

						var title = t.title;

						if (t.torrent.voices != null && t.torrent.voices.Count > 0)
						{
							title += $" | {string.Join(" | ", t.torrent.voices)}";
						}

						t.torrent.title = title;
					}

					if (torrent.trackerName == "kinozal")
					{
						t.title = torrent.title;
						temp[hex] = t;
						UpdateTitle();
					}

					if (torrent.voices != null && torrent.voices.Count > 0)
					{
						if (t.torrent.voices == null)
						{
							t.torrent.voices = torrent.voices;
						} else
						{
							foreach (var v in torrent.voices)
							{
								t.torrent.voices.Add(v);
							}
						}

						UpdateTitle();
					}

					#endregion

					if (torrent.trackerName != "selezen")
					{
						if (torrent.sid > t.torrent.sid)
						{
							t.torrent.sid = torrent.sid;
						}

						if (torrent.pir > t.torrent.pir)
						{
							t.torrent.pir = torrent.pir;
						}
					}

					if (torrent.createTime > t.torrent.createTime)
					{
						t.torrent.createTime = torrent.createTime;
					}

					if (torrent.voices != null && torrent.voices.Count > 0)
					{
						if (t.torrent.voices == null)
						{
							t.torrent.voices = new();
						}

						foreach (var v in torrent.voices)
						{
							t.torrent.voices.Add(v);
						}
					}

					if (torrent.languages != null && torrent.languages.Count > 0)
					{
						if (t.torrent.languages == null)
						{
							t.torrent.languages = new();
						}

						foreach (var v in torrent.languages)
						{
							t.torrent.languages.Add(v);
						}
					}

					if (t.torrent.ffprobe == null && torrent.ffprobe != null)
					{
						t.torrent.ffprobe = torrent.ffprobe;
					}
				}
			}

			result = temp.Select(i => i.Value.torrent);
		} else
		{
			result = torrents.Values;
		}

		#endregion

		if (apikey == "rus")
		{
			result = result.Where(i =>
				(i.languages != null && i.languages.Contains("rus"))
				|| (i.types != null && (i.types.Contains("sport") || i.types.Contains("tvshow") || i.types.Contains("docuserial"))));
		}

		#region FFprobe

		List<ffStream> FFprobe(TorrentDetails t, out HashSet<string> langs)
		{
			langs = t.languages;

			if (t.ffprobe != null || !AppInit.conf.tracks)
			{
				langs = TracksDB.Languages(t, t.ffprobe);

				return t.ffprobe;
			}

			var streams = TracksDB.Get(t.magnet, t.types, true);
			langs = TracksDB.Languages(t, streams ?? t.ffprobe);

			if (streams == null)
			{
				return null;
			}

			return streams;
		}

		#endregion

		var Results = new List<Result>(torrents.Values.Count);

		foreach (var i in result)
		{
			HashSet<string> languages = null;

			var ffprobe = rqnum
				? null
				: FFprobe(i, out languages);

			Results.Add(new()
			{
				Tracker = i.trackerName,
				Details = i.url != null && i.url.StartsWith("http")
					? i.url
					: null,
				Title = i.title,
				Size = i.size,
				PublishDate = i.createTime,
				Category = getCategoryIds(i, out var categoryDesc),
				CategoryDesc = categoryDesc,
				Seeders = i.sid,
				Peers = i.pir,
				MagnetUri = i.magnet,
				ffprobe = ffprobe,
				languages = languages,
				info = rqnum
					? null
					: new TorrentInfo
					{
						name = i.name,
						originalname = i.originalname,
						sizeName = i.sizeName,
						relased = i.relased,
						videotype = i.videotype,
						quality = i.quality,
						voices = i.voices,
						seasons = i.seasons != null && i.seasons.Count > 0
							? i.seasons
							: null,
						types = i.types
					}
			});
		}

		if (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
		{
			MemoryCache.Set(cachekey, Results, DateTime.Now.AddMinutes(5));
		}

		return Json(new RootObject
		{
			Results = Results
		});
	}

	#endregion

	#region Torrents

	[Route("/api/v1.0/torrents")]
	public async Task<JsonResult> Torrents(string search, string altname, bool exact, string type, string sort,
											string tracker, string voice, string videotype, long relased, long quality,
											long season)
	{
		var db = await _contentCatalog.GetAllKeysAsync();

		#region search kp/imdb

		if (!string.IsNullOrWhiteSpace(search) && Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
		{
			var memkey = $"api/v1.0/torrents:{search}";

			if (!MemoryCache.TryGetValue(memkey, out (string original_name, string name) cache))
			{
				search = search.Trim();
				var uri = $"&imdb={search}";

				if (search.StartsWith("kp"))
				{
					uri = $"&kp={search.Remove(0, 2)}";
				}

				var root = await HttpClient.Get<JObject>("https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1" + uri,
					timeoutSeconds: 8);

				cache.original_name = root?.Value<JObject>("data")
					?.Value<string>("original_name");

				cache.name = root?.Value<JObject>("data")
					?.Value<string>("name");

				MemoryCache.Set(memkey, cache, DateTime.Now.AddDays(1));
			}

			if (!string.IsNullOrWhiteSpace(cache.name) && !string.IsNullOrWhiteSpace(cache.original_name))
			{
				search = cache.original_name;
				altname = cache.name;
			} else
			{
				search = cache.original_name ?? cache.name;
			}
		}

		#endregion

		#region Выборка

		var torrents = new Dictionary<string, TorrentDetails>();

		#region AddTorrents

		void AddTorrents(TorrentDetails t)
		{
			if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName))
			{
				return;
			}

			if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName))
			{
				return;
			}

			if (torrents.TryGetValue(t.url, out var val))
			{
				if (t.updateTime > val.updateTime)
				{
					torrents[t.url] = t;
				}
			} else
			{
				torrents.TryAdd(t.url, t);
			}
		}

		#endregion

		if (string.IsNullOrWhiteSpace(search) || search.Length == 1)
		{
			return Json(torrents);
		}

		var _s = StringConvert.SearchName(search);
		var _altsearch = StringConvert.SearchName(altname);

		if (exact)
		{
			#region Точный поиск

			foreach (var mdb in db.Where(i =>
						i.Key.StartsWith($"{_s}:") || i.Key.EndsWith($":{_s}") || (_altsearch != null && i.Key.Contains(_altsearch))))
			{
				foreach (var t in (await _torrentRepository.GetCollectionAsync(mdb.Key, true)).Values)
				{
					if (t.types == null)
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
					{
						var _n = t._sn ?? StringConvert.SearchName(t.name);
						var _o = t._so ?? StringConvert.SearchName(t.originalname);

						if (_n == _s || _o == _s || (_altsearch != null && (_n == _altsearch || _o == _altsearch)))
						{
							AddTorrents(t);
						}
					}
				}
			}

			#endregion
		} else
		{
			#region Поиск по совпадению ключа в имени

			var mdb = db.Where(i => i.Key.Contains(_s) || (_altsearch != null && i.Key.Contains(_altsearch)));

			if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
			{
				mdb = mdb.Take(AppInit.conf.maxreadfile);
			}

			foreach (var val in mdb)
			{
				foreach (var t in (await _torrentRepository.GetCollectionAsync(val.Key, true)).Values)
				{
					if (t.types == null)
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
					{
						AddTorrents(t);
					}
				}
			}

			#endregion
		}

		if (torrents.Count == 0)
		{
			return Json(torrents);
		}

		IEnumerable<TorrentDetails> query = torrents.Values;

		#region sort

		switch (sort ?? string.Empty)
		{
			case "sid":
				query = query.OrderByDescending(i => i.sid);

				break;

			case "pir":
				query = query.OrderByDescending(i => i.pir);

				break;

			case "size":
				query = query.OrderByDescending(i => i.size);

				break;

			case "create":
				query = query.OrderByDescending(i => i.createTime);

				break;
		}

		#endregion

		if (!string.IsNullOrWhiteSpace(tracker))
		{
			query = query.Where(i => i.trackerName == tracker);
		}

		if (relased > 0)
		{
			query = query.Where(i => i.relased == relased);
		}

		if (quality > 0)
		{
			query = query.Where(i => i.quality == quality);
		}

		if (!string.IsNullOrWhiteSpace(videotype))
		{
			query = query.Where(i => i.videotype == videotype);
		}

		if (!string.IsNullOrWhiteSpace(voice))
		{
			query = query.Where(i => i.voices.Contains(voice));
		}

		if (season > 0)
		{
			query = query.Where(i => i.seasons.Contains((int) season));
		}

		#endregion

		return Json(query.Take(2_000)
			.Select(i => new
			{
				tracker = i.trackerName,
				url = i.url != null && i.url.StartsWith("http")
					? i.url
					: null,
				i.title,
				i.size,
				i.sizeName,
				i.createTime,
				i.sid,
				i.pir,
				i.magnet,
				i.name,
				i.originalname,
				i.relased,
				i.videotype,
				i.quality,
				i.voices,
				i.seasons,
				i.types
			}));
	}

	#endregion

	#region Qualitys

	[Route("/api/v1.0/qualitys")]
	public async Task<JsonResult> Qualitys(string name, string originalname, string type, int page = 1, int take = 1000)
	{
		var torrents = new Dictionary<string, Dictionary<int, TorrentQuality>>();
		var db = await _contentCatalog.GetAllKeysAsync();

		#region AddTorrents

		void AddTorrents(TorrentDetails t)
		{
			if (t?.types == null || t.types.Contains("sport") || t.relased == 0)
			{
				return;
			}

			if (!string.IsNullOrEmpty(type) && !t.types.Contains(type))
			{
				return;
			}

			var key = $"{StringConvert.SearchName(t.name)}:{StringConvert.SearchName(t.originalname)}";

			var langs = t.languages;

			if (t.ffprobe != null || !AppInit.conf.tracks)
			{
				langs = TracksDB.Languages(t, t.ffprobe);
			} else
			{
				var streams = TracksDB.Get(t.magnet, t.types, true);
				langs = TracksDB.Languages(t, streams ?? t.ffprobe);
			}

			var model = new TorrentQuality
			{
				types = t.types.ToHashSet(),
				createTime = t.createTime,
				updateTime = t.updateTime,
				languages = langs ?? new HashSet<string>(),
				qualitys = new()
				{
					t.quality
				}
			};

			if (torrents.TryGetValue(key, out var val))
			{
				if (val.TryGetValue(t.relased, out var _md))
				{
					if (langs != null)
					{
						foreach (var item in langs)
						{
							_md.languages.Add(item);
						}
					}

					if (t.types != null)
					{
						foreach (var item in t.types)
						{
							_md.types.Add(item);
						}
					}

					_md.qualitys.Add(t.quality);

					if (_md.createTime > t.createTime)
					{
						_md.createTime = t.createTime;
					}

					if (t.updateTime > _md.updateTime)
					{
						_md.updateTime = t.updateTime;
					}

					val[t.relased] = _md;
				} else
				{
					val.TryAdd(t.relased, model);
				}

				torrents[key] = val;
			} else
			{
				torrents.TryAdd(key, new()
				{
					[t.relased] = model
				});
			}
		}

		#endregion

		var _s = StringConvert.SearchName(name);
		var _so = StringConvert.SearchName(originalname);

		var mdb = db.OrderByDescending(i => i.Value.updateTime)
			.Where(i =>
				(_s == null && _so == null) || (_s != null && i.Key.Contains(_s)) || (_so != null && i.Key.Contains(_so)));

		if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
		{
			mdb = mdb.Take(AppInit.conf.maxreadfile);
		}

		foreach (var val in mdb)
		{
			foreach (var t in (await _torrentRepository.GetCollectionAsync(val.Key, true)).Values)
			{
				AddTorrents(t);
			}
		}

		if (take == -1)
		{
			return Json(torrents);
		}

		return Json(torrents.Skip((page * take) - take)
			.Take(take));
	}

	#endregion
}