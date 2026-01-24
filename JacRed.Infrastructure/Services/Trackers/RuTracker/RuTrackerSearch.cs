using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTracker;

public sealed class RuTrackerSearch : BaseRuTracker
{
    private readonly ITorrentRepository _torrentRepository;


    public RuTrackerSearch(ICacheService cacheService, HttpService httpService, IOptionsSnapshot<Config> config,
        ITorrentRepository torrentRepository) : base(cacheService, httpService, config)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        var url = BuildQueryUrl(Host, query, 0);
        var parsed = await FetchForumPageAsync(url, string.Empty, now);

        if (parsed.Count == 0)
            return new List<TorrentDetails>();

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
                    TryEnrichAsync);
            });

        return results.Values.ToList();
    }
}