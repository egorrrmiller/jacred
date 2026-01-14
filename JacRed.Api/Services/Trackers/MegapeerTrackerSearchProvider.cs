using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class MegapeerTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Megapeer;
    public override string TrackerName => "megapeer";
    public override string Host => AppInit.conf.Megapeer.host;
}
