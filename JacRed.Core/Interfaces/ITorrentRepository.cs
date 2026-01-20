using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentRepository
{
    Task AddOrUpdateAsync(IReadOnlyCollection<TorrentDetails> torrents);

    Task AddOrUpdateAsync<T>(
        IReadOnlyCollection<T> torrents,
        Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate)
        where T : TorrentDetails;

    Task<List<TorrentDetails>> GetStaleAsync(TimeSpan olderThan, int limit);
    Task<IReadOnlyCollection<string>> GetSearchQueriesAsync(int limit);
    Task TrackSearchQueryAsync(string query);
    
    /// <summary>
    ///     Возвращает время последнего обновления любого торрента в БД.
    /// </summary>
    Task<DateTime> GetLastUpdateTimeAsync();
}