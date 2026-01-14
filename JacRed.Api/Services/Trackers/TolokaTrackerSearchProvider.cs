using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class TolokaTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Toloka;
    public override string TrackerName => "toloka";
    public override string Host => AppInit.conf.Toloka.host;
}
