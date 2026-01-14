using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITrackerSearchService
{
    IReadOnlyCollection<TrackerType> GetSupportedTrackers();

    Task<IReadOnlyCollection<TorrentBaseDetails>> SearchAsync(
        string query,
        IReadOnlyCollection<TrackerType>? trackers = null,
        CancellationToken cancellationToken = default);
}
