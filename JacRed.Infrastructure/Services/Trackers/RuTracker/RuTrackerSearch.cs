using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

public sealed class RuTrackerSearch : BaseRuTracker
{
    private readonly ITorrentRepository _torrentRepository;


    public RuTrackerSearch(ICacheService cacheService, HttpService httpService, IOptions<Config> config, ITorrentRepository torrentRepository) : base(cacheService, httpService, config)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var requestHost = Host;

        var url = BuildQueryUrl(requestHost, query, 0);
        var html = await Get(
            url,
            RuEncoding,
            timeoutSeconds: 10/*,
            useProxy: AppInit.conf.Rutracker.useproxy*/);

        if (string.IsNullOrWhiteSpace(html))
            return new List<TorrentDetails>();

        var parsed = ParseForumPage(html, string.Empty, Host, now);
        foreach (var item in parsed)
            results[item.Url] = item;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(
            results.Values,
            options,
            async (torrent, _) =>
            {
                await _torrentRepository.AddOrUpdateAsync(
                    new[] { torrent },
                    (t, existing) => TryEnrichAsync(t, existing));
            });

        return results.Values.ToList();
    }
}
