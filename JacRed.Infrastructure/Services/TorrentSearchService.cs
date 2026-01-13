using JacRed.Core;
using JacRed.Core.Interfaces;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Infrastructure.Services;

public class TorrentSearchService : ITorrentSearchService
{
    private readonly IMemoryCache _cache;
    private readonly IContentCatalog _contentCatalog;
    private readonly ITorrentRepository _torrentRepository;

    public TorrentSearchService(
        IContentCatalog contentCatalog,
        ITorrentRepository torrentRepository,
        IMemoryCache cache)
    {
        _contentCatalog = contentCatalog;
        _torrentRepository = torrentRepository;
        _cache = cache;
    }

    public async Task<List<TorrentDetails>> SearchByTitleAsync(
        string title,
        string originalTitle,
        int? year = null,
        int? mediaType = null,
        bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(originalTitle))
            return new List<TorrentDetails>();

        var fastDb = await _contentCatalog.GetFastIndexes();
        var searchName = StringConvert.SearchName(title);
        var searchOriginal = StringConvert.SearchName(originalTitle);

        var keys = new HashSet<string>();

        // Собираем ключи через fastDb
        foreach (var key in new[] { searchName, searchOriginal }.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (fastDb.TryGetValue(key, out var foundKeys))
                keys.UnionWith(foundKeys);

        if (keys.Count == 0) return new List<TorrentDetails>();

        // Ограничиваем, если нужно
        if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
            keys = keys.Take(AppInit.conf.maxreadfile).ToHashSet();

        return await FilterAndCollectTorrents(keys, searchName, searchOriginal, year, mediaType, exact);
    }

    public async Task<List<TorrentDetails>> SearchByQueryAsync(
        string query,
        int? mediaType = null,
        bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<TorrentDetails>();

        var fastDb = await _contentCatalog.GetFastIndexes();
        var searchQuery = StringConvert.SearchName(query);

        var keys = new HashSet<string>();

        if (exact && fastDb.TryGetValue(searchQuery, out var exactKeys))
            keys.UnionWith(exactKeys);
        else
            keys.UnionWith(fastDb
                .Where(kvp => kvp.Key.Contains(searchQuery))
                .SelectMany(kvp => kvp.Value)
                .Take(AppInit.conf.maxreadfile));

        return await FilterAndCollectTorrents(keys, searchQuery, null, null, mediaType, exact);
    }

    public async Task<List<TorrentQuality>> GetQualityInfoAsync(string name, string originalName, string type = null,
        int page = 1, int take = 1000)
    {
        // Реализуется аналогично
        throw new NotImplementedException();
    }

    private async Task<List<TorrentDetails>> FilterAndCollectTorrents(
        HashSet<string> keys,
        string searchName,
        string searchOriginal,
        int? year,
        int? mediaType,
        bool exact)
    {
        var torrents = new Dictionary<string, TorrentDetails>();

        foreach (var key in keys)
        {
            var collection = await _torrentRepository.GetCollectionAsync(key, true);
            foreach (var t in collection.Values)
            {
                if (t.Types == null || t.Title.Contains(" КПК"))
                    continue;

                if (exact)
                {
                    var sn = t.SourceSeasonNumber ?? StringConvert.SearchName(t.Name);
                    var so = t.SourceSeasonOrder ?? StringConvert.SearchName(t.OriginalName);
                    if (sn != searchName && so != searchOriginal)
                        continue;
                }

                if (!MatchesType(t, mediaType))
                    continue;

                if (year.HasValue && !MatchesYear(t, year.Value))
                    continue;

                if (torrents.TryGetValue(t.Url, out var existing))
                {
                    if (t.UpdateTime > existing.UpdateTime)
                        torrents[t.Url] = t;
                }
                else
                {
                    torrents[t.Url] = t;
                }
            }
        }

        return torrents.Values.ToList();
    }

    private bool MatchesType(TorrentDetails t, int? mediaType)
    {
        return mediaType switch
        {
            1 => t.Types.Contains("movie") || t.Types.Contains("multfilm") || t.Types.Contains("documovie"),
            2 => t.Types.Contains("serial") || t.Types.Contains("multserial") || t.Types.Contains("tvshow"),
            3 => t.Types.Contains("tvshow"),
            4 => t.Types.Contains("docuserial") || t.Types.Contains("documovie"),
            5 => t.Types.Contains("anime"),
            _ => true
        };
    }

    private bool MatchesYear(TorrentDetails t, int year)
    {
        return t.Types.Contains("movie") || t.Types.Contains("multfilm") || t.Types.Contains("documovie")
            ? t.Relased == year || t.Relased == year - 1 || t.Relased == year + 1
            : t.Relased >= year - 1;
    }
}