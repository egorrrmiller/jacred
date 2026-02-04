using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Infrastructure.Services.Trackers.RuTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.RuTracker;

public class RuTrackerRefreshHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Config _config;

    public RuTrackerRefreshHostedService(IServiceScopeFactory scopeFactory, IOptions<Config> config)
    {
        _scopeFactory = scopeFactory;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.RuTracker.Refresh.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var providers = scope.ServiceProvider.GetRequiredService<IEnumerable<ITrackerRefreshProvider>>();
                var ruTrackerRefreshService = providers.FirstOrDefault(x => x is RuTrackerRefreshService) as RuTrackerRefreshService ?? throw new ArgumentException(nameof(providers));
                
                await ruTrackerRefreshService.InvokeAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // ignored
            }
    }
}