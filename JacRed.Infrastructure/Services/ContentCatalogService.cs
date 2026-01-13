using System.Collections.Concurrent;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Services;

public class ContentCatalogService : IContentCatalog
{
	private readonly ICacheService _cache;
	private readonly IMemoryCache _contentCatalog;

	//private readonly ConcurrentDictionary<string, TorrentInfo> _contentCatalog;

	private readonly ILogger<ContentCatalogService> _logger;

	public ContentCatalogService(ICacheService cache, ILogger<ContentCatalogService> logger, IMemoryCache contentCatalog)
	{
		//_contentCatalog = new();
		_cache = cache;
		_logger = logger;
		_contentCatalog = contentCatalog;
	}

	public bool IsUpdated(string key, DateTime updatedTime)
	{
		/*if (_contentCatalog.TryGetValue(key, out var info))
		{
			return updatedTime <= info.updateTime;
		}*/

		return false;
	}

	public void Update(string key, DateTime updatedTime)
	{
		/*var info = new TorrentInfo
		{
			updateTime = updatedTime,
			fileTime = updatedTime.ToFileTimeUtc()
		};

		_contentCatalog.AddOrUpdate(key, info, (k, v) => updatedTime > v.updateTime
			? info
			: v);*/
	}

	public async Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync()
	{
		var cacheKey = "catalog:all_keys";
		var tt = _contentCatalog.TryGetValue(cacheKey, out ConcurrentDictionary<string, TorrentInfo>? value);

		if (tt)
			return value;

		return new ConcurrentDictionary<string, TorrentInfo>();
	}

	public async Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false)
	{
		var cacheKey = "catalog:fast_index";

		return await _cache.GetOrCreateAsync(cacheKey, async () =>
		{
			var fastdb = new Dictionary<string, List<string>>();

			foreach (var item in await GetAllKeysAsync())
			{
				foreach (var k in item.Key.Split(':', StringSplitOptions.RemoveEmptyEntries))
				{
					if (!fastdb.TryGetValue(k, out var list))
					{
						list = new();
						fastdb[k] = list;
					}

					list.Add(item.Key);
				}
			}

			return fastdb;

		}, TimeSpan.FromHours(1));
	}

	public DateTime GetLastUpdateTime(string key) => _contentCatalog.TryGetValue(key, out TorrentInfo info)
		? info.updateTime
		: DateTime.MinValue;

	public async Task SaveToFileAsync()
	{
		try
		{
			var data = _contentCatalog.TryGetValue("catalog:all_keys", out ConcurrentDictionary<string, TorrentInfo>? value);

			if(!data)
				return;

			JsonStream.Write("Data/masterDb.bz", data);

			// Создание бэкапа
			var backupFile = $"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz";

			if (!File.Exists(backupFile))
			{
				File.Copy("Data/masterDb.bz", backupFile);
			}

			// Очистка старых бэкапов
			var oldBackup = $"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz";

			if (File.Exists(oldBackup))
			{
				File.Delete(oldBackup);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving master index to file");
		}
	}
}