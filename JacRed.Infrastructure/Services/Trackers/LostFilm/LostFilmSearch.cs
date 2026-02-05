using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using JacRed.Core.Enums;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;
using MonoTorrent;

namespace JacRed.Infrastructure.Services.Trackers.LostFilm;

public class LostFilmSearch : BaseTrackerSearch
{
    private readonly HttpService _httpService;
    private readonly Config _config;
    private readonly HtmlParser _parser = new();

    public LostFilmSearch(HttpService httpService, IOptionsSnapshot<Config> config)
    {
        _httpService = httpService;
        _config = config.Value;
    }

    public override TrackerType Tracker => TrackerType.Lostfilm;
    public override string TrackerName => "lostfilm";
    public override string Host => "https://www.lostfilm.tv";

    public override async Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_config.LostFilm.Cookie))
            return [];

        var searchUrl = $"{Host}/ajaxik.php";
        var formData = new Dictionary<string, string>
        {
            { "act", "serial" },
            { "type", "search" },
            { "o", "0" },
            { "s", "1" },
            { "t", "0" },
            { "q", query.Replace("-", " ") }
        };

        var json = await _httpService.Post(searchUrl, new FormUrlEncodedContent(formData), cookie: _config.LostFilm.Cookie,
            addHeaders: [("Referer", $"{Host}/search/?q={query}")]);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var response = JsonSerializer.Deserialize<LostFilmSearchResponse>(json);
            if (response?.Data == null || response.Data.Count == 0)
                return [];

            var results = new ConcurrentBag<TorrentDetails>();
            var seriesToProcess = response.Data.Take(3).ToList();

            await Parallel.ForEachAsync(seriesToProcess, async (series, _) =>
            {
                var torrents = await GetTorrentsForSeries(series);
                foreach (var t in torrents)
                    results.Add(t);
            });

            return results;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<TorrentDetails>> GetTorrentsForSeries(LostFilmSeries series)
    {
        var url = $"{Host}/series/{series.Alias}";
        var html = await _httpService.Get(url, referer: Host, cookie: _config.LostFilm.Cookie);
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var document = await _parser.ParseDocumentAsync(html);
        var results = new List<TorrentDetails>();
        
        var episodes = document.QuerySelectorAll(".serie-block");
        
        foreach (var episode in episodes)
        {
            var onClick = episode.GetAttribute("onclick");
            var episodeIdMatch = Regex.Match(onClick ?? "", @"PlayEpisode\((\d+)\)");
            if (!episodeIdMatch.Success) continue;
            
            var episodeId = episodeIdMatch.Groups[1].Value;
            
            var torrentLinks = await GetTorrentLinks(episodeId);
            if (torrentLinks.Count == 0) continue;

            var titleElement = episode.QuerySelector(".alpha");
            var title = titleElement?.TextContent.Trim() ?? series.Title;
            var betaText = episode.QuerySelector(".beta")?.TextContent.Trim();
            
            foreach (var link in torrentLinks)
            {
                var (magnet, size) = await DownloadAndParseTorrent(link.Url);
                if (string.IsNullOrWhiteSpace(magnet)) continue;

                results.Add(new TorrentDetails
                {
                    TrackerName = TrackerName,
                    Title = $"{series.Title} / {series.OriginalTitle}. {betaText ?? title} [{link.Quality}]",
                    Url = url,
                    Magnet = magnet,
                    Size = size,
                    SizeName = StringConvert.FormatSize(size),
                    CreateTime = DateTime.UtcNow,
                    Types = ["serial"],
                    Name = series.Title,
                    OriginalName = series.OriginalTitle
                });
            }
        }

        return results;
    }

    private async Task<List<TorrentLink>> GetTorrentLinks(string episodeId)
    {
        var formData = new Dictionary<string, string>
        {
            { "act", "serial" },
            { "type", "get_torrent" },
            { "id", episodeId }
        };
        
        var json = await _httpService.Post($"{Host}/ajaxik.php", new FormUrlEncodedContent(formData), cookie: _config.LostFilm.Cookie);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try 
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("result").GetString() != "ok") return [];
            
            var data = root.GetProperty("data");
            var links = new List<TorrentLink>();
            
            foreach (var prop in data.EnumerateObject())
            {
                var quality = prop.Name; // "1080p", "SD", etc.
                var url = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    links.Add(new TorrentLink(quality, url));
                }
            }
            
            return links;
        }
        catch
        {
            return [];
        }
    }

    private async Task<(string? magnet, long size)> DownloadAndParseTorrent(string url)
    {
        try
        {
            var bytes = await _httpService.Download(url, cookie: _config.LostFilm.Cookie, referer: Host);
            if (bytes == null || bytes.Length == 0) return (null, 0);

            using var stream = new MemoryStream(bytes);
            var torrent = await Torrent.LoadAsync(stream);
            
            var infoHash = torrent.InfoHashes.V1 ?? torrent.InfoHashes.V2;
            if (infoHash == null) return (null, 0);

            var magnet = $"magnet:?xt=urn:btih:{infoHash.ToHex()}&dn={Uri.EscapeDataString(torrent.Name)}";

            if (torrent.AnnounceUrls.Count == 0) return (magnet, torrent.Size);
            
            magnet = torrent.AnnounceUrls.SelectMany(tier => tier).Aggregate(magnet, (current, tracker) => current + $"&tr={Uri.EscapeDataString(tracker)}");

            return (magnet, torrent.Size);
        }
        catch
        {
            return (null, 0);
        }
    }

    private record TorrentLink(string Quality, string Url);

    private class LostFilmSearchResponse
    {
        [JsonPropertyName("data")]
        public List<LostFilmSeries>? Data { get; set; }
        
        [JsonPropertyName("result")]
        public string? Result { get; set; }
    }

    private class LostFilmSeries
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("alias")]
        public string? Alias { get; set; }
        
        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; set; }
    }
}