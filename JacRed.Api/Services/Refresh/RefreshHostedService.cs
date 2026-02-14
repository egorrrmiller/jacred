using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public class RefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Config _config;

    public RefreshHostedService(IServiceScopeFactory scopeFactory, IOptions<Config> config)
    {
        _scopeFactory = scopeFactory;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!_config.Refresh.Enable)
            return;
        
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.Refresh.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ISearchQueryRepository>();
                var remoteSearch = scope.ServiceProvider.GetRequiredService<IRemoteSearchService>();

                var queries = await repository.GetStaleSearchQueriesAsync(TimeSpan.FromMinutes(_config.Refresh.OlderThanMin), 100);
                if (queries.Count == 0) continue;

                foreach (var query in queries)
                {
                    await remoteSearch.SearchAsync(query);
                    await repository.UpdateLastRefreshTimeAsync(query);
                }
            }
            catch (Exception ex)
            {
                // ignored
            }
        }
    }
}