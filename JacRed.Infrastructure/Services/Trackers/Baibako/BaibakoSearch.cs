using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Baibako;

public sealed class BaibakoSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Baibako;
    public override string TrackerName => "baibako";
    public override string Host => AppInit.conf.Baibako.host;
}