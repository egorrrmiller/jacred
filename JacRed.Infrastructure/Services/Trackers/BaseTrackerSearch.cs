using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;

namespace JacRed.Infrastructure.Services.Trackers;

public abstract class BaseTrackerSearch : ITrackerRefreshProvider
{
    public abstract TrackerType Tracker { get; }
    public abstract string TrackerName { get; }
    public abstract string Host { get; }

    public virtual Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        return Task.FromResult<IReadOnlyCollection<TorrentDetails>>([]);
    }

    public virtual Task RefreshAsync()
    {
        return Task.CompletedTask;
    }
}