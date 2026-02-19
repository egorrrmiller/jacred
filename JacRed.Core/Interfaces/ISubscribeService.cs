namespace JacRed.Core.Interfaces;

public interface ISubscribeService
{
    Task SubscribeAsync(long tmdbId, string uid);
    Task UnSubscribeAsync(long tmdbId, string uid);
    Task<bool> CheckSubscribeAsync(long tmdbId, string uid);
}