using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;

namespace JacRed.Infrastructure.Services.Trackers;

public abstract class BaseTrackerSearch : ITrackerSearch
{
    public abstract TrackerType Tracker { get; }
    public abstract string TrackerName { get; }
    public abstract string Host { get; }

    public virtual Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<TorrentDetails>>(Array.Empty<TorrentDetails>());
    }
}