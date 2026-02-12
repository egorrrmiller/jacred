using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

/// <summary>
///     Сервис для обновления данных по торрентам, обновление которых было > n часов назад
/// </summary>
public class RuTrackerRefreshService : BaseRuTracker
{
    private readonly ITorrentRepository _torrentRepository;

    public RuTrackerRefreshService(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task InvokeAsync()
    {
        var config = Config.RuTracker;

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