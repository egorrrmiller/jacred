using JacRett.Core.Interfaces;
using JacRett.Core.Models.Details;
using JacRett.Core.Models.Options;
using JacRett.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRett.Infrastructure.Services.Trackers.Kinozal;

public class KinozalSearch : BaseKinozal
{
    private readonly ITorrentRepository _torrentRepository;

    public KinozalSearch(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (!Config.Kinozal.EnableSearch)
            return [];

        var url = $"{Host}/browse.php?s={query}&g=0&c=0&v=0&d=0&w=0&t=1&f=0";

        var html = await Get(url, RuEncoding);
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var results = ParseBrowsePage(html, Host);

        if (results.Count == 0)
            return [];

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(
            results,
            options,
            async (torrent, _) =>
            {
                await _torrentRepository.AddOrUpdateAsync(
                    [torrent],
                    FetchDetailsAsync);
            });

        return results;
    }
}