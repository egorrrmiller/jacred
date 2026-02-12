using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.NNMClub;

public class NNMClubRefreshService : BaseNNMClub
{
    private readonly ITorrentRepository _torrentRepository;

    protected NNMClubRefreshService(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task InvokeAsync()
    {
        var config = Config.NNMClub;

        var olderThan = TimeSpan.FromMinutes(config.Refresh.OlderThanMin);
        var limit = config.Refresh.Limit > 0 ? (int?)config.Refresh.Limit : null;
        var torrents = await _torrentRepository.GetByTrackerAsync(TrackerName, olderThan, limit);

        await Parallel.ForEachAsync(torrents, async (torrent, _) =>
        {
            await _torrentRepository.AddOrUpdateAsync(
                [torrent],
                x => FetchDetailsAsync(x, true));
        });
    }
}