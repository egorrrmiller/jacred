using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers;

public sealed class NnmClub : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.NNMClub;
    public override string TrackerName => "nnmclub";
    public override string Host => AppInit.conf.NNMClub.host;
}
