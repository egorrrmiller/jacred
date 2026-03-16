using JacRett.Core.Models.Database;

namespace JacRett.Core.Interfaces;

public interface ISubscriptionRepository
{
    Task AddAsync(Subscription subscription);
    Task RemoveAsync(long tmdbId, string uid, string? media = null);
    Task<bool> ExistsAsync(long tmdbId, string uid, string? media = null);
}