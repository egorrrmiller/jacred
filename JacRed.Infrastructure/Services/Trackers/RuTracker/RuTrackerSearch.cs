using JacRed.Core;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

public sealed class RuTrackerSearch : BaseRuTracker, ITrackerSearch
{
    private readonly ITorrentRepository _torrentRepository;
    
    public RuTrackerSearch(ICacheService cacheService, HttpService httpService, ITorrentRepository torrentRepository) : base(cacheService, httpService)
    {
        _torrentRepository = torrentRepository;
    }

    public TrackerType Tracker => TrackerType.Rutracker;
    public string TrackerName => "rutracker";
    public string Host => AppInit.conf.Rutracker.host;

    public async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var requestHost = AppInit.conf.Rutracker.rqHost();

        var url = BuildQueryUrl(requestHost, query, 0);
        var html = await Get(
            url,
            RuEncoding,
            timeoutSeconds: 10,
            useProxy: AppInit.conf.Rutracker.useproxy);

        if (string.IsNullOrWhiteSpace(html))
            return new List<TorrentDetails>();

        var parsed = ParseForumPage(html, string.Empty, RuTrackerUrl, now);
        foreach (var item in parsed)
            results[item.Url] = item;
        
        await _torrentRepository.AddOrUpdateAsync(
            results.Values,
            (torrent, existing) =>
                TryEnrichAsync(torrent, existing, cancellationToken));

        return results.Values.ToList();
    }
}
