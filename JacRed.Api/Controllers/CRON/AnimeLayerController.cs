using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers.CRON;

[Route("/cron/animelayer/[action]")]
public class AnimeLayerController : BaseController
{
    private readonly HttpService _httpService;
    private readonly ITorrentRepository _torrentRepository;

    #region parsePage

    private async Task<bool> parsePage(int page)
    {
        var html = await _httpService.Get($"{AppInit.conf.Animelayer.host}/torrents/anime/?page={page}",
            useProxy: AppInit.conf.Animelayer.useproxy);

        if (html == null || !html.Contains("id=\"wrapper\"")) return false;

        var torrents = new List<TorrentBaseDetails>();

        foreach (var row in MediaNameUtils.Normalize(HttpUtility.HtmlDecode(html.Replace("&nbsp;", "")))
                     .Split("class=\"torrent-item torrent-item-medium panel\"")
                     .Skip(1))
        {
            #region Локальный метод - Match

            string Match(string pattern, int index = 1)
            {
                var res = new Regex(pattern, RegexOptions.IgnoreCase).Match(row)
                    .Groups[index]
                    .Value.Trim();

                res = Regex.Replace(res, "[\n\r\t ]+", " ");

                return res.Trim();
            }

            #endregion

            if (string.IsNullOrWhiteSpace(row)) continue;

            #region Дата создания

            DateTime createTime = default;

            if (Regex.IsMatch(row, "(Добавл|Обновл)[^<]+</span>[0-9]+ [^ ]+ [0-9]{4}"))
            {
                createTime = MediaNameUtils.ParseDate(Match(">(Добавл|Обновл)[^<]+</span>([0-9]+ [^ ]+ [0-9]{4})", 2),
                    "dd.MM.yyyy");
            }
            else
            {
                var date = Match("(Добавл|Обновл)[^<]+</span>([^\n]+) в", 2);

                if (string.IsNullOrWhiteSpace(date)) continue;

                createTime = MediaNameUtils.ParseDate($"{date} {DateTime.Today.Year}", "dd.MM.yyyy");
            }

            if (createTime == default)
            {
                if (page != 1) continue;

                createTime = DateTime.UtcNow;
            }

            #endregion

            #region Данные раздачи

            var gurl = Regex.Match(row, "<a href=\"/(torrent/[a-z0-9]+)/?\">([^<]+)</a>")
                .Groups;

            var url = gurl[1].Value;
            var title = gurl[2].Value;

            var _sid = Match("class=\"icon s-icons-upload\"></i>([0-9]+)");
            var _pir = Match("class=\"icon s-icons-download\"></i>([0-9]+)");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title)) continue;

            if (Regex.IsMatch(row, "Разрешение: ?</strong>1920x1080"))
                title += " [1080p]";
            else if (Regex.IsMatch(row, "Разрешение: ?</strong>1280x720")) title += " [720p]";

            url = $"{AppInit.conf.Animelayer.host}/{url}/";

            #endregion

            #region name / originalname

            string name = null,
                originalname = null;

            // Shaman king (2021) / Король-шаман [ТВ] (1-7)
            var g = Regex.Match(title, "([^/\\[\\(]+)\\([0-9]{4}\\)[^/]+/([^/\\[\\(]+)")
                .Groups;

            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                name = g[2]
                    .Value.Trim();

                originalname = g[1]
                    .Value.Trim();
            }
            else
            {
                // Shadows House / Дом теней (1—6)
                g = Regex.Match(title, "^([^/\\[\\(]+)/([^/\\[\\(]+)")
                    .Groups;

                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[2]
                        .Value.Trim();

                    originalname = g[1]
                        .Value.Trim();
                }
            }

            #endregion

            // Год выхода
            if (!int.TryParse(Match("Год выхода: ?</strong>([0-9]{4})"), out var relased) || relased == 0) continue;

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0]
                    .Trim();

            if (!string.IsNullOrWhiteSpace(name))
            {
                int.TryParse(_sid, out var sid);
                int.TryParse(_pir, out var pir);

                torrents.Add(new TorrentDetails
                {
                    TrackerName = "animelayer",
                    Types = new[]
                    {
                        "anime"
                    },
                    Url = url,
                    Title = title,
                    Sid = sid,
                    Pir = pir,
                    CreateTime = createTime,
                    Name = name,
                    OriginalName = originalname,
                    Relased = relased
                });
            }
        }

        await _torrentRepository.AddOrUpdateAsync(torrents, async (t, db) =>
        {
            if (db.TryGetValue(t.Url, out var _tcache) && _tcache.Title == t.Title) return true;

            var torrent = await _httpService.Download($"{t.Url}download/", Cookie(MemoryCache));
            var magnet = BencodeTo.Magnet(torrent);
            var sizeName = BencodeTo.SizeName(torrent);

            if (!string.IsNullOrWhiteSpace(magnet) && !string.IsNullOrWhiteSpace(sizeName))
            {
                t.Magnet = magnet;
                t.SizeName = sizeName;

                return true;
            }

            return false;
        });

        return torrents.Count > 0;
    }

    #endregion

    #region TakeLogin

    public AnimeLayerController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, HttpService httpService)
        : base(memoryCache)
    {
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    private static string Cookie(IMemoryCache memoryCache)
    {
        if (memoryCache.TryGetValue("animelayer:cookie", out string cookie)) return cookie;

        return null;
    }

    public async Task<bool> TakeLogin()
    {
        try
        {
            var clientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

            clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            using (var client = new HttpClient(clientHandler))
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.MaxResponseContentBufferSize = 2000000; // 2MB

                client.DefaultRequestHeaders.Add("user-agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                var postParams = new Dictionary<string, string>
                {
                    {
                        "login", AppInit.conf.Animelayer.login.u
                    },
                    {
                        "password", AppInit.conf.Animelayer.login.p
                    }
                };

                using (var postContent = new FormUrlEncodedContent(postParams))
                {
                    using (var response =
                           await client.PostAsync($"{AppInit.conf.Animelayer.host}/auth/login/", postContent))
                    {
                        if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                        {
                            string layer_id = null,
                                layer_hash = null,
                                PHPSESSID = null;

                            foreach (var line in cook)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (line.Contains("layer_id="))
                                    layer_id = new Regex("layer_id=([^;]+)(;|$)").Match(line)
                                        .Groups[1].Value;

                                if (line.Contains("layer_hash="))
                                    layer_hash = new Regex("layer_hash=([^;]+)(;|$)").Match(line)
                                        .Groups[1].Value;

                                if (line.Contains("PHPSESSID="))
                                    PHPSESSID = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line)
                                        .Groups[1].Value;
                            }

                            if (!string.IsNullOrWhiteSpace(layer_id)
                                && !string.IsNullOrWhiteSpace(layer_hash)
                                && !string.IsNullOrWhiteSpace(PHPSESSID))
                            {
                                MemoryCache.Set("animelayer:cookie",
                                    $"layer_id={layer_id}; layer_hash={layer_hash}; PHPSESSID={PHPSESSID};",
                                    DateTime.Now.AddDays(1));

                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return false;
    }

    #endregion

    #region Parse

    private static bool workParse;

    public async Task<string> Parse(int maxpage = 1)
    {
        #region Авторизация

        if (Cookie(MemoryCache) == null)
            if (!await TakeLogin())
                return "Не удалось авторизоваться";

        #endregion

        if (workParse) return "work";

        workParse = true;

        try
        {
            for (var page = 1; page <= maxpage; page++)
            {
                if (page > 1) await Task.Delay(AppInit.conf.Animelayer.parseDelay);

                await parsePage(page);
            }
        }
        catch
        {
        }
        finally
        {
            workParse = false;
        }

        return "ok";
    }

    #endregion
}