using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Tracker refresh stub. Defaults to SearchAsync.
/// </summary>
public interface ITrackerRefreshProvider : ITrackerSearch
{
    Task<IReadOnlyCollection<TorrentDetails>> RefreshAsync(string query)
        => SearchAsync(query);
}
