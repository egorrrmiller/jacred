using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Baibako : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Baibako;
    public override string TrackerName => "baibako";
    public override string Host => AppInit.conf.Baibako.host;
}
