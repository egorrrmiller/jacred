using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JacRed.Core.Enums;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Options;
using JacRed.Core.Utils;
using Microsoft.Extensions.Options;

namespace JacRed.Infrastructure.Services.Trackers.AnimeLayer;

public abstract class BaseAnimeLayer : BaseTrackerSearch
{
    private const string CookieKey = "animelayer:cookie";
    private static readonly Encoding Encoding = Encoding.UTF8;

    private readonly ICacheService _cacheService;
    private readonly Config _config;
    protected readonly HttpService HttpService;

    protected BaseAnimeLayer(ICacheService cacheService, HttpService httpService, IOptionsSnapshot<Config> config)
    {
        _cacheService = cacheService;
        HttpService = httpService;
        _config = config.Value;
    }

    public override TrackerType Tracker => TrackerType.AnimeLayer;
    public override string TrackerName => "animelayer";
    public override string Host => "https://animelayer.ru";

    private string LoginUrl => $"{Host}/auth/login/";

    protected async Task<string> Get(string url, string? referer = null)
    {
        if (!_cacheService.TryGetValue(CookieKey, out string? cookie))
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

    protected async Task<string?> GetMagnet(string url)
    {
        if (!_cacheService.TryGetValue(CookieKey, out string? cookie))
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
        if (!string.IsNullOrWhiteSpace(_config.AnimeLayer.Authorization.Cookie) && !reAuth)
            return _config.AnimeLayer.Authorization.Cookie;

        if (string.IsNullOrWhiteSpace(_config.AnimeLayer.Authorization.Login) ||
            string.IsNullOrWhiteSpace(_config.AnimeLayer.Authorization.Password))
            return string.Empty;

        var formData = new Dictionary<string, string>
        {
            { "login", _config.AnimeLayer.Authorization.Login },
            { "password", _config.AnimeLayer.Authorization.Password }
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
                await _cacheService.SetAsync(CookieKey, cookie, TimeSpan.FromDays(1));
                return cookie;
            }
        }

        return string.Empty;
    }
}