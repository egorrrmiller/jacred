using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITrackerSearch
{
    TrackerType Tracker { get; }
    string TrackerName { get; }
    string Host { get; }

    Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}