using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Bitru : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Bitru;
    public override string TrackerName => "bitru";
    public override string Host => AppInit.conf.Bitru.host;
}
