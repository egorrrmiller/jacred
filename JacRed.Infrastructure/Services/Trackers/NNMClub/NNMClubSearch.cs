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
    private readonly Config _config;
    
    public NNMClubSearch(HttpService httpService, ITorrentRepository torrentRepository, IOptionsSnapshot<Config> config) : base(httpService)
    {
        _torrentRepository = torrentRepository;
        _config = config.Value;
    }

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (!_config.NNMClub.EnableSearch)
            return [];

        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var parameters = GetSearchParameters(query);
        var url = $"{Host}/forum/tracker.php";
        
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("windows-1251");
        
        var pairs = parameters.Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value, encoding)}");
        var formEncoded = string.Join("&", pairs);
        
        var content = new StringContent(formEncoded, encoding, "application/x-www-form-urlencoded");
        
        var html = await _httpService.Post(url, content, encoding: encoding);
        
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var parsed =  ParseTrackerPage(html, Host);
        
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
                    [torrent],
                    TryEnrichAsync);
            });

        return results.Values.ToList();
    }
}