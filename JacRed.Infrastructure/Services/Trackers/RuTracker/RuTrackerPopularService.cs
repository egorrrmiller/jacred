using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

/// <summary>
///     Сервис обновления популярных раздач по категориям
/// </summary>
public class RuTrackerPopularService : BaseRuTracker
{
    private readonly Config _config;
    private readonly ITorrentRepository _torrentRepository;

    public RuTrackerPopularService(ICacheService cacheService, HttpService httpService, IOptionsSnapshot<Config> config,
        ITorrentRepository torrentRepository) : base(cacheService, httpService, config)
    {
        _torrentRepository = torrentRepository;
        _config = config.Value;
    }

    public override async Task InvokeAsync()
    {
        if (!_config.RuTracker.Popular.Enable)
            return;

        var categories = _config.RuTracker.Popular.Categories;
        var now = DateTime.UtcNow;

        foreach (var category in categories)
        {
            var url = BuildCategoryUrl(Host, category.ToString(), 0);
            var html = await Get(
                url,
                RuEncoding,
                url
                /*useProxy: useProxy*/);
            var torrents = ParseForumPage(html, category.ToString(), Host, now);

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };
            await Parallel.ForEachAsync(
                torrents,
                options,
                async (torrent, _) =>
                {
                    await _torrentRepository.AddOrUpdateAsync(
                        [torrent],
                        TryEnrichAsync);
                });

            var maxPage = GetMaxPages(html);
            if (maxPage == 0) continue;

            var maxPages = _config.RuTracker.Popular.MaxPages;
            if (maxPage <= maxPages)
                maxPages = maxPage;

            for (var page = 1; page < maxPages; page++)
            {
                url = BuildCategoryUrl(Host, category.ToString(), page);
                torrents = await FetchForumPageAsync(url, category.ToString(), now);

                await Parallel.ForEachAsync(
                    torrents,
                    options,
                    async (torrent, _) =>
                    {
                        await _torrentRepository.AddOrUpdateAsync(
                            [torrent],
                            TryEnrichAsync);
                    });
            }
        }
    }
}