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
    private readonly Config _config;
    private readonly ITorrentRepository _torrentRepository;

    public RuTrackerRefreshService(
        ICacheService cacheService,
        HttpService httpService,
        IOptionsSnapshot<Config> config,
        ITorrentRepository torrentRepository) : base(cacheService, httpService, config)
    {
        _torrentRepository = torrentRepository;
        _config = config.Value;
    }

    public override async Task InvokeAsync()
    {
        if (!_config.RuTracker.Refresh.Enable)
            return;

        var olderThan = TimeSpan.FromMinutes(_config.RuTracker.Refresh.OlderThanMin);
        var limit = _config.RuTracker.Refresh.Limit > 0 ? (int?)_config.RuTracker.Refresh.Limit : null;
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