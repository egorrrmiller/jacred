/*using System.Globalization;
using System.Text.RegularExpressions;
using JacRed.Api.Engine.Tracks;
using JacRed.Core;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.FileDB;

public partial class FileDB : IDisposable
{


	#region Dispose

	public void Dispose()
	{
		if (Database.Count > 0 && savechanges)
		{
			JsonStream.Write(pathDb(fdbkey), Database);
		}

		if (openWriteTask.TryGetValue(fdbkey, out var val))
		{
			val.openconnection -= 1;

			if (0 >= val.openconnection)
			{
				if (!AppInit.conf.evercache.enable || (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour > 0))
				{
					openWriteTask.TryRemove(fdbkey, out var _);
				}
			}
		}
	}

	#endregion

	#region AddOrUpdate

	public void AddOrUpdate(TorrentBaseDetails torrent)
	{
		if (Database.TryGetValue(torrent.url, out var t))
		{
			var updateFull = false;

			void upt(bool uptfull = false, bool updatetime = true)
			{
				savechanges = true;

				if (updatetime)
				{
					t.updateTime = DateTime.UtcNow;
				}

				if (uptfull)
				{
					updateFull = true;
				}
			}

			#region types

			if (torrent.types != null)
			{
				if (t.types == null)
				{
					t.types = torrent.types;
					upt(true);
				} else
				{
					foreach (var type in torrent.types)
					{
						if (type != null && !t.types.Contains(type))
						{
							upt(true);
						}
					}

					t.types = torrent.types;
				}
			}

			#endregion

			if (torrent.trackerName != t.trackerName)
			{
				t.trackerName = torrent.trackerName;
				upt(true);
			}

			if (torrent.title != t.title)
			{
				t.title = torrent.title;
				upt(true);
			}

			if (!string.IsNullOrWhiteSpace(torrent.magnet) && torrent.magnet != t.magnet)
			{
				t.ffprobe_tryingdata = 0;
				t.magnet = torrent.magnet;
				upt();
			}

			if (torrent.sid != t.sid)
			{
				t.sid = torrent.sid;
				upt(updatetime: false);
			}

			if (torrent.pir != t.pir)
			{
				t.pir = torrent.pir;
				upt(updatetime: false);
			}

			if (!string.IsNullOrWhiteSpace(torrent.sizeName) && torrent.sizeName != t.sizeName)
			{
				t.sizeName = torrent.sizeName;
				upt(true);
			}

			if (!string.IsNullOrWhiteSpace(torrent.name) && torrent.name != t.name)
			{
				t.name = torrent.name;
				t._sn = StringConvert.SearchName(t.name);
				upt();
			}

			if (!string.IsNullOrWhiteSpace(torrent.originalname) && torrent.originalname != t.originalname)
			{
				t.originalname = torrent.originalname;
				t._so = StringConvert.SearchName(t.originalname);
				upt();
			}

			if (torrent.relased > 0 && torrent.relased != t.relased)
			{
				t.relased = torrent.relased;
				upt();
			}

			if (torrent.ffprobe != null && t.ffprobe == null)
			{
				t.ffprobe = torrent.ffprobe;
				upt();
			}

			if (updateFull)
			{
				updateFullDetails(t);
			} else if (AppInit.conf.log)
			{
				File.AppendAllText("Data/log/fdb.txt",
					JsonConvert.SerializeObject(new List<TorrentBaseDetails>
					{
						torrent,
						t
					}, Formatting.Indented)
					+ ",\n\n");
			}

			t.checkTime = DateTime.Now;
			AddOrUpdateMasterDb(t);
		} else
		{
			if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
			{
				return;
			}

			t = new()
			{
				url = torrent.url,
				types = torrent.types,
				trackerName = torrent.trackerName,
				createTime = torrent.createTime,
				updateTime = torrent.updateTime,
				title = torrent.title,
				name = torrent.name,
				originalname = torrent.originalname,
				pir = torrent.pir,
				sid = torrent.sid,
				relased = torrent.relased,
				sizeName = torrent.sizeName,
				magnet = torrent.magnet,
				ffprobe = torrent.ffprobe
			};

			savechanges = true;
			updateFullDetails(t);
			Database.TryAdd(t.url, t);
			AddOrUpdateMasterDb(t);
		}
	}

	#endregion

	#region updateFullDetails

	public static void updateFullDetails(TorrentDetails t)
	{
		#region getSizeInfo

		long getSizeInfo(string sizeName)
		{
			if (string.IsNullOrWhiteSpace(sizeName))
			{
				return 0;
			}

			try
			{
				var size = 0.1;

				var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase)
					.Groups;

				if (!string.IsNullOrWhiteSpace(gsize[2].Value))
				{
					if (double.TryParse(gsize[1]
								.Value.Replace(",", "."), NumberStyles.Any,
							CultureInfo.InvariantCulture, out size)
						&& size != 0)
					{
						if (gsize[2]
								.Value.ToLower() is "gb" or "гб")
						{
							size *= 1024;
						}

						if (gsize[2]
								.Value.ToLower() is "tb" or "тб")
						{
							size *= 1048576;
						}

						return (long) (size * 1048576);
					}
				}
			}
			catch
			{
			}

			return 0;
		}

		#endregion

		t.size = getSizeInfo(t.sizeName);

		#region quality

		t.quality = 480;

		if (t.quality == 480)
		{
			if (t.title.Contains("720p"))
			{
				t.quality = 720;
			} else if (t.title.Contains("1080p"))
			{
				t.quality = 1080;
			} else if (Regex.IsMatch(t.title.ToLower(), "(4k|uhd)( |\\]|,|$)") || t.title.Contains("2160p"))

				// Вышел после 2000г
				// Размер файла выше 10GB
				// Есть пометка о 4K
			{
				t.quality = 2160;
			}
		}

		#endregion

		var titlelower = t.title.ToLower();

		#region videotype

		t.videotype = "sdr";

		if (Regex.IsMatch(titlelower, "(\\[|,| )hdr(10| |\\]|,|$)") || Regex.IsMatch(titlelower, "(10-bit|10 bit|10-бит|10 бит|hdr10)"))
		{
			if (!Regex.IsMatch(titlelower, "(\\[|,| )sdr( |\\]|,|$)"))
			{
				t.videotype = "hdr";
			}
		}

		#endregion

		#region voice

		t.voices = new();

		if (t.trackerName == "lostfilm")
		{
			t.voices.Add("LostFilm");
		} else if (t.trackerName == "hdrezka")
		{
			t.voices.Add("HDRezka");
		}

		if (Regex.IsMatch(titlelower, "( |x)(d|dub|дб|дуб|дубляж)(,| )"))
		{
			t.voices.Add("Дубляж");
		}

		foreach (var v in allVoices)
		{
			try
			{
				if (v.Length > 4 && titlelower.Contains(v.ToLower()))
				{
					t.voices.Add(v);
				}
			}
			catch
			{
			}
		}

		var streams = TracksDB.Get(t.magnet, t.types);

		if (streams != null)
		{
			foreach (var s in streams)
			{
				if (string.IsNullOrEmpty(s.tags?.title))
				{
					continue;
				}

				if (s.codec_type != "audio")
				{
					continue;
				}

				foreach (var v in allVoices)
				{
					try
					{
						if (v.Length > 4
							&& s.tags.title.ToLower()
								.Contains(v.ToLower()))
						{
							t.voices.Add(v);
						}

						if (Regex.IsMatch(s.tags.title.ToLower(), "( |x)(d|dub|дб|дуб|дубляж)(,| )"))
						{
							t.voices.Add("Дубляж");
						}
					}
					catch
					{
					}
				}
			}
		}

		#endregion

		#region languages

		t.languages = new();

		if (titlelower.Contains("ukr") || titlelower.Contains("українськ") || titlelower.Contains("украинск") || t.trackerName == "toloka")
		{
			t.languages.Add("ukr");
		}

		if (t.trackerName == "lostfilm")
		{
			t.languages.Add("rus");
		}

		if (!t.languages.Contains("ukr"))
		{
			foreach (var v in ukrVoices)
			{
				if (t.voices.Contains(v))
				{
					t.languages.Add("ukr");

					break;
				}
			}
		}

		if (!t.languages.Contains("rus"))
		{
			foreach (var v in rusVoices)
			{
				if (t.voices.Contains(v))
				{
					t.languages.Add("rus");

					break;
				}
			}
		}

		#endregion

		#region seasons

		t.seasons = new();

		if (t.types != null)
		{
			try
			{
				if (t.types.Contains("serial")
					|| t.types.Contains("multserial")
					|| t.types.Contains("docuserial")
					|| t.types.Contains("tvshow")
					|| t.types.Contains("anime"))
				{
					if (Regex.IsMatch(t.title, "([0-9]+(\\-[0-9]+)?x[0-9]+|сезон|s[0-9]+)", RegexOptions.IgnoreCase))
					{
						if (Regex.IsMatch(t.title, "([0-9]+\\-[0-9]+x[0-9]+|[0-9]+\\-[0-9]+ сезон|s[0-9]+\\-[0-9]+)",
								RegexOptions.IgnoreCase))
						{
							#region Несколько сезонов

							int startSeason = 0,
								endSeason = 0;

							if (Regex.IsMatch(t.title, "[0-9]+x[0-9]+", RegexOptions.IgnoreCase))
							{
								var g = Regex.Match(t.title, "([0-9]+)\\-([0-9]+)x", RegexOptions.IgnoreCase)
									.Groups;

								int.TryParse(g[1].Value, out startSeason);
								int.TryParse(g[2].Value, out endSeason);
							} else if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
							{
								var g = Regex.Match(t.title, "([0-9]+)\\-([0-9]+) сезон", RegexOptions.IgnoreCase)
									.Groups;

								int.TryParse(g[1].Value, out startSeason);
								int.TryParse(g[2].Value, out endSeason);
							} else if (Regex.IsMatch(t.title, "s[0-9]+", RegexOptions.IgnoreCase))
							{
								var g = Regex.Match(t.title, "s([0-9]+)\\-([0-9]+)", RegexOptions.IgnoreCase)
									.Groups;

								int.TryParse(g[1].Value, out startSeason);
								int.TryParse(g[2].Value, out endSeason);
							}

							if (startSeason > 0 && endSeason > startSeason)
							{
								for (var s = startSeason; s <= endSeason; s++)
								{
									t.seasons.Add(s);
								}
							}

							#endregion
						}

						if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
						{
							#region Один сезон

							if (Regex.IsMatch(t.title, "[0-9]+ сезон", RegexOptions.IgnoreCase))
							{
								if (int.TryParse(Regex.Match(t.title, "([0-9]+) сезон", RegexOptions.IgnoreCase)
											.Groups[1].Value,
										out var s)
									&& s > 0)
								{
									t.seasons.Add(s);
								}
							}

							#endregion
						} else if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+\\-[0-9]+", RegexOptions.IgnoreCase))
						{
							#region Несколько сезонов

							int startSeason = 0,
								endSeason = 0;

							if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+", RegexOptions.IgnoreCase))
							{
								var g = Regex.Match(t.title, "сезон(ы|и)?:? ([0-9]+)\\-([0-9]+)",
										RegexOptions.IgnoreCase)
									.Groups;

								int.TryParse(g[2].Value, out startSeason);
								int.TryParse(g[3].Value, out endSeason);
							}

							if (startSeason > 0 && endSeason > startSeason)
							{
								for (var s = startSeason; s <= endSeason; s++)
								{
									t.seasons.Add(s);
								}
							}

							#endregion
						} else
						{
							#region Один сезон

							if (Regex.IsMatch(t.title, "[0-9]+x[0-9]+", RegexOptions.IgnoreCase))
							{
								if (int.TryParse(Regex.Match(t.title, "([0-9]+)x", RegexOptions.IgnoreCase)
											.Groups[1].Value,
										out var s)
									&& s > 0)
								{
									t.seasons.Add(s);
								}
							} else if (Regex.IsMatch(t.title, "сезон(ы|и)?:? [0-9]+", RegexOptions.IgnoreCase))
							{
								if (int.TryParse(Regex.Match(t.title, "сезон(ы|и)?:? ([0-9]+)", RegexOptions.IgnoreCase)
										.Groups[2].Value, out var s)
									&& s > 0)
								{
									t.seasons.Add(s);
								}
							} else if (Regex.IsMatch(t.title, "s[0-9]+", RegexOptions.IgnoreCase))
							{
								if (int.TryParse(Regex.Match(t.title, "s([0-9]+)", RegexOptions.IgnoreCase)
											.Groups[1].Value,
										out var s)
									&& s > 0)
								{
									t.seasons.Add(s);
								}
							}

							#endregion
						}
					}
				}
			}
			catch
			{
			}
		}

		#endregion
	}

	#endregion

	#region FileDB

	private readonly string fdbkey;

	public bool savechanges;

	private FileDB(string key)
	{
		fdbkey = key;
		var fdbpath = pathDb(key);

		if (File.Exists(fdbpath))
		{
			Database = JsonStream.Read<Dictionary<string, TorrentDetails>>(fdbpath) ?? new Dictionary<string, TorrentDetails>();
		}
	}

	public Dictionary<string, TorrentDetails> Database = new();

	#endregion
}*/