using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class SelezenTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Selezen;
    public override string TrackerName => "selezen";
    public override string Host => AppInit.conf.Selezen.host;
}
