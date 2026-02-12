using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Interfaces;

public interface ITorrentRepository
{
    Task AddOrUpdateAsync(IReadOnlyCollection<TorrentDetails> torrents);

    Task AddOrUpdateAsync<T>(IReadOnlyCollection<T> torrents,
        Func<T, Task<bool>> predicate)
        where T : TorrentDetails;

    Task<List<TorrentDetails>> GetStaleAsync(TimeSpan olderThan, int limit);

    /// <summary>
    ///     Возвращает торренты конкретного трекера, опционально — только старше заданного срока, с лимитом.
    /// </summary>
    Task<List<TorrentDetails>> GetByTrackerAsync(string trackerName, TimeSpan? olderThan = null, int? limit = null);

    Task<IReadOnlyCollection<string>> GetSearchQueriesAsync(int limit);
    Task TrackSearchQueryAsync(string query);

    Task<List<TorrentDetails>> GetForMediaProbeAsync(int limit, int maxAttempts,
        IReadOnlyCollection<string>? excludedTypes = null);

    Task UpdateMediaProbeAsync(string url, List<FfStream> ffprobe, HashSet<string>? languages);

    Task IncrementMediaProbeAttemptsAsync(string url);
}