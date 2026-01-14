using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class AnimelayerTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Animelayer;
    public override string TrackerName => "animelayer";
    public override string Host => AppInit.conf.Animelayer.host;
}
