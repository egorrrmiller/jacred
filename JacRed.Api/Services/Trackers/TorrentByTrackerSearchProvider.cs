using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class TorrentByTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.TorrentBy;
    public override string TrackerName => "torrentby";
    public override string Host => AppInit.conf.TorrentBy.host;
}
