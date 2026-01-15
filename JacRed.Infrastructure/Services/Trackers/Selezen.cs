using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Selezen : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Selezen;
    public override string TrackerName => "selezen";
    public override string Host => AppInit.conf.Selezen.host;
}
