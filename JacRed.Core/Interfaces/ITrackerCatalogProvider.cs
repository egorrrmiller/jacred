using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITrackerCatalogProvider
{
    TrackerType Tracker { get; }
    string TrackerName { get; }
    Task<IReadOnlyCollection<TorrentDetails>> FetchCatalogAsync(CancellationToken cancellationToken = default);
}
