using JacRed.Core.Models.Options;
using JacRed.Core.Models.Options.TrackerConfigs;
using JacRed.Infrastructure.Services.Trackers.NNMClub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public class NNMClubRefreshHostedService : BaseRefreshService<NNMClubRefreshService>
{
    public NNMClubRefreshHostedService(IOptions<Config> config, IServiceScopeFactory scopeFactory) : base(config, scopeFactory)
    {
    }
    protected override RefreshSettings RefreshSettings => Config.NNMClub.Refresh;
}