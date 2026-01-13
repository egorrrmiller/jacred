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
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Api.Controllers.CRON;

[Route("/cron/selezen/[action]")]
public class SelezenController : BaseController
{
    private static readonly List<TaskParse> taskParse = new();
    private readonly HttpService _httpService;
    private readonly ITorrentRepository _torrentRepository;

    static SelezenController()
    {
        if (IO.File.Exists("Data/temp/selezen_taskParse.json"))
            taskParse = JsonConvert.DeserializeObject<List<TaskParse>>(
                IO.File.ReadAllText("Data/temp/selezen_taskParse.json"));
    }

    #region UpdateTasksParse

    public async Task<string> UpdateTasksParse()
    {
        // Получаем html
        var html = await _httpService.Get($"{AppInit.conf.Selezen.host}/relizy-ot-selezen/",
            timeoutSeconds: 10,
            useProxy: AppInit.conf.Selezen.useproxy);

        if (html == null) return "html == null";

        // Максимальное количиство страниц
        int.TryParse(Regex.Match(html,
                "<span class='page-link'>...</span></li> <li class='page-item'><a class='page-link' href=\"[^\"]+/page/[0-9]+/\">([0-9]+)</a></li>")
            .Groups[1].Value, out var maxpages);

        if (maxpages == 0) maxpages = 1;

        // Загружаем список страниц в список задач
        for (var page = 1; page <= maxpages; page++)
            try
            {
                if (taskParse.FirstOrDefault(i => i.page == page) == null) taskParse.Add(new TaskParse(page));
            }
            catch
            {
            }

        IO.File.WriteAllText("Data/temp/selezen_taskParse.json", JsonConvert.SerializeObject(taskParse));

        return "ok";
    }

    #endregion

    #region parsePage

    private async Task<bool> parsePage(int page)
    {
        #region Авторизация

        if (Cookie(MemoryCache) == null && string.IsNullOrEmpty(AppInit.conf.Selezen.cookie))
            if (!await TakeLogin())
                return false;

        #endregion

        var cookie = AppInit.conf.Selezen.cookie ?? Cookie(MemoryCache);

        var html = await _httpService.Get(page == 1
                ? $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/"
                : $"{AppInit.conf.Selezen.host}/relizy-ot-selezen/page/{page}/", cookie: cookie,
            useProxy: AppInit.conf.Selezen.useproxy);

        if (html == null || !html.Contains("dle_root")) return false;

        if (!html.Contains($">{AppInit.conf.Selezen.login.u}<"))
        {
            if (string.IsNullOrEmpty(AppInit.conf.Selezen.cookie)) await TakeLogin();

            return false;
        }

        var torrents = new List<TorrentBaseDetails>();

        foreach (var row in MediaNameUtils.Normalize(html)
                     .Split("card overflow-hidden")
                     .Skip(1))
        {
            if (row.Contains(">Аниме</a>") || row.Contains(" [S0")) continue;

            #region Локальный метод - Match

            string Match(string pattern, int index = 1)
            {
                var res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row)
                    .Groups[index]
                    .Value.Trim());

                res = Regex.Replace(res, "[\n\r\t ]+", " ");

                return res.Trim();
            }

            #endregion

            if (string.IsNullOrWhiteSpace(row)) continue;

            #region Дата создания

            var createTime =
                MediaNameUtils.ParseDate(
                    Match("class=\"bx bx-calendar\"></span> ?([0-9]{2}\\.[0-9]{2}\\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"),
                    "dd.MM.yyyy HH:mm");

            if (createTime == default) continue;

            #endregion

            #region Данные раздачи

            var g = Regex.Match(row, "<a href=\"(https?://[^<]+)\"><h4 class=\"card-title\">([^<]+)</h4>")
                .Groups;

            var url = g[1].Value;
            var title = g[2].Value;

            var _sid = Match("<i class=\"bx bx-chevrons-up\"></i>([0-9 ]+)")
                .Trim();

            var _pir = Match("<i class=\"bx bx-chevrons-down\"></i>([0-9 ]+)")
                .Trim();

            var sizeName = Match("<span class=\"bx bx-download\"></span>([^<]+)</a>")
                .Trim();

            if (string.IsNullOrWhiteSpace(url)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(_sid)
                || string.IsNullOrWhiteSpace(_pir)
                || string.IsNullOrWhiteSpace(sizeName))
                continue;

            #endregion

            #region Парсим раздачи

            var relased = 0;

            string name = null,
                originalname = null;

            // Бэд трип / Приколисты в дороге / Bad Trip (2020)
            g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)")
                .Groups;

            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) &&
                !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
            }
            else
            {
                // Летний лагерь / A Week Away (2021)
                g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
                    .Groups;

                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
            }

            #endregion

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0]
                    .Trim();

            if (!string.IsNullOrWhiteSpace(name))
            {
                #region types

                var types = new[]
                {
                    "movie"
                };

                if (row.Contains(">Мульт") || row.Contains(">мульт"))
                    types = new[]
                    {
                        "multfilm"
                    };

                #endregion

                int.TryParse(_sid, out var sid);
                int.TryParse(_pir, out var pir);

                torrents.Add(new TorrentDetails
                {
                    TrackerName = "selezen",
                    Types = types,
                    Url = url,
                    Title = title,
                    Sid = sid,
                    Pir = pir,
                    SizeName = sizeName,
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

            var fullnews = await _httpService.Get(t.Url, cookie: cookie, useProxy: AppInit.conf.Selezen.useproxy);

            if (fullnews != null)
            {
                var _mg = Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"")
                    .Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(_mg))
                {
                    t.Magnet = _mg;

                    return true;
                }
            }

            return false;
        });

        return torrents.Count > 0;
    }

    #endregion

    #region Cookie / TakeLogin

    public SelezenController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, HttpService httpService) :
        base(memoryCache)
    {
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    private static string Cookie(IMemoryCache memoryCache)
    {
        if (memoryCache.TryGetValue("selezen:cookie", out string cookie)) return cookie;

        return null;
    }

    private async Task<bool> TakeLogin()
    {
        var authKey = "selezen:TakeLogin()";

        if (MemoryCache.TryGetValue(authKey, out _)) return false;

        MemoryCache.Set(authKey, 0, TimeSpan.FromMinutes(2));

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
                        "login_name", AppInit.conf.Selezen.login.u
                    },
                    {
                        "login_password", AppInit.conf.Selezen.login.p
                    },
                    {
                        "login_not_save", "1"
                    },
                    {
                        "login", "submit"
                    }
                };

                using (var postContent = new FormUrlEncodedContent(postParams))
                {
                    using (var response = await client.PostAsync(AppInit.conf.Selezen.host, postContent))
                    {
                        if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                        {
                            string PHPSESSID = null;

                            foreach (var line in cook)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (line.Contains("PHPSESSID="))
                                    PHPSESSID = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line)
                                        .Groups[1].Value;
                            }

                            if (!string.IsNullOrWhiteSpace(PHPSESSID))
                            {
                                MemoryCache.Set("selezen:cookie", $"PHPSESSID={PHPSESSID}; _ym_isad=2;",
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

    private static bool _workParse;

    public async Task<string> Parse(int page = 1)
    {
        if (_workParse) return "work";

        _workParse = true;

        try
        {
            await parsePage(page);
        }
        catch
        {
        }
        finally
        {
            _workParse = false;
        }

        return "ok";
    }

    #endregion

    #region ParseAllTask

    private static bool _parseAllTaskWork;

    public async Task<string> ParseAllTask()
    {
        if (_parseAllTaskWork) return "work";

        _parseAllTaskWork = true;

        foreach (var val in taskParse.ToArray())
            try
            {
                if (DateTime.Today == val.updateTime) continue;

                await Task.Delay(AppInit.conf.Selezen.parseDelay);

                var res = await parsePage(val.page);

                if (res) val.updateTime = DateTime.Today;
            }
            catch
            {
            }

        _parseAllTaskWork = false;

        return "ok";
    }

    #endregion
}