using System.Text;
using System.Web;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.NNMClub;

public class NNMClubSearch : BaseNNMClub
{
    private readonly ITorrentRepository _torrentRepository;

    public NNMClubSearch(IOptions<Config> config, HttpService httpService, ICacheService cacheService,
        ITorrentRepository torrentRepository) : base(config, httpService, cacheService)
    {
        _torrentRepository = torrentRepository;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (!Config.NNMClub.EnableSearch)
            return [];

        var parameters = GetSearchParameters(query);
        var url = $"{Host}/forum/tracker.php";

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("windows-1251");

        var pairs = parameters.Select(kv =>
            $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value, encoding)}");
        var formEncoded = string.Join("&", pairs);

        var content = new StringContent(formEncoded, Encoding.UTF8, "application/x-www-form-urlencoded");

        var html = await HttpService.PostAsync(url, content, new RequestOptions { Encoding = RuEncoding });

        if (string.IsNullOrWhiteSpace(html))
            return [];

        var torrents = ParseTrackerPage(html, Host);

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

        return torrents;
    }
}