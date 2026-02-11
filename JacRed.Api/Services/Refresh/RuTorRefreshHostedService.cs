using JacRed.Core.Models.Options;
using JacRed.Infrastructure.Services.Trackers.RuTor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public class RuTorRefreshHostedService : BaseRefreshService<RuTorRefreshService>
{
    public RuTorRefreshHostedService(IOptions<Config> config, IServiceScopeFactory scopeFactory) : base(config, scopeFactory)
    {
    }

    protected override int TimeOut => Config.RuTor.Refresh.TimeOut;
}