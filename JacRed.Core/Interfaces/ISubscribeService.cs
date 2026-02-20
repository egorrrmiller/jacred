namespace JacRed.Core.Interfaces;

public interface ISubscribeService
{
    Task<bool> SubscribeAsync(long tmdbId, string uid);
    Task<bool> UnSubscribeAsync(long tmdbId, string uid);
    Task<bool> CheckSubscribeAsync(long tmdbId, string uid);
}