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

public class RuTrackerPopularHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public RuTrackerPopularHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Config>>().Value;
        var providers = scope.ServiceProvider.GetRequiredService<IEnumerable<ITrackerRefreshProvider>>();
        var ruTrackerPopularService = providers.FirstOrDefault(x => x is RuTrackerPopularService) as RuTrackerPopularService ?? throw new ArgumentException(nameof(providers));
        
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(config.RuTracker.Popular.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await ruTrackerPopularService.InvokeAsync();
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