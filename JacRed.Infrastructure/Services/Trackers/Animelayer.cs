using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class Animelayer : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Animelayer;
    public override string TrackerName => "animelayer";
    public override string Host => AppInit.conf.Animelayer.host;
}
