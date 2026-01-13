using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Services;

public class FileTorrentRepository : ITorrentRepository
{
	private readonly ICacheService _cache;

	private readonly IContentCatalog _contentCatalog;

	private readonly IKeyGenerator _keyGenerator;

	private readonly IPathResolver _pathResolver;

	private readonly ITorrentEnricher _torrentEnricher;

	public FileTorrentRepository(IContentCatalog contentCatalog,
								ICacheService cache,
								IPathResolver pathResolver,
								IKeyGenerator keyGenerator, ITorrentEnricher torrentEnricher)
	{
		_contentCatalog = contentCatalog;
		_cache = cache;
		_pathResolver = pathResolver;
		_keyGenerator = keyGenerator;
		_torrentEnricher = torrentEnricher;
	}

	public async Task AddOrUpdateAsync(IReadOnlyCollection<TorrentBaseDetails> torrents)
	{
		var grouped = torrents.GroupBy(t => _keyGenerator.Build(t.name, t.originalname));

		foreach (var group in grouped)
		{
			using var collection = await GetWritableCollectionAsync(group.Key);

			foreach (var torrent in group)
			{
				await collection.AddOrUpdate(torrent);
			}
		}
	}

	public async Task AddOrUpdateAsync<T>(
		IReadOnlyCollection<T> torrents,
		Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate
	) where T : TorrentBaseDetails
	{
		// Группируем торренты по ключу как в оригинале
		var temp = new Dictionary<string, List<T>>();

		foreach (var torrent in torrents)
		{
			string key = _keyGenerator.Build(torrent.name, torrent.originalname);
			if (!temp.ContainsKey(key))
				temp.Add(key, new List<T>());

			temp[key].Add(torrent);
		}

		// Обрабатываем каждую группу
		foreach (var group in temp)
		{
			var collection = await GetWritableCollectionAsync(group.Key);

			foreach (var torrent in group.Value)
			{
				if (predicate != null)
				{
					var existingData = await GetCollectionAsync(group.Key);
					if (await predicate.Invoke(torrent, existingData) == false)
						continue;
				}

				await _torrentEnricher.EnrichAndConvertAsync(torrent);
				await collection.AddOrUpdate(torrent);
			}

			await collection.SaveAsync();
		}
	}

	public async Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key,
																					bool updateCache = false)
	{
		var cacheKey = $"collection:{key}";

		if (!updateCache)
		{
			return await _cache.GetOrCreateAsync(cacheKey,
				async () => await LoadCollectionFromFileAsync(key),
				TimeSpan.FromHours(1));
		}

		// Принудительная загрузка при обновлении кэша
		var result = await LoadCollectionFromFileAsync(key);
		await _cache.InvalidateAsync(cacheKey);

		return result;
	}

	public async Task<ITorrentCollection> GetWritableCollectionAsync(string key)
	{
		var cacheKey = $"writable_collection:{key}";

		return await _cache.GetOrCreateAsync(cacheKey, async () =>
		{
			var data = await LoadCollectionFromFileAsync(key);

			return new TorrentCollection(key, data, _pathResolver, _torrentEnricher);
		}, TimeSpan.FromMinutes(30));
	}

	public async Task SaveMasterIndexAsync()
	{
		if (_contentCatalog is ContentCatalogService catalogService)
		{
			await catalogService.SaveToFileAsync();
		}
	}

	public async Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync() => await _contentCatalog.GetAllKeysAsync();

	private async Task<IReadOnlyDictionary<string, TorrentDetails>> LoadCollectionFromFileAsync(string key)
	{
		var filePath = _pathResolver.GenerateFilePath(key);

		if (!File.Exists(filePath))
		{
			return new Dictionary<string, TorrentDetails>();
		}

		try
		{
			// Используем JsonStream.Read как в оригинальном коде
			var result = JsonStream.Read<Dictionary<string, TorrentDetails>>(filePath);
			return result;
		}
		catch (Exception ex)
		{
			return new Dictionary<string, TorrentDetails>();
		}
	}
}