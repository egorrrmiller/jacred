using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITrackerCronProvider
{
    TrackerType Tracker { get; }
    string TrackerName { get; }
    string Url { get; }
    Task<IReadOnlyCollection<TorrentDetails>> FetchCatalogAsync(CancellationToken cancellationToken = default);
}