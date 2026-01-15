using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Lostfilm : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Lostfilm;
    public override string TrackerName => "lostfilm";
    public override string Host => AppInit.conf.Lostfilm.host;
}
