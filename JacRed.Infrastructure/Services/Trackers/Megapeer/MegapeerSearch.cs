using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Megapeer;

public sealed class MegapeerSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Megapeer;
    public override string TrackerName => "megapeer";
    public override string Host => AppInit.conf.Megapeer.host;
}