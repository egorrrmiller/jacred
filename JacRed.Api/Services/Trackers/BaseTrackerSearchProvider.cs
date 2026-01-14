using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;

namespace JacRed.Api.Services.Trackers;

public abstract class BaseTrackerSearchProvider : ITrackerSearchProvider
{
    public abstract Core.Enums.TrackerType Tracker { get; }
    public abstract string TrackerName { get; }
    public abstract string Host { get; }

    public virtual Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<TorrentDetails>>(Array.Empty<TorrentDetails>());
    }
}
