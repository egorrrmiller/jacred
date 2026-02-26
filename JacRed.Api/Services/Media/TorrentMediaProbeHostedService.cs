using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace JacRed.Api.Services.Media;

public class TorrentMediaProbeHostedService : BackgroundService
{
    private readonly Config _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    public TorrentMediaProbeHostedService(IServiceScopeFactory scopeFactory, IOptions<Config> config, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

                _logger.Information("Starting torrent media probe service");
                await torrentMediaProbeService.ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "TorrentMediaProbeHostedService failed");
            }
    }
}