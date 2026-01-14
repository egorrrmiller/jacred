using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class KinozalTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Kinozal;
    public override string TrackerName => "kinozal";
    public override string Host => AppInit.conf.Kinozal.host;
}
