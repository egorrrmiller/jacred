using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.AnimeLayer;

public abstract class BaseAnimeLayer : BaseTrackerSearch, ITrackerCatalogEnricher
{
    private const string CookieKey = "animelayer:cookie";
    private static readonly Encoding Encoding = Encoding.UTF8;

    protected BaseAnimeLayer(ICacheService cacheService, HttpService httpService, IOptionsSnapshot<Config> config) :
        base(config, httpService, cacheService)
    {
    }

    public override TrackerType Tracker => TrackerType.AnimeLayer;
    public override string TrackerName => "animelayer";
    public override string Host => "https://animelayer.ru";

    private string LoginUrl => $"{Host}/auth/login/";

    public async Task<bool> FetchDetailsAsync(TorrentDetails torrent)
    {
        if (string.IsNullOrWhiteSpace(torrent.Url))
            return false;

        var magnet = await GetMagnet($"{torrent.Url}download/?type=magnet");
        if (!string.IsNullOrWhiteSpace(magnet))
            torrent.Magnet = magnet;

        return !string.IsNullOrWhiteSpace(torrent.Magnet);
    }

    protected async Task<string> Get(string url, string? referer = null)
    {
        if (!CacheService.TryGetValue(CookieKey, out string? cookie))
            cookie = await Authorize();

        var html = await HttpService.Get(url, Encoding, cookie, referer);

        // Проверка на разлогин
        if (string.IsNullOrWhiteSpace(html) || (html.Contains("name=\"login\"") && html.Contains("name=\"password\"")))
        {
            cookie = await Authorize(true);
            html = await HttpService.Get(url, Encoding, cookie, referer);
        }

        return html;
    }

    private async Task<string?> GetMagnet(string url)
    {
        if (!CacheService.TryGetValue(CookieKey, out string? cookie))
            cookie = await Authorize();

        var response = await HttpService.PostResponse(url, null, cookie, allowRedirect: false);

        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            cookie = await Authorize(true);
            response = await HttpService.PostResponse(url, null, cookie, allowRedirect: false);
        }

        return response.Headers.TryGetValues("Location", out var locations) ? locations.FirstOrDefault() : null;
    }

    private async Task<string> Authorize(bool reAuth = false)
    {
        if (!string.IsNullOrWhiteSpace(Config.AnimeLayer.Authorization.Cookie) && !reAuth)
            return Config.AnimeLayer.Authorization.Cookie;

        if (string.IsNullOrWhiteSpace(Config.AnimeLayer.Authorization.Login) ||
            string.IsNullOrWhiteSpace(Config.AnimeLayer.Authorization.Password))
            return string.Empty;

        var formData = new Dictionary<string, string>
        {
            { "login", Config.AnimeLayer.Authorization.Login },
            { "password", Config.AnimeLayer.Authorization.Password }
        };

        var response = await HttpService.PostResponse(
            LoginUrl,
            new FormUrlEncodedContent(formData),
            allowRedirect: false);

        if (response.Headers.TryGetValues("Set-Cookie", out var cook))
        {
            string? layerId = null, layerHash = null, phpsessid = null;
            foreach (var line in cook)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.Contains("layer_id="))
                    layerId = Regex.Match(line, "layer_id=([^;]+)(;|$)").Groups[1].Value;

                if (line.Contains("layer_hash="))
                    layerHash = Regex.Match(line, "layer_hash=([^;]+)(;|$)").Groups[1].Value;

                if (line.Contains("PHPSESSID="))
                    phpsessid = Regex.Match(line, "PHPSESSID=([^;]+)(;|$)").Groups[1].Value;
            }

            if (!string.IsNullOrWhiteSpace(layerId) && !string.IsNullOrWhiteSpace(layerHash) &&
                !string.IsNullOrWhiteSpace(phpsessid))
            {
                var cookie = $"layer_id={layerId}; layer_hash={layerHash}; PHPSESSID={phpsessid};";
                await CacheService.SetAsync(CookieKey, cookie, TimeSpan.FromDays(Config.Cache.AuthExpiry));
                return cookie;
            }
        }

        return string.Empty;
    }
}