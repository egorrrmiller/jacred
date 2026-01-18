using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.Rutor;

public sealed class RutorSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.Rutor;
    public override string TrackerName => "rutor";
    public override string Host => AppInit.conf.Rutor.host;
}