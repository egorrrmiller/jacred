using JacRed.Core.Models.Options;
using JacRed.Core.Models.Options.TrackerConfigs;
using JacRed.Infrastructure.Services.Trackers.RuTor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public class RuTorRefreshHostedService : BaseRefreshService<RuTorRefreshService>
{
    public RuTorRefreshHostedService(IOptions<Config> config, IServiceScopeFactory scopeFactory) : base(config, scopeFactory)
    {
    }

    protected override RefreshSettings RefreshSettings => Config.RuTor.Refresh;
}