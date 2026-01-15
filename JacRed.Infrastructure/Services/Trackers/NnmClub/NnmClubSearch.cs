using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Infrastructure.Services.Trackers.NnmClub;

public sealed class NnmClubSearch : BaseTrackerSearch
{
    public override TrackerType Tracker => TrackerType.NNMClub;
    public override string TrackerName => "nnmclub";
    public override string Host => AppInit.conf.NNMClub.host;
}