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
    private readonly Config _config;
    private readonly RuTrackerPopularService _ruTrackerPopularService;

    public RuTrackerPopularHostedService(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        _config = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Config>>().Value;
        var providers = scope.ServiceProvider.GetRequiredService<IEnumerable<ITrackerRefreshProvider>>();
        _ruTrackerPopularService =
            providers.FirstOrDefault(x => x is RuTrackerPopularService) as RuTrackerPopularService ??
            throw new ArgumentException(nameof(providers));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.RuTracker.Popular.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await _ruTrackerPopularService.InvokeAsync();
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