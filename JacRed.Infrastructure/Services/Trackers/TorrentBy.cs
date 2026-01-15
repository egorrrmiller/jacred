using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class TorrentBy : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.TorrentBy;
    public override string TrackerName => "torrentby";
    public override string Host => AppInit.conf.TorrentBy.host;
}
