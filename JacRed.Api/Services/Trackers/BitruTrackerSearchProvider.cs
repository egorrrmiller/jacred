using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class BitruTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Bitru;
    public override string TrackerName => "bitru";
    public override string Host => AppInit.conf.Bitru.host;
}
