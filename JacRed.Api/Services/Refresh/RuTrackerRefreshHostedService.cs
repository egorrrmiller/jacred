using JacRed.Core.Models.Options;
using JacRed.Core.Models.Options.TrackerConfigs;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public class RuTrackerRefreshHostedService : BaseRefreshService<RuTrackerRefreshService>
{
    public RuTrackerRefreshHostedService(IOptions<Config> config, IServiceScopeFactory scopeFactory) : base(config, scopeFactory)
    {
    }

    protected override RefreshSettings RefreshSettings => Config.RuTracker.Refresh;
}