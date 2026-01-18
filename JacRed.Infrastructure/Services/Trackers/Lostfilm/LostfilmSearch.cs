using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Lostfilm;

public sealed class LostfilmSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Lostfilm;
    public override string TrackerName => "lostfilm";
    public override string Host => AppInit.conf.Lostfilm.host;
}