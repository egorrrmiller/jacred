using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class RutrackerTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Rutracker;
    public override string TrackerName => "rutracker";
    public override string Host => AppInit.conf.Rutracker.host;
}
