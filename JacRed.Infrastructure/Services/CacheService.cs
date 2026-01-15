using JacRed.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Обертка над IMemoryCache для работы с кэшем приложения.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    ///     Возвращает значение из кэша или создаёт и сохраняет его с заданным сроком жизни.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
    {
        if (_cache.TryGetValue(key, out T cached)) return cached;

        var result = await factory();

        var options = new MemoryCacheEntryOptions
        {
            Size = 1024 * 1024 * 150
        };

        if (expiry.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiry.Value;
        else
            options.SlidingExpiration = TimeSpan.FromHours(1);

        _cache.Set(key, result, options);

        return result;
    }

    /// <summary>
    ///     Удаляет значение из кэша по ключу.
    /// </summary>
    public async Task InvalidateAsync(string key)
    {
        _cache.Remove(key);
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Сохраняет значение в кэше с опциональным сроком жизни.
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            Size = 1024 * 1024 * 150
        };

        if (expiry.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiry.Value;
        else
            options.SlidingExpiration = TimeSpan.FromHours(1);

        _cache.Set(key, value, options);

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Пытается получить значение из кэша без исключений на промахе.
    /// </summary>
    public bool TryGetValue<T>(string key, out T? value)
    {
        return _cache.TryGetValue(key, out value);
    }

    /// <summary>
    ///     Полностью очищает кэш путём принудительного компактирования.
    /// </summary>
    public async Task ClearAsync()
    {
        if (_cache is MemoryCache memoryCache) memoryCache.Compact(1.0);

        await Task.CompletedTask;
    }
}
