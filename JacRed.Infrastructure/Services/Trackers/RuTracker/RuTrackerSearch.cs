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

        var maxPages = GetMaxPages(html);
        var parsed = ParseForumPage(html, string.Empty, RuTrackerUrl, now);
        foreach (var item in parsed)
            results[item.Url] = item;

        var totalPages = Math.Min(maxPages, MaxPagesPerCategory);
        var delayMs = AppInit.conf.Rutracker.parseDelay == 0 ? 3000 : AppInit.conf.Rutracker.parseDelay;
        for (var page = 1; page <= totalPages; page++)
        {
            /*if (cancellationToken.IsCancellationRequested)
                break;*/

            var pageUrl = BuildQueryUrl(requestHost, query, page);
            var pageHtml = await Get(
                pageUrl,
                RuEncoding,
                timeoutSeconds: 10,
                useProxy: AppInit.conf.Rutracker.useproxy);

            if (string.IsNullOrWhiteSpace(pageHtml))
                continue;

            var pageParsed = ParseForumPage(pageHtml, string.Empty, RuTrackerUrl, now);
            foreach (var item in pageParsed)
                results[item.Url] = item;

            // Задержку ставим только если впереди ещё страницы, чтобы не тратить время на последней.
            if (delayMs > 0 && page < totalPages)
            {
                try
                {
                    await Task.Delay(delayMs);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        await _torrentRepository.AddOrUpdateAsync(
            results.Values,
            (torrent, existing) =>
                TryEnrichAsync(torrent, existing, cancellationToken));

        return results.Values.ToList();
    }
}
