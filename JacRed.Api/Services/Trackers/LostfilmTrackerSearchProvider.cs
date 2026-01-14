using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class LostfilmTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Lostfilm;
    public override string TrackerName => "lostfilm";
    public override string Host => AppInit.conf.Lostfilm.host;
}
