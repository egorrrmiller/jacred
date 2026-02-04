using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Services.Media;

public class TorrentMediaProbeHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public TorrentMediaProbeHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Config>>().Value;
        var torrentMediaProbeService = scope.ServiceProvider.GetRequiredService<ITorrentMediaProbeService>();
        
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(config.Ffprobe.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await torrentMediaProbeService.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // ignored
            }
    }
}