using System;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Services;

public class CacheService : ICacheService
{
	private readonly IMemoryCache _cache;

	public CacheService(IMemoryCache cache)
	{
		_cache = cache;
	}

	public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
	{
		if (_cache.TryGetValue(key, out T cached))
		{
			return cached;
		}

		var result = await factory();

		var options = new MemoryCacheEntryOptions();

		if (expiry.HasValue)
		{
			options.AbsoluteExpirationRelativeToNow = expiry.Value;
		} else
		{
			options.SlidingExpiration = TimeSpan.FromHours(1);
		}

		_cache.Set(key, result, options);

		return result;
	}

	public async Task InvalidateAsync(string key)
	{
		_cache.Remove(key);
		await Task.CompletedTask;
	}

	public async Task ClearAsync()
	{
		if (_cache is MemoryCache memoryCache)
		{
			memoryCache.Compact(1.0);
		}

		await Task.CompletedTask;
	}
}