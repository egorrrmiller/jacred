using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.RuTor;

public class RuTorSearch : BaseRuTor
{
    private readonly Config _config;
    private readonly ITorrentRepository _torrentRepository;

    public RuTorSearch(HttpService httpService, ITorrentRepository torrentRepository, IOptionsSnapshot<Config> config)
        : base(httpService)
    {
        _torrentRepository = torrentRepository;
        _config = config.Value;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (!_config.RuTor.EnableSearch)
            return [];

        var url = SearchUrl + query;
        var html = await HttpService.Get(url, referer: url, encoding: RuEncoding);

        if (string.IsNullOrWhiteSpace(html))
            return [];

        var torrents = Parse(html).Where(t => t.Types.Length > 0).ToList();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount)
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

        return torrents;
    }
}