namespace JacRed.Core.Interfaces;

/// <summary>
///     Простой интерфейс для кэширования объектов
/// </summary>
public interface ICacheService
{
    /// <summary>
    ///     Получает значение из кэша или создаёт его
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);

    /// <summary>
    ///     Инвалидирует значение в кэше
    /// </summary>
    Task InvalidateAsync(string key);

    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

    bool TryGetValue<T>(string key, out T? value);
}