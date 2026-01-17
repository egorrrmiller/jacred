using JacRed.Core;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

/// <summary>
///     Джоба для синхронизации популярных раздач RuTracker
/// </summary>
public class RuTrackerTopSeededSync : BaseRuTracker, ITrackerCronProvider
{
    public RuTrackerTopSeededSync(HttpService httpService,
        ICacheService cacheService) : base(cacheService, httpService)
    {
    }

    public TrackerType Tracker => TrackerType.Rutracker;
    public string TrackerName => RuTrackerName;

    public string Url => RuTrackerUrl;

    public async Task<IReadOnlyCollection<TorrentDetails>> FetchCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var requestHost = AppInit.conf.Rutracker.rqHost();

        foreach (var category in CategoryMap.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildCategoryUrl(requestHost, category, 0);
            var html = await Get(
                url,
                RuEncoding,
                timeoutSeconds: 10,
                useProxy: AppInit.conf.Rutracker.useproxy,
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
                continue;

            var maxPages = GetMaxPages(html);
            var parsed = ParseForumPage(html, category, Url, now);
            foreach (var item in parsed)
                results[item.Url] = item;

            var totalPages = Math.Min(maxPages, MaxPagesPerCategory);
            var delayMs = AppInit.conf.Rutracker.parseDelay == 0 ? 3000 : AppInit.conf.Rutracker.parseDelay;
            for (var page = 1; page <= totalPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageUrl = BuildCategoryUrl(requestHost, category, page);
                var pageHtml = await Get(
                    pageUrl,
                    RuEncoding,
                    timeoutSeconds: 10,
                    useProxy: AppInit.conf.Rutracker.useproxy,
                    cancellationToken: cancellationToken);

                if (string.IsNullOrWhiteSpace(pageHtml))
                    continue;

                var pageParsed = ParseForumPage(pageHtml, category, Url, now);
                foreach (var item in pageParsed)
                    results[item.Url] = item;

                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
            }

            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
        }

        return results.Values.ToArray();
    }
}