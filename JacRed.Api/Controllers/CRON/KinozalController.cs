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

[Route("/cron/kinozal/[action]")]
public class KinozalController : BaseController
{
    private static readonly Dictionary<string, Dictionary<string, List<TaskParse>>> taskParse = new();
    private readonly HttpService _httpService;

    private readonly ITorrentRepository _torrentRepository;

    static KinozalController()
    {
        if (IO.File.Exists("Data/temp/kinozal_taskParse.json"))
            taskParse = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<TaskParse>>>>(
                IO.File.ReadAllText("Data/temp/kinozal_taskParse.json"));
    }

    #region UpdateTasksParse

    public async Task<string> UpdateTasksParse()
    {
        foreach (var cat in new List<string>
                 {
                     // Сериалы
                     "45",
                     "46",

                     // Фильмы
                     "8",
                     "6",
                     "15",
                     "17",
                     "35",
                     "39",
                     "13",
                     "14",
                     "24",
                     "11",
                     "9",
                     "47",
                     "18",
                     "37",
                     "12",
                     "10",
                     "7",
                     "16",

                     // ТВ-шоу
                     "49",
                     "50",

                     // Мульты
                     "21",
                     "22",

                     // Аниме
                     "20"
                 })
            for (var year = DateTime.Today.Year; year >= 1990; year--)
            {
                // Получаем html
                var html = await _httpService.Get($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&d={year}&t=1",
                    timeoutSeconds: 10, useProxy: AppInit.conf.Kinozal.useproxy);

                if (html == null) continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, ">([0-9]+)</a></li><li><a rel=\"next\"")
                        .Groups[1].Value,
                    out var maxpages);

                // Загружаем список страниц в список задач
                for (var page = 0; page <= maxpages; page++)
                    try
                    {
                        if (!taskParse.ContainsKey(cat)) taskParse.Add(cat, new Dictionary<string, List<TaskParse>>());

                        var arg = $"&d={year}&t=1";
                        var catVal = taskParse[cat];

                        if (!catVal.ContainsKey(arg)) catVal.Add(arg, new List<TaskParse>());

                        var val = catVal[arg];

                        if (val.FirstOrDefault(i => i.page == page) == null) val.Add(new TaskParse(page));
                    }
                    catch
                    {
                    }
            }

        IO.File.WriteAllText("Data/temp/kinozal_taskParse.json", JsonConvert.SerializeObject(taskParse));

        return "ok";
    }

    #endregion

    #region parsePage

    private async Task<bool> parsePage(string cat, int page, string arg = null)
    {
        var html = await _httpService.Get($"{AppInit.conf.Kinozal.host}/browse.php?c={cat}&page={page}" + arg,
            useProxy: AppInit.conf.Kinozal.useproxy);

        if (html == null || !html.Contains("Кинозал.ТВ</title>")) return false;

        if (Cookie == null || !html.Contains(">Выход</a>")) TakeLogin();

        var torrents = new List<TorrentBaseDetails>();

        foreach (var row in Regex.Split(MediaNameUtils.Normalize(html), "<tr class=('first bg'|bg)>")
                     .Skip(1))
        {
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

            DateTime createTime = default;

            if (row.Contains("<td class='s'>сегодня"))
                createTime = DateTime.UtcNow;
            else if (row.Contains("<td class='s'>вчера"))
                createTime = DateTime.UtcNow.AddDays(-1);
            else
                createTime =
                    MediaNameUtils.ParseDate(
                        Match("<td class='s'>([0-9]{2}.[0-9]{2}.[0-9]{4}) в [0-9]{2}:[0-9]{2}</td>"),
                        "dd.MM.yyyy");

            if (createTime == default) continue;

            #endregion

            #region Данные раздачи

            var url = Match("href=\"/(details.php\\?id=[0-9]+)\"");
            var title = Match("class=\"r[0-9]+\">([^<]+)</a>");
            var _sid = Match("<td class='sl_s'>([0-9]+)</td>");
            var _pir = Match("<td class='sl_p'>([0-9]+)</td>");
            var sizeName = Match("<td class='s'>([0-9\\.,]+ (МБ|ГБ))</td>");

            if (string.IsNullOrWhiteSpace(url)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(_sid)
                || string.IsNullOrWhiteSpace(_pir)
                || string.IsNullOrWhiteSpace(sizeName))
                continue;

            url = "http://kinozal.tv/" + url;

            #endregion

            #region Парсим раздачи

            var relased = 0;

            string name = null,
                originalname = null;

            if (cat is "8" or "6" or "15" or "17" or "35" or "39" or "13" or "14" or "24" or "11" or "9" or "47" or "18"
                or "37" or "12" or "10" or "7" or "16")
            {
                #region Фильмы

                // Бэд трип (Приколисты в дороге) / Bad Trip / 2020 / ДБ, СТ / WEB-DLRip (AVC)
                // Интерстеллар / Interstellar (IMAX Edition) / 2014 / ДБ / BDRip
                // Успеть всё за месяц / 30 jours max / 2020 / ЛМ / WEB-DLRip
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})")
                    .Groups;

                if (!string.IsNullOrWhiteSpace(g[1].Value)
                    && !string.IsNullOrWhiteSpace(g[3].Value)
                    && !string.IsNullOrWhiteSpace(g[5].Value))
                {
                    name = g[1].Value;
                    originalname = g[3].Value;

                    if (int.TryParse(g[5].Value, out var _yer)) relased = _yer;
                }
                else
                {
                    // Голая правда / 2020 / ЛМ / WEB-DLRip
                    g = Regex.Match(title, "^([^/\\(]+) / ([0-9]{4})")
                        .Groups;

                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out var _yer)) relased = _yer;
                }

                #endregion
            }
            else if (cat == "45" || cat == "22")
            {
                #region Сериал - Русский

                if (row.Contains("сезон"))
                {
                    // Сельский детектив (6 сезон: 1-2 серии из 2) ([^/]+)?/ 2020 / РУ / WEB-DLRip (AVC)
                    // Любовь в рабочие недели (1 сезон: 1 серия из 15) / 2020 / РУ / WEB-DLRip (AVC)
                    // Фитнес (Королева фитнеса) (1-4 сезон: 1-80 серии из 80) / 2018-2020 / РУ / WEB-DLRip
                    // Бывшие (1-3 сезон: 1-24 серии из 24) / 2016-2020 / РУ / WEB-DLRip (AVC)
                    var g = Regex.Match(title,
                            "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})")
                        .Groups;

                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[4].Value))
                    {
                        name = g[1].Value;

                        if (int.TryParse(g[4].Value, out var _yer)) relased = _yer;
                    }
                }
                else
                {
                    // Авантюра на двоих (1-8 серии из 8) / 2021 / РУ /  WEBRip (AVC)
                    // Жизнь после жизни (Небеса подождут) (1-16 серии из 16) / 2016 / РУ / WEB-DLRip
                    var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([0-9]{4})")
                        .Groups;

                    name = g[1].Value;

                    if (int.TryParse(g[4].Value, out var _yer)) relased = _yer;
                }

                #endregion
            }
            else if (cat == "46" || cat == "21" || cat == "20")
            {
                #region Сериал - Буржуйский

                if (row.Contains("сезон"))
                {
                    // Сокол и Зимний солдат (1 сезон: 1-2 серия из 6) / The Falcon and the Winter Soldier / 2021 / ЛД (#NW), СТ / WEB-DL (1080p)
                    // Голубая кровь (Семейная традиция) (11 сезон: 1-9 серия из 20) / Blue Bloods / 2020 / ПМ (BaibaKo) / WEBRip
                    var g = Regex.Match(title,
                            "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([0-9\\-]+ сезоны?: [^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})")
                        .Groups;

                    if (!string.IsNullOrWhiteSpace(g[1].Value)
                        && !string.IsNullOrWhiteSpace(g[4].Value)
                        && !string.IsNullOrWhiteSpace(g[5].Value))
                    {
                        name = g[1].Value;
                        originalname = g[4].Value;

                        if (int.TryParse(g[5].Value, out var _yer)) relased = _yer;
                    }
                }
                else
                {
                    // Дикий ангел (151-270 серии из 270) / Muneca Brava / 1998-1999 / ПМ / DVB
                    var g = Regex.Match(title,
                            "^([^\\(/]+) (\\([^\\)/]+\\) )?\\([^\\)/]+\\) ([^/]+ )?/ ([^\\(/]+) / ([0-9]{4})")
                        .Groups;

                    if (!string.IsNullOrWhiteSpace(g[1].Value)
                        && !string.IsNullOrWhiteSpace(g[4].Value)
                        && !string.IsNullOrWhiteSpace(g[5].Value))
                    {
                        name = g[1].Value;
                        originalname = g[4].Value;

                        if (int.TryParse(g[5].Value, out var _yer)) relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^\\(/]+) / ([^\\(/]+) / ([0-9]{4})")
                            .Groups;

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                    }
                }

                #endregion
            }
            else if (cat == "49" || cat == "50")
            {
                #region ТВ-шоу

                // Топ Гир (30 сезон: 1-2 выпуски из 10) / Top Gear / 2021 / ЛМ (ColdFilm) / WEBRip
                var g = Regex.Match(title, "^([^\\(/]+) (\\([^\\)/]+\\) )?/ ([^\\(/]+) / ([0-9]{4})")
                    .Groups;

                if (!string.IsNullOrWhiteSpace(g[1].Value)
                    && !string.IsNullOrWhiteSpace(g[3].Value)
                    && !string.IsNullOrWhiteSpace(g[4].Value))
                {
                    name = g[1].Value;
                    originalname = g[3].Value;

                    if (int.TryParse(g[4].Value, out var _yer)) relased = _yer;
                }
                else
                {
                    // Супермама (3 сезон: 1-12 выпуски из 40) / 2021 / РУ / IPTV (1080p)
                    g = Regex.Match(title, "^([^/\\(]+) (\\([^\\)/]+\\) )?/ ([0-9]{4})")
                        .Groups;

                    name = g[1].Value;

                    if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                }

                #endregion
            }

            #endregion

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0]
                    .Trim();

            if (!string.IsNullOrWhiteSpace(name))
            {
                #region types

                string[] types = null;

                switch (cat)
                {
                    case "8":
                    case "6":
                    case "15":
                    case "17":
                    case "35":
                    case "39":
                    case "13":
                    case "14":
                    case "24":
                    case "11":
                    case "9":
                    case "47":
                    case "18":
                    case "37":
                    case "12":
                    case "10":
                    case "7":
                    case "16":
                        types = new[]
                        {
                            "movie"
                        };

                        break;

                    case "45":
                    case "46":
                        types = new[]
                        {
                            "serial"
                        };

                        break;

                    case "49":
                    case "50":
                        types = new[]
                        {
                            "tvshow"
                        };

                        break;

                    case "21":
                    case "22":
                        types = new[]
                        {
                            "multfilm",
                            "multserial"
                        };

                        break;

                    case "20":
                        types = new[]
                        {
                            "anime"
                        };

                        break;
                }

                if (types == null) continue;

                #endregion

                int.TryParse(_sid, out var sid);
                int.TryParse(_pir, out var pir);

                torrents.Add(new TorrentDetails
                {
                    TrackerName = "kinozal",
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

            var id = Regex.Match(t.Url, "\\?id=([0-9]+)")
                .Groups[1].Value;

            var srv_details = await _httpService.Post(
                $"{AppInit.conf.Kinozal.host}/get_srv_details.php?id={id}&action=2",
                $"id={id}&action=2", Cookie, useProxy: AppInit.conf.Kinozal.useproxy);

            if (srv_details != null)
            {
                var torrentHash = new Regex("<ul><li>Инфо хеш: +([^<]+)</li>").Match(srv_details)
                    .Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(torrentHash))
                {
                    t.Magnet = $"magnet:?xt=urn:btih:{torrentHash}";

                    return true;
                }
            }

            return false;
        });

        return torrents.Count > 0;
    }

    #endregion

    #region Cookie / TakeLogin

    private static string Cookie;

    public KinozalController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, HttpService httpService) :
        base(memoryCache)
    {
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    private async void TakeLogin()
    {
        var authKey = "kinozal:TakeLogin()";

        if (MemoryCache.TryGetValue(authKey, out _)) return;

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
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/99.0.4844.51 Safari/537.36");

                client.DefaultRequestHeaders.Add("cache-control", "no-cache");
                client.DefaultRequestHeaders.Add("dnt", "1");
                client.DefaultRequestHeaders.Add("origin", AppInit.conf.Kinozal.host);
                client.DefaultRequestHeaders.Add("pragma", "no-cache");
                client.DefaultRequestHeaders.Add("referer", $"{AppInit.conf.Kinozal.host}/");
                client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");

                var postParams = new Dictionary<string, string>
                {
                    {
                        "username", AppInit.conf.Kinozal.login.u
                    },
                    {
                        "password", AppInit.conf.Kinozal.login.p
                    },
                    {
                        "returnto", ""
                    }
                };

                using (var postContent = new FormUrlEncodedContent(postParams))
                {
                    using (var response =
                           await client.PostAsync($"{AppInit.conf.Kinozal.host}/takelogin.php", postContent))
                    {
                        if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                        {
                            string uid = null,
                                pass = null;

                            foreach (var line in cook)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (line.Contains("uid="))
                                    uid = new Regex("uid=([0-9]+)").Match(line)
                                        .Groups[1].Value;

                                if (line.Contains("pass="))
                                    pass = new Regex("pass=([^;]+)(;|$)").Match(line)
                                        .Groups[1].Value;
                            }

                            if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(pass))
                                Cookie = $"uid={uid}; pass={pass};";
                        }
                    }
                }
            }
        }
        catch
        {
        }
    }

    #endregion

    #region Parse

    private static bool _workParse;

    public async Task<string> Parse(int page)
    {
        if (_workParse) return "work";

        var log = "";
        _workParse = true;

        try
        {
            foreach (var cat in new List<string>
                     {
                         // Сериалы
                         "45",
                         "46",

                         // Фильмы
                         "8",
                         "6",
                         "15",
                         "17",
                         "35",
                         "39",
                         "13",
                         "14",
                         "24",
                         "11",
                         "9",
                         "47",
                         "18",
                         "37",
                         "12",
                         "10",
                         "7",
                         "16",

                         // ТВ-шоу
                         "49",
                         "50",

                         // Мульты
                         "21",
                         "22",

                         // Аниме
                         "20"
                     })
            {
                await parsePage(cat, page);
                log += $"{cat} - {page}\n";
            }
        }
        catch
        {
        }
        finally
        {
            _workParse = false;
        }

        return string.IsNullOrWhiteSpace(log)
            ? "ok"
            : log;
    }

    #endregion

    #region ParseAllTask

    private static bool _parseAllTaskWork;

    public async Task<string> ParseAllTask()
    {
        if (_parseAllTaskWork) return "work";

        _parseAllTaskWork = true;

        try
        {
            foreach (var cat in taskParse.ToArray())
            foreach (var arg in cat.Value.ToArray())
            foreach (var val in arg.Value.ToArray())
            {
                if (DateTime.Today == val.updateTime) continue;

                await Task.Delay(AppInit.conf.Kinozal.parseDelay);

                var res = await parsePage(cat.Key, val.page, arg.Key);

                if (res) val.updateTime = DateTime.Today;
            }
        }
        catch
        {
        }

        _parseAllTaskWork = false;

        return "ok";
    }

    #endregion
}