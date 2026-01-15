using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Kinozal : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Kinozal;
    public override string TrackerName => "kinozal";
    public override string Host => AppInit.conf.Kinozal.host;
}
