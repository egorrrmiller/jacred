using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class BaibakoTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Baibako;
    public override string TrackerName => "baibako";
    public override string Host => AppInit.conf.Baibako.host;
}
