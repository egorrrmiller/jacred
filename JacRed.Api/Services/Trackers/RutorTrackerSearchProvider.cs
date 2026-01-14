using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class RutorTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.Rutor;
    public override string TrackerName => "rutor";
    public override string Host => AppInit.conf.Rutor.host;
}
