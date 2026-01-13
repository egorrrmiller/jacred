using System.Threading;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using Microsoft.Extensions.Hosting;

namespace JacRed.Api.Services;

public class TracksDatabaseInitializer : IHostedService
{
    private readonly ITracksDatabase _tracksDatabase;

    public TracksDatabaseInitializer(ITracksDatabase tracksDatabase)
    {
        _tracksDatabase = tracksDatabase;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _tracksDatabase.LoadAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}