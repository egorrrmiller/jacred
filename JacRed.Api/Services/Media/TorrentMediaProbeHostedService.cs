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
    private readonly IServiceScopeFactory _scopeFactory;

    public TorrentMediaProbeHostedService(IServiceScopeFactory scopeFactory, IOptions<Config> config)
    {
        _scopeFactory = scopeFactory;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_config.Ffprobe.TimeOut));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var torrentMediaProbeService = scope.ServiceProvider.GetRequiredService<ITorrentMediaProbeService>();

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