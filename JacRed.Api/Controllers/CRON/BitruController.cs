using System;
using System.Collections.Generic;
using System.Linq;
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

[Route("/cron/bitru/[action]")]
public class BitruController : BaseController
{
    private static readonly Dictionary<string, List<TaskParse>> taskParse = new();
    private readonly HttpService _httpService;

    private readonly ITorrentRepository _torrentRepository;

    static BitruController()
    {
        if (IO.File.Exists("Data/temp/bitru_taskParse.json"))
            taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(
                IO.File.ReadAllText("Data/temp/bitru_taskParse.json"));
    }

    #region UpdateTasksParse

    public async Task<string> UpdateTasksParse()
    {
        // movie     - Фильмы    | Фильмы
        // serial    - Сериалы   | Сериалы
        foreach (var cat in new List<string>
                 {
                     "movie",
                     "serial"
                 })
        {
            // Получаем html
            var html = await _httpService.Get($"{AppInit.conf.Bitru.rqHost()}/browse.php?tmp={cat}", timeoutSeconds: 10,
                useProxy: AppInit.conf.Bitru.useproxy);

            if (html == null) continue;

            // Максимальное количиство страниц
            int.TryParse(Regex.Match(html, $"<a href=\"browse.php\\?tmp={cat}&page=[^\"]+\">([0-9]+)</a></div>")
                    .Groups[1].Value,
                out var maxpages);

            if (maxpages == 0) maxpages = 1;

            // Загружаем список страниц в список задач
            for (var page = 1; page <= maxpages; page++)
                try
                {
                    if (!taskParse.ContainsKey(cat)) taskParse.Add(cat, new List<TaskParse>());

                    var val = taskParse[cat];

                    if (val.FirstOrDefault(i => i.page == page) == null) val.Add(new TaskParse(page));
                }
                catch
                {
                }
        }

        IO.File.WriteAllText("Data/temp/bitru_taskParse.json", JsonConvert.SerializeObject(taskParse));

        return "ok";
    }

    #endregion

    #region parsePage

    private async Task<bool> parsePage(string cat, int page)
    {
        var html = await _httpService.Get($"{AppInit.conf.Bitru.rqHost()}/browse.php?tmp={cat}&page={page}",
            useProxy: AppInit.conf.Bitru.useproxy);

        if (html == null || !html.Contains("id=\"logo\"")) return false;

        var torrents = new List<TorrentBaseDetails>();

        foreach (var row in MediaNameUtils.Normalize(html)
                     .Split("<div class=\"b-title\"")
                     .Skip(1))
        {
            if (row.Contains(">Аниме</a>") || row.Contains(">Мульт")) continue;

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

            if (row.Contains("<span>Сегодня"))
                createTime = DateTime.UtcNow;
            else if (row.Contains("<span>Вчера"))
                createTime = DateTime.UtcNow.AddDays(-1);
            else
                createTime =
                    MediaNameUtils.ParseDate(
                        Match("<div class=\"ellips\"><span>([0-9]{2} [^ ]+ [0-9]{4}) в [0-9]{2}:[0-9]{2} от <a"),
                        "dd.MM.yyyy");

            if (createTime == default) continue;

            #endregion

            #region Данные раздачи

            var url = Match("href=\"(details.php\\?id=[0-9]+)\"");
            var title = Match("<div class=\"it-title\">([^<]+)</div>");
            var _sid = Match("<span class=\"b-seeders\">([0-9]+)</span>");
            var _pir = Match("<span class=\"b-leechers\">([0-9]+)</span>");
            var sizeName = Match("title=\"Размер\">([^<]+)</td>");

            if (string.IsNullOrWhiteSpace(url)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(_sid)
                || string.IsNullOrWhiteSpace(_pir)
                || string.IsNullOrWhiteSpace(sizeName))
                continue;

            url = $"{AppInit.conf.Bitru.host}/{url}";

            #endregion

            #region Парсим раздачи

            var relased = 0;

            string name = null,
                originalname = null;

            if (cat == "movie")
            {
                #region Фильмы

                // Звонок из прошлого / Звонок / Kol / The Call (2020)
                var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)")
                    .Groups;

                if (!string.IsNullOrWhiteSpace(g[1].Value)
                    && !string.IsNullOrWhiteSpace(g[2].Value)
                    && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                }
                else
                {
                    // Код бессмертия / Код молодости / Eternal Code (2019)
                    g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)")
                        .Groups;

                    if (!string.IsNullOrWhiteSpace(g[1].Value)
                        && !string.IsNullOrWhiteSpace(g[2].Value)
                        && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                    }
                    else
                    {
                        // Брешь / Breach (2020)
                        g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)")
                            .Groups;

                        if (!string.IsNullOrWhiteSpace(g[1].Value)
                            && !string.IsNullOrWhiteSpace(g[2].Value)
                            && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                        }
                        else
                        {
                            // Жертва (2020)
                            g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)")
                                .Groups;

                            name = g[1].Value;

                            if (int.TryParse(g[2].Value, out var _yer)) relased = _yer;
                        }
                    }
                }

                #endregion
            }
            else if (cat == "serial")
            {
                #region Сериалы

                if (row.Contains("сезон"))
                {
                    // Золотое Божество 3 сезон (1-12 из 12) / Gōruden Kamui / Golden Kamuy (2020)
                    var g = Regex.Match(title,
                            "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)")
                        .Groups;

                    if (!string.IsNullOrWhiteSpace(g[1].Value)
                        && !string.IsNullOrWhiteSpace(g[2].Value)
                        && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                    }
                    else
                    {
                        // Ход королевы / Ферзевый гамбит 1 сезон (1-7 из 7) / The Queen's Gambit (2020)
                        g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)")
                            .Groups;

                        if (!string.IsNullOrWhiteSpace(g[1].Value)
                            && !string.IsNullOrWhiteSpace(g[2].Value)
                            && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                        }
                        else
                        {
                            // Доллар 1 сезон (1-15 из 15) / Dollar (2019)
                            // Эш против Зловещих мертвецов 1-3 сезон (1-30 из 30) / Ash vs Evil Dead (2015-2018)
                            g = Regex.Match(title,
                                    "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)")
                                .Groups;

                            if (!string.IsNullOrWhiteSpace(g[1].Value)
                                && !string.IsNullOrWhiteSpace(g[2].Value)
                                && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out var _yer)) relased = _yer;
                            }
                            else
                            {
                                // СашаТаня 6 сезон (1-19 из 22) (2021)
                                // Метод 1-2 сезон (1-26 из 32) (2015-2020)
                                g = Regex.Match(title,
                                        "^([^/\\(]+) [0-9\\-]+ сезон \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)")
                                    .Groups;

                                name = g[1].Value;

                                if (int.TryParse(g[2].Value, out var _yer)) relased = _yer;
                            }
                        }
                    }
                }
                else
                {
                    // Проспект обороны (1-16 из 16) (2019)
                    var g = Regex.Match(title, "^([^/\\(]+) \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)")
                        .Groups;

                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out var _yer)) relased = _yer;
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
                    case "movie":
                        types = new[]
                        {
                            "movie"
                        };

                        break;

                    case "serial":
                        types = new[]
                        {
                            "serial"
                        };

                        break;
                }

                if (types == null) continue;

                #endregion

                int.TryParse(_sid, out var sid);
                int.TryParse(_pir, out var pir);

                torrents.Add(new TorrentDetails
                {
                    TrackerName = "bitru",
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

            var torrent = await _httpService.Download(t.Url.Replace("/details.php", "/download.php"), referer: t.Url,
                useProxy: AppInit.conf.Bitru.useproxy);

            var magnet = BencodeTo.Magnet(torrent);

            if (magnet != null)
            {
                t.Magnet = magnet;

                return true;
            }

            return false;
        });

        return torrents.Count > 0;
    }

    #endregion

    #region Parse

    private static bool _workParse;

    public BitruController(IMemoryCache memoryCache, HttpService httpService) : base(memoryCache)
    {
        _httpService = httpService;
    }

    public async Task<string> Parse(int page = 1)
    {
        if (_workParse) return "work";

        _workParse = true;
        var log = "";

        try
        {
            // movie     - Фильмы    | Фильмы
            // serial    - Сериалы   | Сериалы
            foreach (var cat in new List<string>
                     {
                         "movie",
                         "serial"
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
            foreach (var task in taskParse.ToArray())
            foreach (var val in task.Value.ToArray())
            {
                if (DateTime.Today == val.updateTime) continue;

                await Task.Delay(AppInit.conf.Bitru.parseDelay);

                var res = await parsePage(task.Key, val.page);

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