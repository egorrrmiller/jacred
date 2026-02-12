using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Models.Options.TrackerConfigs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Refresh;

public abstract class BaseRefreshService<T> : BackgroundService where T : class, ITrackerRefreshProvider
{
    protected readonly Config Config;
    private readonly IServiceScopeFactory _scopeFactory;

    protected BaseRefreshService(IOptions<Config> config, IServiceScopeFactory scopeFactory)
    {
        Config = config.Value;
        _scopeFactory = scopeFactory;
    }
    protected abstract RefreshSettings RefreshSettings { get; }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!RefreshSettings.Enable)
            return;
        
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(RefreshSettings.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var providers = scope.ServiceProvider.GetRequiredService<IEnumerable<ITrackerRefreshProvider>>();
                var refreshService =
                    providers.FirstOrDefault(x => x is T) as T ??
                    throw new ArgumentException(nameof(providers));

                await refreshService.InvokeAsync();
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