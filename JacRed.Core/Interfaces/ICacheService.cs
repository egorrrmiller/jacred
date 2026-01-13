namespace JacRed.Core.Interfaces;

public interface ICacheService
{
	public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);

	public Task InvalidateAsync(string key);

	public Task ClearAsync();
}