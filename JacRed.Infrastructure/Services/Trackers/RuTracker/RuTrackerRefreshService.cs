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

    public RuTrackerRefreshService(IOptions<Config> config, HttpService httpService, ICacheService cacheService, ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task InvokeAsync()
    {
        if (!Config.RuTracker.Refresh.Enable)
            return;

        var olderThan = TimeSpan.FromMinutes(Config.RuTracker.Refresh.OlderThanMin);
        var limit = Config.RuTracker.Refresh.Limit > 0 ? (int?)Config.RuTracker.Refresh.Limit : null;
        var torrents = await _torrentRepository.GetByTrackerAsync(TrackerName, olderThan, limit);
        var now = DateTime.UtcNow;

        foreach (var torrent in torrents)
        {
            var parsed = await FetchForumPageAsync(torrent.Url, string.Empty, now);
            foreach (var parse in parsed)
                await _torrentRepository.AddOrUpdateAsync(
                    [parse],
                    TryEnrichAsync);
        }
    }
}