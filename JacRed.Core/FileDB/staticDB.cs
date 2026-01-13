/*using System.Collections.Concurrent;
using JacRed.Core;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.FileDB;

public partial class FileDB : IDisposable
{
	// TODO ПЕРЕНЕСЕНО В MasterIndexService - Update

	#region AddOrUpdateMasterDb

	private static void AddOrUpdateMasterDb(TorrentDetails torrent)
	{
		var key = keyDb(torrent.name, torrent.originalname);

		var md = new TorrentInfo
		{
			updateTime = torrent.updateTime,
			fileTime = torrent.updateTime.ToFileTimeUtc()
		};

		if (masterDb.TryGetValue(key, out var info))
		{
			if (torrent.updateTime > info.updateTime)
			{
				masterDb[key] = md;
			}
		} else
		{
			masterDb.TryAdd(key, md);
		}
	}

	#endregion

	//todo ВЫНЕСЕНО В MasterIndexService

	#region SaveChangesToFile

	public static void SaveChangesToFile()
	{
		try
		{
			JsonStream.Write("Data/masterDb.bz", masterDb);

			if (!File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
			{
				File.Copy("Data/masterDb.bz", $"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");
			}

			if (File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz"))
			{
				File.Delete($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz");
			}
		}
		catch
		{
		}
	}

	#endregion

	#region FileDB

	/// <summary>
	/// $"{search_name}:{search_originalname}"
	/// Верхнее время изменения
	/// </summary>
	public static ConcurrentDictionary<string, TorrentInfo> masterDb = new();

	private static readonly ConcurrentDictionary<string, WriteTaskModel> openWriteTask = new();

	static FileDB()
	{
		/*if (File.Exists("Data/masterDb.bz"))
		{
			masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>("Data/masterDb.bz");
		}

		if (masterDb == null)
		{
			if (File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
			{
				masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>(
					$"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");
			}

			if (masterDb == null && File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz"))
			{
				masterDb = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>(
					$"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz");
			}

			if (masterDb == null)
			{
				masterDb = new ConcurrentDictionary<string, TorrentInfo>();
			}

			#region переход с 29.08.2023

			if (File.Exists("Data/masterDb.bz"))
			{
				try
				{
					foreach (var item in JsonStream.Read<Dictionary<string, DateTime>>("Data/masterDb.bz"))
					{
						masterDb.TryAdd(item.Key,
							new TorrentInfo { updateTime = item.Value, fileTime = item.Value.ToFileTimeUtc() });
					}

					if (masterDb.Count > 0)
					{
						JsonStream.Write("Data/masterDb.bz", masterDb);
						return;
					}
				}
				catch
				{
				}
			}

			#endregion

			if (File.Exists("lastsync.txt"))
			{
				File.Delete("lastsync.txt");
			}
		}#1#
	}

	#endregion

	//todo ВЫНЕСЕНО В FileService

	#region pathDb / keyDb

	private static string pathDb(string key)
	{
		var md5key = HashTo.Md5(key);

		if (AppInit.conf.fdbPathLevels == 2)
		{
			Directory.CreateDirectory($"Data/fdb/{md5key.Substring(0, 2)}");

			return $"Data/fdb/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";
		}

		Directory.CreateDirectory($"Data/fdb/{md5key[0]}");

		return $"Data/fdb/{md5key[0]}/{md5key}";
	}

	private static string keyDb(string name, string originalname)
	{
		var search_name = StringConvert.SearchName(name);
		var search_originalname = StringConvert.SearchName(originalname);

		return $"{search_name}:{search_originalname}";
	}

	#endregion

	#region OpenRead / OpenWrite

	public static IReadOnlyDictionary<string, TorrentDetails> OpenRead(string key, bool update_lastread = false,
																		bool cache = true)
	{
		if (openWriteTask.TryGetValue(key, out var val))
		{
			if (update_lastread)
			{
				val.countread++;
				val.lastread = DateTime.UtcNow;
			}

			return val.db.Database;
		}

		var fdb = new FileDB(key);

		if (AppInit.conf.evercache.enable && (cache || AppInit.conf.evercache.validHour == 0))
		{
			var wtm = new WriteTaskModel
			{
				db = fdb,
				openconnection = 1
			};

			if (update_lastread)
			{
				wtm.countread++;
				wtm.lastread = DateTime.UtcNow;
			}

			openWriteTask.TryAdd(key, wtm);
		}

		return fdb.Database;
	}

	public static FileDB OpenWrite(string key)
	{
		if (openWriteTask.TryGetValue(key, out var val))
		{
			val.openconnection += 1;

			return val.db;
		}

		var fdb = new FileDB(key);

		openWriteTask.TryAdd(key, new()
		{
			db = fdb,
			openconnection = 1
		});

		return fdb;
	}

	#endregion

	#region AddOrUpdate

	public static void AddOrUpdate(IReadOnlyCollection<TorrentBaseDetails> torrents) => _ = AddOrUpdate(torrents, null);

	public static async Task AddOrUpdate<T>(IReadOnlyCollection<T> torrents,
											Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate)
		where T : TorrentBaseDetails
	{
		var temp = new Dictionary<string, List<T>>();

		foreach (var torrent in torrents)
		{
			var key = keyDb(torrent.name, torrent.originalname);

			if (!temp.ContainsKey(key))
			{
				temp.Add(key, new());
			}

			temp[key]
				.Add(torrent);
		}

		foreach (var t in temp)
		{
			using (var fdb = OpenWrite(t.Key))
			{
				foreach (var torrent in t.Value)
				{
					if (predicate != null)
					{
						if (!await predicate.Invoke(torrent, fdb.Database))
						{
							continue;
						}
					}

					fdb.AddOrUpdate(torrent);
				}
			}
		}
	}

	#endregion

	#region Cron

	public static async Task Cron()
	{
		while (true)
		{
			await Task.Delay(TimeSpan.FromMinutes(10));

			if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
			{
				continue;
			}

			try
			{
				foreach (var i in openWriteTask)
				{
					if (DateTime.UtcNow > i.Value.lastread.AddHours(AppInit.conf.evercache.validHour))
					{
						openWriteTask.TryRemove(i.Key, out var _);
					}
				}
			}
			catch
			{
			}
		}
	}

	public static async Task CronFast()
	{
		while (true)
		{
			await Task.Delay(TimeSpan.FromSeconds(20));

			if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
			{
				continue;
			}

			try
			{
				if (openWriteTask.Count > AppInit.conf.evercache.maxOpenWriteTask)
				{
					var query = openWriteTask.Where(i => DateTime.Now > i.Value.create.AddMinutes(10));

					query = query.OrderBy(i => i.Value.countread)
						.ThenBy(i => i.Value.lastread);

					foreach (var i in query.Take(AppInit.conf.evercache.dropCacheTake))
					{
						openWriteTask.TryRemove(i.Key, out var _);
					}
				}
			}
			catch
			{
			}
		}
	}

	#endregion
}*/