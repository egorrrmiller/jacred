using System.Text;
using JacRett.Core.Interfaces;
using JacRett.Core.Models.Details;
using JacRett.Core.Models.Options;
using JacRett.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRett.Infrastructure.Services.Trackers.MegaPeer;

public class MegaPeerSearch : BaseMegaPeer
{
    private readonly ITorrentRepository _torrentRepository;

    public MegaPeerSearch(IOptions<Config> config, HttpService httpService, ICacheService cacheService, ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("windows-1251");
        var encodedQuery = string.Join("", encoding.GetBytes(query).Select(b => $"%{b:X2}"));
        
        var url = $"{SearchUrl}?search={encodedQuery}&age=&cat=0&stype=0&sort=3&ascdesc=0";
        var html = await HttpService.GetStringAsync(url, new RequestOptions { Referer = url, Encoding = encoding });

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