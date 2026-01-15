using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.TorrentBy;

public sealed class TorrentBySearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.TorrentBy;
    public override string TrackerName => "torrentby";
    public override string Host => AppInit.conf.TorrentBy.host;
}