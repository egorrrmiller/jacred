using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using AngleSharp.Html.Parser;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services.Trackers.RuTor;

public class RuTorSearch : BaseTrackerSearch, ITrackerCatalogEnricher
{
    private readonly HttpService _httpService;
    private readonly ITorrentRepository _torrentRepository;
    private readonly HtmlParser _parser = new();

    public RuTorSearch(HttpService httpService, ITorrentRepository torrentRepository)
    {
        _httpService = httpService;
        _torrentRepository = torrentRepository;
    }

    public override TrackerType Tracker => TrackerType.Rutor;
    public override string TrackerName => "rutor";
    public override string Host => "http://rutor.info/";
    private string SearchUrl => $"{Host}search/0/0/000/2/";

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        var url = SearchUrl + query;
        var html = await _httpService.Get(url, referer: url);
        
        if (string.IsNullOrWhiteSpace(html))
            return Array.Empty<TorrentDetails>();

        var torrents = Parse(html);
        
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
                    new[] { torrent },
                    TryEnrichAsync);
            });

        // Фильтруем раздачи, у которых не определился тип
        return torrents.Where(t => t.Types != null && t.Types.Length > 0).ToList();
    }

    public async Task<bool> TryEnrichAsync(TorrentDetails torrent, IReadOnlyDictionary<string, TorrentDetails> existing)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.Url))
            return false;

        if (existing.TryGetValue(torrent.Url, out var cached))
        {
            if (!string.IsNullOrWhiteSpace(cached.Name))
                torrent.Name = cached.Name;
            
            if (!string.IsNullOrWhiteSpace(cached.OriginalName))
                torrent.OriginalName = cached.OriginalName;
            
            if (cached.Relased > 0)
                torrent.Relased = cached.Relased;
            
            if (cached.Types != null && cached.Types.Length > 0)
                torrent.Types = cached.Types;

            if (!string.IsNullOrWhiteSpace(torrent.Name))
                return true;
        }

        var html = await _httpService.Get(torrent.Url, referer: torrent.Url);
        if (string.IsNullOrWhiteSpace(html))
            return false;

        var document = await _parser.ParseDocumentAsync(html);
        var detailsTable = document.QuerySelector("table#details");
        if (detailsTable == null)
            return false;

        var nameElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Название:"));
        if (nameElement?.NextSibling != null)
            torrent.Name = nameElement.NextSibling.TextContent.Trim();

        var originalNameElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Оригинальное название:"));
        if (originalNameElement?.NextSibling != null)
            torrent.OriginalName = originalNameElement.NextSibling.TextContent.Trim();

        var yearElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Год выхода:"));
        if (yearElement?.NextSibling != null && int.TryParse(yearElement.NextSibling.TextContent.Trim(), out var year))
            torrent.Relased = year;

        var categoryLink = detailsTable.QuerySelectorAll("tr")
            .FirstOrDefault(tr => tr.QuerySelector("td.header")?.TextContent.Contains("Категория") == true)
            ?.QuerySelector("a");

        if (categoryLink != null)
        {
            var href = categoryLink.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                var category = href.Trim('/').Split('/').LastOrDefault();
                if (category != null)
                    torrent.Types = MapCategory(category);
            }
        }
        
        // Parse Quality, VideoType, Voices from details
        var qualityElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Качество:"));
        if (qualityElement?.NextSibling != null)
        {
            var qualityText = qualityElement.NextSibling.TextContent.Trim();
            torrent.Quality = StringConvert.ParseQuality(qualityText);
        }
        
        var formatElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Формат:"));
        if (formatElement?.NextSibling != null)
        {
            torrent.VideoType = formatElement.NextSibling.TextContent.Trim();
        }
        
        var translationElement = detailsTable.QuerySelectorAll("b").FirstOrDefault(e => e.TextContent.Contains("Перевод:"));
        if (translationElement?.NextSibling != null)
        {
            var translationText = translationElement.NextSibling.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(translationText))
            {
                torrent.Voices = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { translationText };
            }
        }

        return !string.IsNullOrWhiteSpace(torrent.Name);
    }

    private static string[] MapCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Array.Empty<string>();
        
        if (category.Contains("seriali", StringComparison.OrdinalIgnoreCase))
            return new[] { "serial" };
        if (category.Contains("anime", StringComparison.OrdinalIgnoreCase))
            return new[] { "anime" };
        if (category.Contains("kino", StringComparison.OrdinalIgnoreCase))
            return new[] { "movie" };
        if (category.Contains("nashe_kino", StringComparison.OrdinalIgnoreCase))
            return new[] { "movie" };
        if (category.Contains("nashi_seriali", StringComparison.OrdinalIgnoreCase))
            return new[] { "serial" };
        if (category.Contains("tv", StringComparison.OrdinalIgnoreCase))
            return new[] { "tvshow" };
        if (category.Contains("multiki", StringComparison.OrdinalIgnoreCase))
            return new[] { "multfilm" };

        return Array.Empty<string>();
    }

    private IReadOnlyCollection<TorrentDetails> Parse(string html)
    {
        var list = new List<TorrentDetails>();
        var document = _parser.ParseDocument(html);
        var rows = document.QuerySelectorAll("#index tr.gai, #index tr.tum");

        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length < 4) continue;

            var dateCell = cells[0];
            var titleCell = cells[1];
            var sizeCell = cells.Length > 3 ? cells[^2] : null; // Size is usually second to last
            var seedsPeersCell = cells.Length > 3 ? cells[^1] : null; // Seeds/Peers is last

            // Handle colspan=2 for title
            if (cells.Length == 4 && cells[1].GetAttribute("colspan") == "2")
            {
                 // Standard layout with colspan
            }
            else if (cells.Length == 5)
            {
                titleCell = cells[1];
                sizeCell = cells[3];
                seedsPeersCell = cells[4];
            }
            
            var magnetLink = titleCell.QuerySelector("a[href^='magnet:']")?.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(magnetLink)) continue;

            var titleLink = titleCell.QuerySelector("a[href^='/torrent/']");
            if (titleLink == null) continue;

            var title = titleLink.TextContent.Trim();
            var url = titleLink.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(url) && !url.StartsWith("http"))
                url = Host.TrimEnd('/') + url;

            long size = 0;
            string? sizeName = null;
            if (sizeCell != null)
            {
                var sizeText = sizeCell.TextContent.Trim();
                // Format: 8.18 GB
                var sizeParts = sizeText.Split(new[] { ' ', '&', '\u00A0' }, StringSplitOptions.RemoveEmptyEntries);
                if (sizeParts.Length >= 2)
                {
                    size = ParseSize(sizeParts[0], sizeParts[1]);
                    sizeName = string.Concat((object?)sizeParts[0], (object?)sizeParts[1]);
                }
            }

            int seeds = 0;
            int peers = 0;
            if (seedsPeersCell != null)
            {
                var seedsElement = seedsPeersCell.QuerySelector("span.green");
                var peersElement = seedsPeersCell.QuerySelector("span.red");

                if (seedsElement != null)
                {
                    // Extract number from text like "S 20" or just "20"
                    var seedsText = seedsElement.TextContent.Trim();
                    var seedsMatch = System.Text.RegularExpressions.Regex.Match(seedsText, @"\d+");
                    if (seedsMatch.Success)
                        int.TryParse(seedsMatch.Value, out seeds);
                }
                
                if (peersElement != null)
                {
                    // Extract number from text like "L 5" or just "5"
                    var peersText = peersElement.TextContent.Trim();
                    var peersMatch = System.Text.RegularExpressions.Regex.Match(peersText, @"\d+");
                    if (peersMatch.Success)
                        int.TryParse(peersMatch.Value, out peers);
                }
            }

            var date = DateTime.UtcNow;
            if (dateCell != null)
            {
                // Format: 09 Янв 26
                var dateText = dateCell.TextContent.Trim();
                var dateParts = dateText.Split(new[] { ' ', '&', '\u00A0' }, StringSplitOptions.RemoveEmptyEntries);
                if (dateParts.Length >= 3)
                {
                    date = ParseDate(dateParts[0], dateParts[1], dateParts[2]);
                }
            }

            list.Add(new TorrentDetails
            {
                TrackerName = TrackerName,
                Title = title,
                Url = url ?? string.Empty,
                Magnet = WebUtility.HtmlDecode(magnetLink),
                Size = size,
                SizeName = sizeName,
                Sid = seeds,
                Pir = peers,
                CreateTime = date,
                UpdateTime = DateTime.UtcNow,
                CheckTime = DateTime.Now,
                Types = Array.Empty<string>()
            });
        }

        return list;
    }

    private static long ParseSize(string val, string unit)
    {
        if (!double.TryParse(val.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return 0;

        var multiplier = unit.ToUpperInvariant() switch
        {
            "TB" => 1024d * 1024d * 1024d * 1024d,
            "GB" => 1024d * 1024d * 1024d,
            "MB" => 1024d * 1024d,
            "KB" => 1024d,
            _ => 1d
        };

        return (long)(value * multiplier);
    }

    private static DateTime ParseDate(string d, string m, string y)
    {
        if (!int.TryParse(d, out var day)) day = 1;
        if (!int.TryParse(y, out var year)) year = 0;
        
        year += 2000; // Assuming 2 digits year
        
        var month = m.ToLowerInvariant() switch
        {
            "янв" => 1,
            "фев" => 2,
            "мар" => 3,
            "апр" => 4,
            "май" => 5,
            "июн" => 6,
            "июл" => 7,
            "авг" => 8,
            "сен" => 9,
            "окт" => 10,
            "ноя" => 11,
            "дек" => 12,
            _ => 1
        };

        try
        {
            return new DateTime(year, month, day);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}