using System;
using System.Collections.Generic;
using System.Linq;
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

namespace JacRed.Api.Controllers.CRON.RIP;

[Route("/cron/hdrezka/[action]")]
public class HDRezkaController : BaseController
{
    private readonly HttpService _httpService;

    #region parsePage

    private readonly ITorrentRepository _torrentRepository;

    private async Task<bool> parsePage(int page)
    {
        var html = await _httpService.Get(AppInit.conf.Rezka.rqHost()
                                          + (page > 1
                                              ? $"/page/{page}"
                                              : ""),
            useProxy: AppInit.conf.Rezka.useproxy);

        if (html == null || !html.Contains("id=\"main_wrapper\"")) return false;

        var torrents = new List<TorrentBaseDetails>();

        foreach (var row in MediaNameUtils.Normalize(html)
                     .Split("<a ")
                     .Skip(1)
                     .Reverse())
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

            if (string.IsNullOrWhiteSpace(row) || !row.Contains("class=\"card-item\"")) continue;

            var url = Match("href=\"/([^\"]+)\"");

            if (string.IsNullOrWhiteSpace(url)) continue;

            #region types

            string[] types = null;
            var type = Match("class=\"card-item-type ([^\" ]+)\"");

            switch (type)
            {
                case "films":
                    types = new[]
                    {
                        "movie"
                    };

                    break;

                case "series":
                    types = new[]
                    {
                        "serial"
                    };

                    break;

                case "cartoons":
                    types = new[]
                    {
                        "multfilm",
                        "multserial"
                    };

                    break;

                case "animation":
                    types = new[]
                    {
                        "anime"
                    };

                    break;
            }

            if (types == null) continue;

            #endregion

            if (!string.IsNullOrWhiteSpace(url))
            {
                url = $"{AppInit.conf.Rezka.host}/{url}";

                if (MemoryCache.TryGetValue(url, out _)) continue;

                var fulnews = await _httpService.Get(url, useProxy: AppInit.conf.Rezka.useproxy);

                if (fulnews == null) continue;

                var name = Regex.Match(fulnews, "class=\"si-title\">([^<]+)<")
                    .Groups[1]
                    .Value.Split("/")[0]
                    .Trim();

                var siparam = Regex.Match(fulnews, "class=\"si-param\">(s[0-9]+e[0-9]+)", RegexOptions.IgnoreCase)
                    .Groups[1].Value;

                if (string.IsNullOrWhiteSpace(siparam) && type == "series")
                {
                    siparam = Regex.Match(fulnews, "class=\"si-param\">(s[0-9]+)", RegexOptions.IgnoreCase)
                        .Groups[1]
                        .Value;

                    if (string.IsNullOrWhiteSpace(siparam)) continue;
                }

                var g = Regex.Match(fulnews,
                        "<div class=\"si-data\">[\n\r\t ]+<ul>[\n\r\t ]+<li>([^<]+)</li>[\n\r\t ]+<li>([0-9]{4})")
                    .Groups;

                var originalname = g[1]
                    .Value.Split("/")[0]
                    .Trim();

                if (!int.TryParse(g[2].Value, out var relased) || relased == 0) continue;

                #region Дата создания

                var createTime = MediaNameUtils.ParseDate(Regex
                    .Match(fulnews, "class=\"si-date\">(Добавлено|Опубликовано|Обновлено) ([^<]+)<")
                    .Groups[2]
                    .Value, "dd.MM.yyyy");

                if (createTime == default)
                {
                    if (page != 1) continue;

                    createTime = DateTime.UtcNow;
                }

                #endregion

                #region Обновляем/Получаем Magnet

                string magnet = null;
                string sizeName = null;
                string quality = null;

                byte[] torrent = null;

                foreach (var q in new[]
                         {
                             "1080p",
                             "720p"
                         })
                {
                    var tid = Regex.Match(fulnews, $"href=\"/([^\"]+)\" class=\"dwn-links-item\">{q}</a>")
                        .Groups[1]
                        .Value;

                    if (string.IsNullOrWhiteSpace(tid)) continue;

                    torrent = await _httpService.Download($"{AppInit.conf.Rezka.rqHost()}/{tid}", referer: url,
                        useProxy: AppInit.conf.Rezka.useproxy);

                    magnet = BencodeTo.Magnet(torrent);
                    sizeName = BencodeTo.SizeName(torrent);
                    quality = q;

                    if (!string.IsNullOrWhiteSpace(magnet)) break;
                }

                if (string.IsNullOrWhiteSpace(magnet)) continue;

                #endregion

                var info = fulnews.Contains("<img title=\"Украинский\"")
                    ? ", UKR"
                    : fulnews.Contains("<u>ненормативная лексика</u>")
                        ? ", 18+"
                        : null;

                torrents.Add(new TorrentDetails
                {
                    TrackerName = "hdrezka",
                    Types = types,
                    Url = url,
                    Title =
                        $"{name} / {originalname} {(string.IsNullOrWhiteSpace(siparam) ? "" : $"/ {siparam.ToLower()} ")}[{relased}, {quality}{info}]",
                    Sid = 1,
                    SizeName = sizeName,
                    CreateTime = createTime,
                    Magnet = magnet,
                    Name = name,
                    OriginalName = originalname,
                    Relased = relased
                });

                MemoryCache.Set(url, 0, DateTime.Today.AddDays(1));
            }
        }

        await _torrentRepository.AddOrUpdateAsync(torrents);

        return torrents.Count > 0;
    }

    #endregion

    #region Parse

    private static bool workParse;

    public HDRezkaController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, HttpService httpService) :
        base(memoryCache)
    {
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    public async Task<string> Parse(int maxpage = 1)
    {
        if (workParse) return "work";

        workParse = true;

        try
        {
            for (var page = 1; page <= maxpage; page++)
            {
                if (page > 1) await Task.Delay(AppInit.conf.Rezka.parseDelay);

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