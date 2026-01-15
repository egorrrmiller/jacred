namespace JacRed.Core.Interfaces;

public interface IQueryStatsService
{
    Task TrackAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetTopQueriesAsync(int take, CancellationToken cancellationToken = default);
}
