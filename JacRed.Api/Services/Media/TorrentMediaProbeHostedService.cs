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
    private readonly Config _config;
    private readonly ITorrentMediaProbeService _torrentMediaProbeService;


    public TorrentMediaProbeHostedService(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        _config = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Config>>().Value;
        _torrentMediaProbeService = scope.ServiceProvider.GetRequiredService<ITorrentMediaProbeService>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.Ffprobe.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                await _torrentMediaProbeService.ExecuteAsync(stoppingToken);
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