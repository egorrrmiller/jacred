using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;

namespace JacRed.Api.Services.Trackers;

public sealed class RutrackerCatalogProvider : ITrackerCatalogProvider, ITrackerCatalogEnricher
{
    #region Category map

    private enum CategoryParser
    {
        Default,
        Movie,
        Serial,
        Generic
    }

    private sealed record CategoryInfo(string[] Types, CategoryParser Parser);

    private static readonly IReadOnlyDictionary<string, CategoryInfo> CategoryMap = BuildCategoryMap();

    #endregion

    #region Regex helpers

    private static readonly Regex RowRegex =
        new("<tr[^>]*class=\"[^\"]*\\bhl-tr\\b[^\"]*\"[^>]*>.*?</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LinkRegex =
        new("<a(?=[^>]*\\bclass=[\"'][^\"']*\\btLink\\b[^\"']*[\"'])(?=[^>]*\\bhref=[\"'](?<url>[^\"']+)[\"'])[^>]*>(?<title>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SeedRegex =
        new("class=\"seed[^\"]*\"[^>]*>\\s*(?:<b>)?(?<value>\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LeechRegex =
        new("class=\"leech[^\"]*\"[^>]*>\\s*(?<value>\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SizeBytesRegex =
        new("tor-size[^>]*data-ts_text=\"(?<bytes>\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SizeTextRegex =
        new("(?<value>\\d+(?:[\\.,]\\d+)?)\\s*(?<unit>TB|GB|MB|KB|ТБ|ГБ|МБ|КБ)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DateRegex =
        new("tor-date[^>]*data-ts_text=\"(?<ts>\\d+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex =
        new("<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex =
        new("[\\n\\r\\t\\u00A0]+", RegexOptions.Compiled);

    private static readonly Regex SeasonMarkerRegex =
        new("(\u0421\u0435\u0437\u043e\u043d|\u0421\u0435\u0440\u0438\u0438)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TopicDateRegex =
        new("<a class=\"p-link small\" href=\"viewtopic\\.php\\?t=[^\"]+\">(?<date>[^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MagnetRegex =
        new("href=\"(?<magnet>magnet:[^\"]+)\" class=\"(med )?magnet-link\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearRegex =
        new("(?<!\\d)(19\\d{2}|20\\d{2})(?!\\d)", RegexOptions.Compiled);

    #endregion

    private static readonly Encoding RuEncoding = Encoding.GetEncoding("windows-1251");

    private readonly HttpService _httpService;
    private readonly ILogger<RutrackerCatalogProvider> _logger;
    private const int MaxPagesPerCategory = 5;
    private static readonly Regex MaxPageRegex =
        new("\u0421\u0442\u0440\u0430\u043d\u0438\u0446\u0430 <b>1</b> \u0438\u0437 <b>(?<pages>\\d+)</b>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RutrackerCatalogProvider(HttpService httpService, ILogger<RutrackerCatalogProvider> logger)
    {
        _httpService = httpService;
        _logger = logger;
    }

    public TrackerType Tracker => TrackerType.Rutracker;
    public string TrackerName => "rutracker";

    public async Task<IReadOnlyCollection<TorrentDetails>> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, TorrentDetails>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var host = AppInit.conf.Rutracker.host;
        var requestHost = AppInit.conf.Rutracker.rqHost();

        foreach (var category in CategoryMap.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildCategoryUrl(requestHost, category, 0);
            var html = await _httpService.Get(
                url,
                cookie: "bb_guid=2hR0qoQAO75s; bb_ssl=1; bb_session=0-44979854-uwt30nnllxWp2KjHA9aH; bb_t=a%3A1%3A%7Bi%3A6799360%3Bi%3A1768325678%3B%7D; cf_clearance=vTaGdw432XmJnTsRLQSnFi36jhuc9KEtzUcFUQRIySM-1768469762-1.2.1.1-Ki2RnZr3tV.MGJ9brpGlIZf9t40joTdrKLbemJq6zB1f_WlIxpKlGQUm5XKHR2j.hQeonEyDnvngQ6UfYMRNPrzBB0y_W01xQA.iLCBiZWGWDmzo3GZqBpaMTa0XoOr_a9h6o_FyRGbCiMBnia0B.uI.3J6W02HjGovuXNBi2DWWE59tnZUespedxp8vEbc_lTn866yEEGfcR7qWmFGHCBlUpmOTEPyiYAMUxh5C99c",
                encoding: RuEncoding,
                timeoutSeconds: 10,
                useProxy: AppInit.conf.Rutracker.useproxy);

            if (string.IsNullOrWhiteSpace(html))
                continue;

            var maxPages = GetMaxPages(html);
            var parsed = ParseForumPage(html, category, host, now);
            foreach (var item in parsed)
                results[item.Url] = item;

            var totalPages = Math.Min(maxPages, MaxPagesPerCategory);
            var delayMs = AppInit.conf.Rutracker.parseDelay == 0 ? 1500 : AppInit.conf.Rutracker.parseDelay;
            for (var page = 1; page <= totalPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageUrl = BuildCategoryUrl(requestHost, category, page);
                var pageHtml = await _httpService.Get(
                    pageUrl,
                    cookie: "bb_guid=2hR0qoQAO75s; bb_ssl=1; bb_session=0-44979854-uwt30nnllxWp2KjHA9aH; bb_t=a%3A1%3A%7Bi%3A6799360%3Bi%3A1768325678%3B%7D; cf_clearance=vTaGdw432XmJnTsRLQSnFi36jhuc9KEtzUcFUQRIySM-1768469762-1.2.1.1-Ki2RnZr3tV.MGJ9brpGlIZf9t40joTdrKLbemJq6zB1f_WlIxpKlGQUm5XKHR2j.hQeonEyDnvngQ6UfYMRNPrzBB0y_W01xQA.iLCBiZWGWDmzo3GZqBpaMTa0XoOr_a9h6o_FyRGbCiMBnia0B.uI.3J6W02HjGovuXNBi2DWWE59tnZUespedxp8vEbc_lTn866yEEGfcR7qWmFGHCBlUpmOTEPyiYAMUxh5C99c",
                    encoding: RuEncoding,
                    timeoutSeconds: 10,
                    useProxy: AppInit.conf.Rutracker.useproxy);

                if (string.IsNullOrWhiteSpace(pageHtml))
                    continue;

                var pageParsed = ParseForumPage(pageHtml, category, host, now);
                foreach (var item in pageParsed)
                    results[item.Url] = item;
                
                if (delayMs > 0)
                    await Task.Delay(delayMs, cancellationToken);
            }
            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken);
        }

        return results.Values.ToArray();
    }

    public async Task<bool> TryEnrichAsync(
        TorrentDetails torrent,
        IReadOnlyDictionary<string, TorrentDetails> existing,
        CancellationToken cancellationToken = default)
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

        var (magnet, createTime) = await FetchTopicDetailsAsync(torrent.Url, cancellationToken);
        if (!string.IsNullOrWhiteSpace(magnet))
            torrent.Magnet = magnet;

        if (createTime != default)
            torrent.CreateTime = createTime;

        return !string.IsNullOrWhiteSpace(torrent.Magnet);
    }

    private static IReadOnlyCollection<TorrentDetails> ParseForumPage(
        string html,
        string categoryId,
        string host,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Array.Empty<TorrentDetails>();

        var cleaned = ReplaceBadNames(html);
        var results = new List<TorrentDetails>();
        var baseForumUri = new Uri(new Uri(host), "forum/");

        foreach (Match rowMatch in RowRegex.Matches(cleaned))
        {
            var row = rowMatch.Value;
            var linkMatch = LinkRegex.Match(row);
            if (!linkMatch.Success)
                continue;

            var href = NormalizeHref(linkMatch.Groups["url"].Value);
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var titleRaw = linkMatch.Groups["title"].Value;
            var title = NormalizeText(WebUtility.HtmlDecode(StripTags(titleRaw)));
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var url = new Uri(baseForumUri, href).ToString();

            var sizeName = string.Empty;
            var sizeBytes = 0L;
            if (TryParseSize(row, out var parsedSizeName, out var parsedSizeBytes))
            {
                sizeName = parsedSizeName;
                sizeBytes = parsedSizeBytes;
            }

            var publishDate = TryParsePublishDate(row) ?? now;

            if (!TryGetCategory(categoryId, out var category))
                continue;

            var (name, originalName, relased) = ParseTitle(title, category);
            if (category.Parser == CategoryParser.Generic && SeasonMarkerRegex.IsMatch(title))
                continue;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
            }

            results.Add(new TorrentDetails
            {
                TrackerName = "rutracker",
                Types = category.Types,
                Url = url,
                Title = title,
                Sid = ParseInt(SeedRegex.Match(row).Groups["value"].Value),
                Pir = ParseInt(LeechRegex.Match(row).Groups["value"].Value),
                SizeName = string.IsNullOrWhiteSpace(sizeName) ? null : sizeName,
                CreateTime = publishDate,
                UpdateTime = now,
                CheckTime = now,
                Name = name,
                OriginalName = originalName,
                Relased = relased,
                Size = sizeBytes
            });
        }

        return results;
    }

    private static (string? name, string? originalName, int relased) ParseTitle(string title, CategoryInfo category)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, 0);

        return category.Parser switch
        {
            CategoryParser.Movie => ParseMovie(title),
            CategoryParser.Serial => ParseSerial(title),
            CategoryParser.Generic => ParseGeneric(title),
            _ => (title.Trim(), null, ExtractYear(title))
        };
    }

    private static (string? name, string? originalName, int relased) ParseMovie(string title)
    {
        var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeMovie(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeMovie(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
        if (IsValidGroups(g, 1, 2))
            return NormalizeMovie(g[1].Value, null, g[2].Value);

        return (title.Trim(), null, ExtractYear(title));
    }

    private static (string? name, string? originalName, int relased) ParseSerial(string title)
    {
        var seasonPattern = Regex.Escape("\u0421\u0435\u0437\u043e\u043d");
        var g = Regex.Match(title,
            $"^([^/\\(\\[]+) / [^/\\(\\[]+ / [^/\\(\\[]+ / ([^/\\(\\[]+) / {seasonPattern}: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeSerial(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title,
            $"^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / {seasonPattern}: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeSerial(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title,
            $"^([^/\\(\\[]+) / ([^/\\(\\[]+) / {seasonPattern}: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeSerial(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title,
            $"^([^/\\(\\[]+) / {seasonPattern}: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2))
            return NormalizeSerial(g[1].Value, null, g[2].Value);

        g = Regex.Match(title,
            "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeSerial(g[1].Value, g[2].Value, g[3].Value);

        g = Regex.Match(title,
            "^([^/\\(\\[]+) / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)",
            RegexOptions.IgnoreCase).Groups;
        if (IsValidGroups(g, 1, 2, 3))
            return NormalizeSerial(g[1].Value, g[2].Value, g[3].Value);

        return (title.Trim(), null, ExtractYear(title));
    }

    private static (string? name, string? originalName, int relased) ParseGeneric(string title)
    {
        var name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;
        if (string.IsNullOrWhiteSpace(name))
            return (title.Trim(), null, ExtractYear(title));

        if (Regex.IsMatch(name, "(\\u0421\\u0435\\u0437\\u043e\\u043d|\\u0421\\u0435\\u0440\\u0438\\u0438)", RegexOptions.IgnoreCase))
            return (null, null, 0);

        var relased = 0;
        var yearMatch = Regex.Match(title, " \\[([0-9]{4})(,|-) ");
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out var parsed))
            relased = parsed;

        return (name.Trim(), null, relased);
    }

    private static (string? name, string? originalName, int relased) NormalizeMovie(string name, string? original, string year)
    {
        var relased = ParseYear(year);
        name = name?.Replace("\u0432 3\u0414", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        original = original?.Replace(" in 3D", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" 3D", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return (name, original, relased);
    }

    private static (string? name, string? originalName, int relased) NormalizeSerial(string name, string? original, string year)
    {
        var relased = ParseYear(year);
        return (name?.Trim(), original?.Trim(), relased);
    }

    private static bool IsValidGroups(GroupCollection groups, params int[] indices)
    {
        return indices.All(i => groups.Count > i && !string.IsNullOrWhiteSpace(groups[i].Value));
    }

    private static int ParseYear(string yearRaw)
    {
        return int.TryParse(yearRaw, out var year) ? year : 0;
    }

    private static int ExtractYear(string title)
    {
        var match = YearRegex.Match(title);
        return match.Success && int.TryParse(match.Value, out var year) ? year : 0;
    }

    private static string ReplaceBadNames(string html)
    {
        return html
            .Replace("\u0412\u0430\u043d\u0434\u0430/\u0412\u0438\u0436\u043d ", "\u0412\u0430\u043d\u0434\u0430\u0412\u0438\u0436\u043d ")
            .Replace("\u0401", "\u0415")
            .Replace("\u0451", "\u0435")
            .Replace("\u0449", "\u0448");
    }

    private static string MatchValue(Regex regex, string input, string groupName)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[groupName].Value.Trim() : string.Empty;
    }

    private static DateTime ParseForumDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return default;

        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) ? parsed : default;
    }

    private static int ParseInt(string raw)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private static string StripTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return TagRegex.Replace(text, string.Empty);
    }

    private static string NormalizeSizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Replace("\u0422\u0411", "TB")
            .Replace("\u0413\u0411", "GB")
            .Replace("\u041c\u0411", "MB")
            .Replace("\u041a\u0411", "KB")
            .Trim();
    }

    private static IReadOnlyDictionary<string, CategoryInfo> BuildCategoryMap()
    {
        var map = new Dictionary<string, CategoryInfo>(StringComparer.OrdinalIgnoreCase);

        Add(map, CategoryParser.Movie, new[] { "movie" },
            "549", "22", "1666", "941", "1950", "2090", "2221", "2091", "2092", "2093", "2200",
            "2540", "934", "505", "124", "1457", "2199", "313", "312", "1247", "2201", "2339", "140",
            "252");

        Add(map, CategoryParser.Movie, new[] { "multfilm" }, "2343", "930", "2365", "208", "539", "209", "1213");
        Add(map, CategoryParser.Serial, new[] { "multserial" }, "921", "815", "1460");

        Add(map, CategoryParser.Serial, new[] { "serial" },
            "842", "235", "242", "819", "1531", "721", "1102", "1120", "1214", "489", "387", "9", "81",
            "119", "1803", "266", "193", "1690", "1459", "825", "1248", "1288", "325", "534", "694",
            "704", "915", "1939");

        Add(map, CategoryParser.Generic, new[] { "anime" }, "1105", "2491", "1389");
        Add(map, CategoryParser.Movie, new[] { "documovie" }, "709", "2109");
        Add(map, CategoryParser.Generic, new[] { "docuserial", "documovie" },
            "46", "671", "2177", "2538", "251", "98", "97", "851", "2178", "821", "2076", "56", "2123",
            "876", "2139", "1467", "1469", "249", "552", "500", "2112", "1327", "1468", "2168", "2160",
            "314", "1281", "2110", "979", "2169", "2164", "2166", "2163");
        Add(map, CategoryParser.Generic, new[] { "tvshow" }, "24", "1959", "939", "1481", "113", "115", "882", "1482", "393", "2537", "532", "827");
        Add(map, CategoryParser.Generic, new[] { "sport" },
            "2103", "2522", "2485", "2486", "2479", "2089", "1794", "845", "2312", "343", "2111",
            "1527", "2069", "1323", "2009", "2000", "2010", "2006", "2007", "2005", "259", "2004",
            "1999", "2001", "2002", "283", "1997", "2003", "1608", "1609", "2294", "1229", "1693",
            "2532", "136", "592", "2533", "1952", "1621", "2075", "1668", "1613", "1614", "1623",
            "1615", "1630", "2425", "2514", "1616", "2014", "1442", "1491", "1987", "1617", "1620",
            "1998", "1343", "751", "1697", "255", "260", "261", "256", "1986", "660", "1551", "626",
            "262", "1326", "978", "1287", "1188", "1667", "1675", "257", "875", "263", "2073", "550",
            "2124", "1470", "528", "486", "854", "2079", "1336", "2171", "1339", "2455", "1434",
            "2350", "1472", "2068", "2016");

        return map;
    }

    private static void Add(Dictionary<string, CategoryInfo> map, CategoryParser parser, string[] types, params string[] categories)
    {
        foreach (var category in categories)
            map[category] = new CategoryInfo(types, parser);
    }

    private static bool TryGetCategory(string categoryId, out CategoryInfo category)
    {
        return CategoryMap.TryGetValue(categoryId, out category!);
    }

    private async Task<(string? magnet, DateTime createTime)> FetchTopicDetailsAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return (null, default);

            var html = await _httpService.Get(
                url,
                encoding: RuEncoding,
                cookie: AppInit.conf.Rutracker.cookie,
                referer: url,
                timeoutSeconds: 7,
                useProxy: AppInit.conf.Rutracker.useproxy);

            if (string.IsNullOrWhiteSpace(html))
                return (null, default);

            var magnetMatch = MagnetRegex.Match(html);
            var magnet = magnetMatch.Success
                ? WebUtility.HtmlDecode(magnetMatch.Groups["magnet"].Value)
                : null;

            var dateRaw = TopicDateRegex.Match(html).Groups["date"].Value;
            var createTime = ParseTopicDate(dateRaw);

            return (string.IsNullOrWhiteSpace(magnet) ? null : magnet, createTime);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Rutracker catalog topic fetch failed for {Url}", url);
            return (null, default);
        }
    }

    private static string BuildCategoryUrl(string host, string categoryId, int page)
    {
        var baseUrl = $"{host.TrimEnd('/')}/forum/tracker.php?f[]={categoryId}&o=10&s=2";
        return page <= 0 ? baseUrl : $"{baseUrl}&start={page * 50}";
    }

    private static int GetMaxPages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        var match = MaxPageRegex.Match(html);
        if (!match.Success)
            return 0;

        return int.TryParse(match.Groups["pages"].Value, out var pages) ? pages : 0;
    }

    private static bool TryParseSize(string row, out string sizeName, out long sizeBytes)
    {
        sizeName = string.Empty;
        sizeBytes = 0L;

        var bytesMatch = SizeBytesRegex.Match(row);
        if (bytesMatch.Success && long.TryParse(bytesMatch.Groups["bytes"].Value, out var bytes))
        {
            sizeBytes = bytes;
            sizeName = FormatSize(bytes);
            return true;
        }

        var match = SizeTextRegex.Match(row);
        if (!match.Success)
            return false;

        var valueRaw = match.Groups["value"].Value.Replace(',', '.');
        if (!double.TryParse(valueRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return false;

        var unitRaw = match.Groups["unit"].Value.ToUpperInvariant();
        var unit = unitRaw switch
        {
            "ТБ" => "TB",
            "ГБ" => "GB",
            "МБ" => "MB",
            "КБ" => "KB",
            _ => unitRaw
        };

        var multiplier = unit switch
        {
            "TB" => 1024d * 1024d * 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "MB" => 1024d * 1024d,
            "KB" => 1024d,
            _ => 1d
        };

        sizeBytes = (long)Math.Round(value * multiplier);
        sizeName = FormatSize(sizeBytes);
        return true;
    }

    private static string FormatSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        double value = bytes;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }

    private static DateTime? TryParsePublishDate(string row)
    {
        var match = DateRegex.Match(row);
        if (!match.Success)
            return null;

        if (!long.TryParse(match.Groups["ts"].Value, out var ts))
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return string.Empty;

        href = WebUtility.HtmlDecode(href).Trim();
        if (href.StartsWith("./", StringComparison.Ordinal))
            href = href[2..];

        if (href.StartsWith("viewtopic.php", StringComparison.OrdinalIgnoreCase))
            return href;

        if (href.StartsWith("/forum/", StringComparison.OrdinalIgnoreCase))
            return href["/forum/".Length..];

        if (href.StartsWith("forum/", StringComparison.OrdinalIgnoreCase))
            return href["forum/".Length..];

        return href;
    }

    private static DateTime ParseTopicDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return default;

        var normalized = NormalizeDate(raw.Replace("-", " "));
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        if (DateTime.TryParseExact(
                normalized,
                "dd.MM.yy HH:mm",
                CultureInfo.GetCultureInfo("ru-RU"),
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(
            normalized,
            CultureInfo.GetCultureInfo("ru-RU"),
            DateTimeStyles.None,
            out parsed)
            ? parsed
            : default;
    }

    private static string NormalizeDate(string value)
    {
        value = ReplaceMonth(value, "\u044f\u043d\u0432", ".01.");
        value = ReplaceMonth(value, "\u0444\u0435\u0432\u0440?", ".02.");
        value = ReplaceMonth(value, "\u043c\u0430\u0440\u0442?", ".03.");
        value = ReplaceMonth(value, "\u0430\u043f\u0440", ".04.");
        value = ReplaceMonth(value, "\u043c\u0430\u0439", ".05.");
        value = ReplaceMonth(value, "\u0438\u044e\u043d\u044c?", ".06.");
        value = ReplaceMonth(value, "\u0438\u044e\u043b\u044c?", ".07.");
        value = ReplaceMonth(value, "\u0430\u0432\u0433", ".08.");
        value = ReplaceMonth(value, "\u0441\u0435\u043d\u0442?", ".09.");
        value = ReplaceMonth(value, "\u043e\u043a\u0442", ".10.");
        value = ReplaceMonth(value, "\u043d\u043e\u044f\u0431?", ".11.");
        value = ReplaceMonth(value, "\u0434\u0435\u043a", ".12.");

        value = ReplaceMonth(value, "\u044f\u043d\u0432\u0430\u0440(\u044c|\u044f)?", ".01.");
        value = ReplaceMonth(value, "\u0444\u0435\u0432\u0440\u0430\u043b(\u044c|\u044f)?", ".02.");
        value = ReplaceMonth(value, "\u043c\u0430\u0440\u0442\u0430?", ".03.");
        value = ReplaceMonth(value, "\u0430\u043f\u0440\u0435\u043b(\u044c|\u044f)?", ".04.");
        value = ReplaceMonth(value, "\u043c\u0430\u0439?\u044f?", ".05.");
        value = ReplaceMonth(value, "\u0438\u044e\u043d(\u044c|\u044f)?", ".06.");
        value = ReplaceMonth(value, "\u0438\u044e\u043b(\u044c|\u044f)?", ".07.");
        value = ReplaceMonth(value, "\u0430\u0432\u0433\u0443\u0441\u0442\u0430?", ".08.");
        value = ReplaceMonth(value, "\u0441\u0435\u043d\u0442\u044f\u0431\u0440(\u044c|\u044f)?", ".09.");
        value = ReplaceMonth(value, "\u043e\u043a\u0442\u044f\u0431\u0440(\u044c|\u044f)?", ".10.");
        value = ReplaceMonth(value, "\u043d\u043e\u044f\u0431\u0440(\u044c|\u044f)?", ".11.");
        value = ReplaceMonth(value, "\u0434\u0435\u043a\u0430\u0431\u0440(\u044c|\u044f)?", ".12.");

        value = ReplaceMonth(value, "Jan", ".01.");
        value = ReplaceMonth(value, "Feb", ".02.");
        value = ReplaceMonth(value, "Mar", ".03.");
        value = ReplaceMonth(value, "Apr", ".04.");
        value = ReplaceMonth(value, "May", ".05.");
        value = ReplaceMonth(value, "Jun", ".06.");
        value = ReplaceMonth(value, "Jul", ".07.");
        value = ReplaceMonth(value, "Aug", ".08.");
        value = ReplaceMonth(value, "Sep", ".09.");
        value = ReplaceMonth(value, "Oct", ".10.");
        value = ReplaceMonth(value, "Nov", ".11.");
        value = ReplaceMonth(value, "Dec", ".12.");

        if (Regex.IsMatch(value, "^[0-9]\\.", RegexOptions.IgnoreCase))
            value = $"0{value}";

        return value;
    }

    private static string ReplaceMonth(string value, string monthPattern, string replacement)
    {
        return Regex.Replace(value, $"\\s{monthPattern}\\.?(\\s|$)", $"{replacement} ", RegexOptions.IgnoreCase);
    }
}
