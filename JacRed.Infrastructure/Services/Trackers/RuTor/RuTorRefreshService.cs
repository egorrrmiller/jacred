using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTor;

public class RuTorRefreshService : BaseRuTor
{
    private readonly ITorrentRepository _torrentRepository;

    public RuTorRefreshService(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task InvokeAsync()
    {
        var config = Config.RuTor;
        if (!config.Refresh.Enable)
            return;

        var olderThan = TimeSpan.FromMinutes(config.Refresh.OlderThanMin);
        var limit = Config.RuTracker.Refresh.Limit > 0 ? (int?)config.Refresh.Limit : null;
        var torrents = await _torrentRepository.GetByTrackerAsync(TrackerName, olderThan, limit);

        foreach (var torrent in torrents)
            await _torrentRepository.AddOrUpdateAsync(
                [torrent],
                x => FetchDetailsAsync(x, true));
    }
}