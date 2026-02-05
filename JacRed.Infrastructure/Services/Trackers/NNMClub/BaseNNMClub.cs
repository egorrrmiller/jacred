using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services.Trackers.NNMClub;

public class BaseNNMClub : BaseTrackerSearch, ITrackerCatalogEnricher
{
    protected readonly HttpService _httpService;

    public BaseNNMClub(HttpService httpService)
    {
        _httpService = httpService;
    }

    public override TrackerType Tracker => TrackerType.NNMClub;
    public override string TrackerName => "nnmclub";
    public override string Host => "https://nnmclub.to";

    public async Task<bool> TryEnrichAsync(TorrentDetails torrent, IReadOnlyDictionary<string, TorrentDetails> existing)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.Url))
            return false;

        if (existing.TryGetValue(torrent.Url, out var cached))
        {
            if (!string.IsNullOrWhiteSpace(cached.Magnet))
                torrent.Magnet = cached.Magnet;

            if (torrent.CreateTime == default && cached.CreateTime != default)
                torrent.CreateTime = cached.CreateTime;

            if (string.IsNullOrWhiteSpace(torrent.OriginalName) && !string.IsNullOrWhiteSpace(cached.OriginalName))
                torrent.OriginalName = cached.OriginalName;

            if (string.IsNullOrWhiteSpace(torrent.Name) && !string.IsNullOrWhiteSpace(cached.Name))
                torrent.Name = cached.Name;

            if (!string.IsNullOrWhiteSpace(torrent.Magnet))
                return true;
        }

        var magnet = await FetchMagnetAsync(torrent.Url);
        if (!string.IsNullOrWhiteSpace(magnet))
            torrent.Magnet = magnet;

        return !string.IsNullOrWhiteSpace(torrent.Magnet);
    }

    private async Task<string?> FetchMagnetAsync(string url)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding("windows-1251");
            
            var html = await _httpService.Get(url, encoding: encoding);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var match = Regex.Match(html, @"href=""(magnet:\?xt=urn:btih:[^""]+)""");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    protected Dictionary<string, string> GetSearchParameters(string query)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "prev_sd", "0" },
            { "prev_a", "0" },
            { "prev_my", "0" },
            { "prev_n", "0" },
            { "prev_shc", "1" },
            { "prev_shf", "1" },
            { "prev_sha", "0" },
            { "prev_shs", "0" },
            { "prev_shr", "0" },
            { "prev_sht", "0" },
            { "f[]", "-1" },
            { "o", "10" },
            { "s", "2" },
            { "tm", "-1" },
            { "shc", "1" },
            { "shf", "1" },
            { "ta", "-1" },
            { "sns", "-1" },
            { "sds", "-1" },
            { "nm", query },
            { "pn", "" },
            { "submit", "Поиск" },
        };
    }

    protected static IReadOnlyCollection<TorrentDetails> ParseTrackerPage(string html, string host)
    {
        var list = new List<TorrentDetails>();
        var regex = new Regex("<tr class=\"prow[12]\">.*?</tr>", RegexOptions.Singleline | RegexOptions.Compiled);
        var matches = regex.Matches(html);
        var now = DateTime.UtcNow;

        foreach (Match match in matches)
        {
            var row = match.Value;
            
            var catMatch = Regex.Match(row, @"tracker\.php\?c=(?<cat>\d+)");
            if (!catMatch.Success) continue;
            
            var catId = catMatch.Groups["cat"].Value;
            if (!CategoryMap.TryGetValue(catId, out var category))
                continue;

            var titleMatch = Regex.Match(row, @"viewtopic\.php\?t=(?<id>\d+)""[^>]*><b>(?<title>.*?)</b>", RegexOptions.Singleline);
            if (!titleMatch.Success) continue;
            
            var topicId = titleMatch.Groups["id"].Value;
            var titleRaw = titleMatch.Groups["title"].Value;
            var title = WebUtility.HtmlDecode(titleRaw);
            
            var sizeMatch = Regex.Match(row, @"<u>(?<bytes>\d+)</u>\s*(?<text>.*?)</td>", RegexOptions.Singleline);
            long size = 0;
            string? sizeName = null;
            if (sizeMatch.Success)
            {
                 long.TryParse(sizeMatch.Groups["bytes"].Value, out size);
                 sizeName = sizeMatch.Groups["text"].Value.Trim();
            }

            var seedMatch = Regex.Match(row, @"title=""Seeders""[^>]*><b>(?<val>\d+)</b>");
            var leechMatch = Regex.Match(row, @"title=""Leechers""[^>]*><b>(?<val>\d+)</b>");
            
            int seed = 0, leech = 0;
            if (seedMatch.Success) int.TryParse(seedMatch.Groups["val"].Value, out seed);
            if (leechMatch.Success) int.TryParse(leechMatch.Groups["val"].Value, out leech);

            var dateMatch = Regex.Match(row, @"title=""Добавлено""[^>]*><u>(?<ts>\d+)</u>");
            DateTime date = default;
            if (dateMatch.Success && long.TryParse(dateMatch.Groups["ts"].Value, out var ts))
            {
                date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
            }

            var (name, originalName, relased) = ParseTitle(title);

            list.Add(new TorrentDetails
            {
                TrackerName = "nnmclub",
                Types = category.Types,
                Url = $"{host}/forum/viewtopic.php?t={topicId}",
                Title = title,
                Sid = seed,
                Pir = leech,
                Size = size,
                SizeName = sizeName,
                CreateTime = date,
                UpdateTime = now,
                CheckTime = now,
                Name = name,
                OriginalName = originalName,
                Relased = relased
            });
        }
        
        return list;
    }

    private static (string? name, string? originalName, int relased) ParseTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, 0);

        var year = ExtractYear(title);
        
        var parts = Regex.Split(title, @"\s+(?:/|\|)\s+")
                         .Select(p => p.Trim())
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .ToArray();
        
        string? name = null;
        string? originalName = null;

        foreach (var part in parts)
        {
            var cleaned = CleanPart(part);
            if (string.IsNullOrWhiteSpace(cleaned)) continue;

            if (Regex.IsMatch(cleaned, @"\p{IsCyrillic}"))
            {
                if (name == null)
                    name = cleaned;
            }
            else
            {
                if (originalName == null)
                    originalName = cleaned;
            }
        }
        
        if (name == null && originalName != null)
            name = originalName;

        return (name, originalName, year);
    }

    private static string CleanPart(string part)
    {
        var indexBracket = part.IndexOf('[');
        if (indexBracket >= 0)
            part = part.Substring(0, indexBracket);
            
        var indexParen = part.IndexOf('(');
        if (indexParen >= 0)
            part = part.Substring(0, indexParen);
            
        return part.Trim();
    }

    private static int ExtractYear(string title)
    {
        var match = Regex.Match(title, @"\b(19|20)\d{2}\b");
        return match.Success && int.TryParse(match.Value, out var year) ? year : 0;
    }

    protected static readonly IReadOnlyDictionary<string, CategoryInfo> CategoryMap = new Dictionary<string, CategoryInfo>
    {
        ["10"] = new(["movie"]),
        ["13"] = new(["movie"]),
        ["6"] = new(["movie"]),
        ["4"] = new(["serial"]),
        ["3"] = new(["serial"]),
        ["22"] = new(["docuserial", "documovie"]),
        ["23"] = new(["docuserial", "documovie"]),
        ["1"] = new(["anime"]),
        ["7"] = new(["multfilm", "multserial"]),
        ["11"] = new(["movie"]),
        ["14"] = new(["movie"]),
        
        ["24"] = new(["anime"]),
        ["26"] = new(["multfilm", "multserial"]),
    };

    protected record CategoryInfo(string[] Types);
}