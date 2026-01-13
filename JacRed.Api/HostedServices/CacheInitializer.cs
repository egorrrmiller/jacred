using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Models;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

namespace JacRed.Api.HostedServices;

public class CacheInitializer : IHostedService
{
	private readonly IMemoryCache _cache;

	public CacheInitializer(IMemoryCache cache) => _cache = cache;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			// Список файлов в порядке приоритета
			var filesToCheck = new List<string>();

			// Добавляем masterDb.bz как первый приоритет
			filesToCheck.Add("Data/masterDb.bz");

			// Добавляем последние 60 дней
			for (int i = 0; i < 60; i++)
			{
				filesToCheck.Add($"Data/masterDb_{DateTime.Today.AddDays(-i):dd-MM-yyyy}.bz");
			}

			foreach (var file in filesToCheck)
			{
				if (!File.Exists(file))
					continue;

				var data = JsonStream.Read<ConcurrentDictionary<string, TorrentInfo>>(file);

				if (data != null && data.Count > 0)
				{
					_cache.Set("catalog:all_keys", data);
					return Task.CompletedTask; // нашли непустой, выходим
				}
			}

			// Переход с 29.08.2023 — старый формат
			if (File.Exists("Data/masterDb.bz"))
			{
				try
				{
					var legacyData = JsonStream.Read<Dictionary<string, DateTime>>("Data/masterDb.bz");
					if (legacyData != null && legacyData.Count > 0)
					{
						foreach (var item in legacyData)
						{
							_cache.Set(item.Key, new TorrentInfo
							{
								updateTime = item.Value,
								fileTime = item.Value.ToFileTimeUtc()
							});
						}

						if (!_cache.TryGetValue("catalog:all_keys", out _))
							JsonStream.Write("Data/masterDb.bz", _cache);

						return Task.CompletedTask;
					}
				}
				catch
				{
					// игнорируем ошибки старого формата
				}
			}

			// Если ничего не загрузилось — создаём пустой кэш
			_cache.Set("catalog:all_keys", new ConcurrentDictionary<string, TorrentInfo>());

			// Удаляем временный файл lastsync.txt, если есть
			if (File.Exists("lastsync.txt"))
				File.Delete("lastsync.txt");
		}
		catch
		{
			// можно добавить логирование
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
