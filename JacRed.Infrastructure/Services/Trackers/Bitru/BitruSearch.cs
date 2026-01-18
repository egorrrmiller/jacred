using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Bitru;

public sealed class BitruSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Bitru;
    public override string TrackerName => "bitru";
    public override string Host => AppInit.conf.Bitru.host;
}