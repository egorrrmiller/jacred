using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Rutracker : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Rutracker;
    public override string TrackerName => "rutracker";
    public override string Host => AppInit.conf.Rutracker.host;
}
