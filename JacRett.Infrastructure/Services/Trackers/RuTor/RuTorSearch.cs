using System.Text;
using JacRett.Core.Interfaces;
using JacRett.Core.Models.Details;
using JacRett.Core.Models.Options;
using JacRett.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRett.Infrastructure.Services.Trackers.RuTor;

public class RuTorSearch : BaseRuTor
{
    private readonly ITorrentRepository _torrentRepository;

    public RuTorSearch(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (!Config.RuTor.EnableSearch)
            return [];

        var url = SearchUrl + Uri.EscapeDataString(query);
        var html = await HttpService.GetStringAsync(url, new RequestOptions { Referer = url, Encoding = Encoding.UTF8 });

        if (string.IsNullOrWhiteSpace(html))
            return [];

        var torrents = Parse(html);

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
                    FetchDetailsAsync);
            });

        return torrents.Where(t => t.Types?.Length > 0).ToList();
    }
}