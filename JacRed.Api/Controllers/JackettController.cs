using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace JacRed.Api.Controllers;

public class JackettController : ControllerBase
{
    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IMemoryCache _cache;
    private readonly ITorrentSearchService _searchService;
    private readonly ITorrentMergerService _mergeService;
    private readonly ITracksDatabase _tracksDatabase;

    public JackettController(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        IMemoryCache cache,
        ITorrentSearchService searchService,
        ITorrentMergerService mergeService,
        ITracksDatabase tracksDatabase)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _cache = cache;
        _searchService = searchService;
        _mergeService = mergeService;
        _tracksDatabase = tracksDatabase;
    }

    [Route("/")]
    public ActionResult Index() => File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");

    [Route("health")]
    public IActionResult Health() => Ok(new { status = "OK" });

    [Route("version")]
    public ActionResult Version() => Content("11", "text/plain; charset=utf-8");

    [Route("lastupdatedb")]
    public async Task<ActionResult> LastUpdateDB()
    {
        var db = await _contentCatalog.GetAllKeysAsync();
        var latest = db.Values.MaxBy(i => i.updateTime)?.updateTime ?? new DateTime(2000, 1, 1);
        return Content(latest.ToString("dd.MM.yyyy HH:mm"), "text/plain; charset=utf-8");
    }

    [Route("api/v1.0/conf")]
    public IActionResult JacRedConf(string apikey) => Ok(new
    {
        apikey = string.IsNullOrWhiteSpace(AppInit.conf.apikey) || apikey == AppInit.conf.apikey
    });

    [Route("/api/v2.0/indexers/{status}/results")]
    public async Task<ActionResult> Jackett(
        string apikey,
        string query,
        string title,
        string title_original,
        int year,
        Dictionary<string, string> category,
        int is_serial = -1)
    {
        var isNumRequest = IsNumRequest(query);
        var cacheKey = GenerateCacheKey(query, title, title_original, year, category, is_serial);

        if (_cache.TryGetValue(cacheKey, out List<Result> cached))
            return Ok(new RootObject { Results = cached });

        // Улучшенное определение типа: fallback на -1 (все типы), если не определён
        var contentType = DetermineContentType(is_serial, category) ?? -1;

        // Применяем эвристику для извлечения title/year из query (например, от Tdarr)
        (title, title_original, year) = ApplyNumQueryHeuristic(query, title, title_original, year, isNumRequest);

        // Если title всё ещё пустой — пробуем использовать query как fallback
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original) &&
            !string.IsNullOrWhiteSpace(query))
        {
            // Простая эвристика: первое слово — название, если нет кириллицы
            var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && !parts[0].Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я'))
            {
                title = parts[0];
            }
            else
            {
                title = query; // fallback
            }
        }

        // Выполняем поиск
        var searchTask = string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original)
            ? _searchService.SearchByQueryAsync(query, contentType)
            : _searchService.SearchByTitleAsync(title, title_original, year, contentType, exact: true);

        var torrents = await searchTask;

        var result = await _mergeService.MergeAsync(torrents);

        // Фильтр по `apikey=rus`
        if (apikey == "rus")
        {
            result = result.Where(t => (t.Languages?.Contains("rus") == true) ||
                                       t.Types?.Intersect(new[] { "sport", "tvshow", "docuserial" }).Any() == true)
                .ToList();
        }

        var jResult = await BuildJackettResults(result, isNumRequest);

        // Кэшируем только если включено и не ограничено по времени
        if (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
            _cache.Set(cacheKey, jResult, TimeSpan.FromMinutes(5));

        return Ok(new RootObject { Results = jResult });
    }

    [Route("/api/v1.0/torrents")]
    public async Task<IActionResult> Torrents(
        string search,
        string altname,
        bool exact = false,
        string type = null,
        string sort = null,
        string tracker = null,
        string voice = null,
        string videotype = null,
        long relased = 0,
        long quality = 0,
        long season = 0)
    {
        (search, altname) = await ResolveKpImdb(search, altname);

        var torrents = exact
            ? await _searchService.SearchByTitleAsync(search, altname, mediaType: TypeToId(type), exact: true)
            : await _searchService.SearchByQueryAsync($"{search} {altname}".Trim(), TypeToId(type));

        var query = torrents.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tracker))
            query = query.Where(t => t.TrackerName == tracker);

        if (relased > 0)
            query = query.Where(t => t.Relased == relased);

        if (quality > 0)
            query = query.Where(t => t.Quality == quality);

        if (!string.IsNullOrWhiteSpace(videotype))
            query = query.Where(t => t.VideoType == videotype);

        if (!string.IsNullOrWhiteSpace(voice))
            query = query.Where(t => t.Voices?.Contains(voice) == true);

        if (season > 0)
            query = query.Where(t => t.Seasons?.Contains((int)season) == true);

        query = sort?.ToLower() switch
        {
            "sid" => query.OrderByDescending(t => t.Sid),
            "pir" => query.OrderByDescending(t => t.Pir),
            "size" => query.OrderByDescending(t => t.Size),
            "create" => query.OrderByDescending(t => t.CreateTime),
            _ => query.OrderByDescending(t => t.CreateTime)
        };

        var result = await _mergeService.MergeAsync(query);

        return Ok(result.Take(2000).Select(t => new
        {
            tracker = t.TrackerName,
            url = t.Url?.StartsWith("http") == true ? t.Url : null,
            title = t.Title,
            size = t.Size,
            sizeName = t.SizeName,
            createTime = t.CreateTime,
            sid = t.Sid,
            pir = t.Pir,
            magnet = t.Magnet,
            name = t.Name,
            originalname = t.OriginalName,
            relased = t.Relased,
            videotype = t.VideoType,
            quality = t.Quality,
            voices = t.Voices,
            seasons = t.Seasons,
            types = t.Types
        }));
    }

    [Route("/api/v1.0/qualitys")]
    public async Task<IActionResult> Qualitys(
        string name,
        string originalname,
        string type,
        int page = 1,
        int take = 1000)
    {
        var db = await _contentCatalog.GetAllKeysAsync();
        var results = new Dictionary<string, Dictionary<int, TorrentQuality>>();

        var keys = BuildSearchKeys(name, originalname)
            .Join(db.Keys, s => s, k => k, (_, k) => k);

        if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
            keys = keys.Take(AppInit.conf.maxreadfile);

        foreach (var key in keys)
        {
            var collection = await _torrentRepository.GetCollectionAsync(key, true);
            foreach (var t in collection.Values.Where(t =>
                         t.Types != null && !t.Types.Contains("sport") && t.Relased != 0))
            {
                if (!string.IsNullOrEmpty(type) && !t.Types.Contains(type)) continue;

                var keyName = $"{StringConvert.SearchName(t.Name)}:{StringConvert.SearchName(t.OriginalName)}";
                var langs = _tracksDatabase.GetLanguages(t, t.Ffprobe ?? _tracksDatabase.GetStreams(t.Magnet, t.Types));

                var model = new TorrentQuality
                {
                    types = t.Types.ToHashSet(),
                    createTime = t.CreateTime,
                    updateTime = t.UpdateTime,
                    languages = langs,
                    qualitys = new() { t.Quality }
                };

                if (!results.TryGetValue(keyName, out var yearMap))
                {
                    results[keyName] = yearMap = new();
                }

                if (yearMap.TryGetValue(t.Relased, out var existing))
                {
                    existing.languages.UnionWith(langs);
                    existing.types.UnionWith(t.Types);
                    existing.qualitys.Add(t.Quality);
                    existing.createTime = existing.createTime < t.CreateTime ? existing.createTime : t.CreateTime;
                    existing.updateTime = existing.updateTime > t.UpdateTime ? existing.updateTime : t.UpdateTime;
                }
                else
                {
                    yearMap[t.Relased] = model;
                }
            }
        }

        var response = take == -1
            ? results
            : results.Skip((page - 1) * take).Take(take).ToDictionary(k => k.Key, v => v.Value);
        return Ok(response);
    }

    #region Helpers

    private async Task<(string, string)> ResolveKpImdb(string search, string altname)
    {
        if (string.IsNullOrWhiteSpace(search) || !Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            return (search, altname);

        var cacheKey = $"api/v1.0/torrents:{search}";
        if (!_cache.TryGetValue(cacheKey, out (string original, string name) cache))
        {
            var uri = search.StartsWith("kp")
                ? $"&kp={search[2..]}"
                : $"&imdb={search}";

            var response =
                await HttpClient.Get<JObject>($"https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1{uri}",
                    timeoutSeconds: 8);
            var data = response?.Value<JObject>("data");
            cache = (data?.Value<string>("original_name"), data?.Value<string>("name"));
            _cache.Set(cacheKey, cache, TimeSpan.FromDays(1));
        }

        return !string.IsNullOrWhiteSpace(cache.original) && !string.IsNullOrWhiteSpace(cache.name)
            ? (cache.original, cache.name)
            : (cache.original ?? cache.name, altname);
    }

    private async Task<List<Result>> BuildJackettResults(IEnumerable<TorrentDetails> torrents, bool isNumRequest)
    {
        var results = new List<Result>();

        foreach (var t in torrents)
        {
            var languages = new HashSet<string>();
            var ffprobe = isNumRequest ? null : await GetFfprobe(t, languages);

            var categoryIds = GetCategoryIds(t, out var categoryDesc);

            results.Add(new Result
            {
                Tracker = t.TrackerName,
                Details = t.Url?.StartsWith("http") == true ? t.Url : null,
                Title = t.Title,
                Size = t.Size,
                PublishDate = t.CreateTime,
                Category = categoryIds,
                CategoryDesc = categoryDesc,
                Seeders = t.Sid,
                Peers = t.Pir,
                MagnetUri = t.Magnet,
                ffprobe = ffprobe,
                languages = languages,
                info = isNumRequest
                    ? null
                    : new Core.Models.Api.TorrentInfo
                    {
                        name = t.Name,
                        originalname = t.OriginalName,
                        sizeName = t.SizeName,
                        relased = t.Relased,
                        videotype = t.VideoType,
                        quality = t.Quality,
                        voices = t.Voices,
                        seasons = t.Seasons?.Count > 0 ? t.Seasons : null,
                        types = t.Types
                    }
            });
        }

        return results;
    }

    private HashSet<int> GetCategoryIds(TorrentDetails t, out string desc)
    {
        desc = null;

        if (t.Types == null || t.Types.Length == 0)
            return new HashSet<int>();

        switch (t.Types[0])
        {
            case "movie":
            case "multfilm":
            case "documovie":
                desc = "Movies";
                return new() { 2000 };

            case "serial":
            case "multserial":
                desc = "TV";
                return new() { 5000 };

            case "docuserial":
                desc = "TV/Documentary";
                return new() { 5080 };

            case "tvshow":
                desc = "TV/Foreign";
                return new() { 5020, 2010 };

            case "anime":
                desc = "TV/Anime";
                return new() { 5070 };

            default:
                return new();
        }
    }

    private async Task<List<ffStream>> GetFfprobe(TorrentDetails t, HashSet<string> languages)
    {
        // Проверяем сначала AppInit.conf.tracks, потом t.ffprobe?.Count
        if (!AppInit.conf.tracks || t.Ffprobe?.Count > 0)
        {
            var langs = _tracksDatabase.GetLanguages(t, t.Ffprobe);
            if (langs?.Any() == true)
                languages.UnionWith(langs);
            return t.Ffprobe;
            
        }
        if (t.Types?.Length == 0)
            t.Types = [];
        
        var streams = _tracksDatabase.GetStreams(t.Magnet, t.Types);
        var streamLangs = _tracksDatabase.GetLanguages(t, streams);
        if (streamLangs?.Any() == true)
            languages.UnionWith(streamLangs);

        return streams;
    }

    private IEnumerable<string> BuildSearchKeys(string name, string original) =>
        new[] { name, original }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(StringConvert.SearchName);

    private string GenerateCacheKey(string query, string title, string orig, int year, Dictionary<string, string> cat,
        int serial) =>
        $"jackett:{query}:{title}:{orig}:{year}:{(cat?.Count > 0 ? string.Join(",", cat) : "none")}:{serial}";

    private bool IsNumRequest(string query) =>
        query != null &&
        HttpContext.Request.Headers.UserAgent ==
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36" &&
        !HttpContext.Request.QueryString.Value.Contains("&is_serial=");

    private (string, string, int) ApplyNumQueryHeuristic(string query, string title, string orig, int year, bool isNum)
    {
        if (!isNum || query == null) return (title, orig, year);

        var m = Regex.Match(query, @"^([^a-z-A-Z]+) ([^а-я-А-Я]+)(?: ([0-9]{4}))?$");
        if (!m.Success) return (title, orig, year);

        var g = m.Groups.Values.Skip(1).ToArray(); // ← m.Groups.Values — может быть пусто?
        if (g.Length < 2) return (title, orig, year); // защита

        if (Regex.IsMatch(g[1].Value, "[a-zA-Z0-9]{2}"))
        {
            return (g[0].Value, g[1].Value, g.Length > 2 ? int.Parse(g[2].Value) : year);
        }

        return (title, orig, year);
    }

    private int? DetermineContentType(int isSerial, Dictionary<string, string> category)
    {
        // Приоритет: category
        if (category?.Count > 0)
        {
            var cat = category.First().Value;
            if (cat.Contains("5020") || cat.Contains("2010")) return 3; // tvshow
            if (cat.Contains("5080")) return 4; // docuserial
            if (cat.Contains("5070")) return 5; // anime
            if (cat.StartsWith("20")) return 1; // movie
            if (cat.StartsWith("50")) return 2; // serial
        }

        // Потом: is_serial
        return isSerial switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            _ => null // ← оставьте null, но логируйте
        };
    }

    private int? TypeToId(string type) => type switch
    {
        "movie" => 1,
        "serial" => 2,
        "tvshow" => 3,
        "docuserial" => 4,
        "anime" => 5,
        _ => null
    };

    #endregion
}