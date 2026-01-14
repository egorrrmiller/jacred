using JacRed.Core;
using JacRed.Core.Enums;

namespace JacRed.Api.Services.Trackers;

public sealed class NNMClubTrackerSearchProvider : BaseTrackerSearchProvider
{
    public override TrackerType Tracker => TrackerType.NNMClub;
    public override string TrackerName => "nnmclub";
    public override string Host => AppInit.conf.NNMClub.host;
}
