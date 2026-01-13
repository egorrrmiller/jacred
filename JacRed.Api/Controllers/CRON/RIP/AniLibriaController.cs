using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.AniLibria;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers.CRON.RIP;

[Route("/cron/anilibria/[action]")]
public class AniLibriaController : BaseController
{
    private static bool workParse;
    private readonly HttpService _httpService;

    private readonly ITorrentRepository _torrentRepository;

    public AniLibriaController(IMemoryCache memoryCache, ITorrentRepository torrentRepository, HttpService httpService)
        : base(memoryCache)
    {
        _torrentRepository = torrentRepository;
        _httpService = httpService;
    }

    public async Task<string> Parse(int limit)
    {
        if (workParse) return "work";

        workParse = true;

        try
        {
            for (var after = 0; after <= limit; after++)
            {
                after = after + 40;

                var roots = await _httpService.Get<List<RootObject>>(
                    $"{AppInit.conf.Anilibria.rqHost()}/v2/getUpdates?limit=40&after={after - 40}&include=raw_torrent",
                    useProxy: AppInit.conf.Anilibria.useproxy);

                if (roots == null || roots.Count == 0) continue;

                foreach (var root in roots)
                {
                    var torrents = new List<TorrentBaseDetails>();

                    var createTime =
                        new DateTime(1970, 1, 1, 0, 0,
                            0, 0).AddSeconds(root.last_change > root.updated
                            ? root.last_change
                            : root.updated);

                    foreach (var torrent in root.torrents.list)
                    {
                        if (string.IsNullOrWhiteSpace(root.code)
                            || (480 >= torrent.quality.resolution
                                && string.IsNullOrWhiteSpace(torrent.quality
                                    .encoder)
                                && string.IsNullOrWhiteSpace(torrent.url)))
                            continue;

                        // Данные раздачи
                        var url = $"anilibria.tv:{root.code}:{torrent.quality.resolution}:{torrent.quality.encoder}";

                        var title =
                            $"{root.names.ru} / {root.names.en} {root.season.year} (s{root.season.code}, e{torrent.series.@string}) [{torrent.quality.@string}]";

                        #region Получаем/Обновляем магнет

                        if (string.IsNullOrWhiteSpace(torrent.raw_base64_file)) continue;

                        var _t = Convert.FromBase64String(torrent.raw_base64_file);
                        var magnet = BencodeTo.Magnet(_t);
                        var sizeName = BencodeTo.SizeName(_t);

                        if (string.IsNullOrWhiteSpace(magnet) || string.IsNullOrWhiteSpace(sizeName)) continue;

                        #endregion

                        torrents.Add(new TorrentBaseDetails
                        {
                            TrackerName = "anilibria",
                            Types = new[]
                            {
                                "anime"
                            },
                            Url = url,
                            Title = title,
                            Sid = torrent.seeders,
                            Pir = torrent.leechers,
                            CreateTime = createTime,
                            Magnet = magnet,
                            SizeName = sizeName,
                            Name = MediaNameUtils.Normalize(root.names.ru),
                            OriginalName = MediaNameUtils.Normalize(root.names.en),
                            Relased = root.season.year
                        });
                    }

                    await _torrentRepository.AddOrUpdateAsync(torrents);
                }

                roots = null;
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
}