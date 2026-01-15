using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Kinozal;

public sealed class KinozalSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Kinozal;
    public override string TrackerName => "kinozal";
    public override string Host => AppInit.conf.Kinozal.host;
}