using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Toloka;

public sealed class TolokaSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Toloka;
    public override string TrackerName => "toloka";
    public override string Host => AppInit.conf.Toloka.host;
}