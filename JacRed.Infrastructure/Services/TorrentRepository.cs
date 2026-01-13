using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Utils;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Репозиторий для управления коллекциями торрентов, хранит данные в виде сериализованных файлов.
///     Использует кэширование с ограничением времени жизни и автоматическую инвалидацию.
/// </summary>
public class TorrentRepository : ITorrentRepository
{
    private readonly ICacheService _cache;
    private readonly IContentCatalog _contentCatalog;
    private readonly IKeyGenerator _keyGenerator;
    private readonly IPathResolver _pathResolver;
    private readonly ITorrentEnricher _torrentEnricher;

    public TorrentRepository(
        IContentCatalog contentCatalog,
        ICacheService cache,
        IPathResolver pathResolver,
        IKeyGenerator keyGenerator,
        ITorrentEnricher torrentEnricher)
    {
        _contentCatalog = contentCatalog;
        _cache = cache;
        _pathResolver = pathResolver;
        _keyGenerator = keyGenerator;
        _torrentEnricher = torrentEnricher;
    }

    public async Task AddOrUpdateAsync(IReadOnlyCollection<TorrentBaseDetails> torrents)
    {
        var grouped = torrents.GroupBy(t => _keyGenerator.Build(t.Name, t.OriginalName));

        foreach (var group in grouped)
        {
            var key = group.Key;
            var filePath = _pathResolver.GenerateFilePath(key);
            var currentData = await LoadOrCreateCollectionAsync(key);

            var hasChanges = false;

            foreach (var torrent in group)
                if (await UpdateOrAddTorrent(currentData, torrent))
                    hasChanges = true;

            if (hasChanges)
            {
                await SaveCollectionToFileAsync(filePath, currentData);
                await _cache.InvalidateAsync($"collection:{key}");
            }
        }
    }

    public async Task AddOrUpdateAsync<T>(
        IReadOnlyCollection<T> torrents,
        Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate
    ) where T : TorrentBaseDetails
    {
        var grouped = torrents.GroupBy(t => _keyGenerator.Build(t.Name, t.OriginalName));

        foreach (var group in grouped)
        {
            var key = group.Key;
            var filePath = _pathResolver.GenerateFilePath(key);
            var currentData = await LoadOrCreateCollectionAsync(key);

            var hasChanges = false;

            foreach (var torrent in group)
            {
                if (predicate != null)
                    if (!await predicate(torrent, currentData))
                        continue;

                await _torrentEnricher.EnrichAndConvertAsync(torrent);

                if (await UpdateOrAddTorrent(currentData, torrent))
                    hasChanges = true;
            }

            if (hasChanges)
            {
                await SaveCollectionToFileAsync(filePath, currentData);
                await _cache.InvalidateAsync($"collection:{key}");
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key,
        bool updateCache = false)
    {
        var cacheKey = $"collection:{key}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async () => await LoadOrCreateCollectionAsync(key),
            TimeSpan.FromMinutes(10)
        );
    }

    public async Task SaveMasterIndexAsync()
    {
        if (_contentCatalog is ContentCatalogService catalogService) await catalogService.SaveToFileAsync();
    }

    #region Helpers

    private async Task<Dictionary<string, TorrentDetails>> LoadOrCreateCollectionAsync(string key)
    {
        var filePath = _pathResolver.GenerateFilePath(key);
        if (!File.Exists(filePath)) return new Dictionary<string, TorrentDetails>();

        try
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                return JsonStream.Read<Dictionary<string, TorrentDetails>>(stream);
            }) ?? new Dictionary<string, TorrentDetails>();
        }
        catch
        {
            return new Dictionary<string, TorrentDetails>();
        }
    }

    private async Task<bool> UpdateOrAddTorrent(Dictionary<string, TorrentDetails> data, TorrentBaseDetails torrent)
    {
        return data.TryGetValue(torrent.Url, out var existing)
            ? await UpdateExistingTorrent(existing, torrent)
            : await AddNewTorrent(data, torrent);
    }

    private async Task<bool> UpdateExistingTorrent(TorrentDetails existing, TorrentBaseDetails torrent)
    {
        var changed = false;

        if (torrent.Types != null && !existing.Types?.SequenceEqual(torrent.Types) == true)
        {
            existing.Types = torrent.Types;
            changed = true;
        }

        if (torrent.Title != existing.Title)
        {
            existing.Title = torrent.Title;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(torrent.Magnet) && torrent.Magnet != existing.Magnet)
        {
            existing.Magnet = torrent.Magnet;
            existing.FfprobeTryCount = 0;
            changed = true;
        }

        if (torrent.TrackerName != existing.TrackerName)
        {
            existing.TrackerName = torrent.TrackerName;
            changed = true;
        }

        existing.UpdateTime = DateTime.UtcNow;
        existing.CheckTime = DateTime.Now;

        if (changed) await _torrentEnricher.EnrichAndConvertAsync(existing);

        return changed;
    }

    private async Task<bool> AddNewTorrent(Dictionary<string, TorrentDetails> data, TorrentBaseDetails torrent)
    {
        if (string.IsNullOrWhiteSpace(torrent.Magnet) || torrent.Types == null || torrent.Types.Length == 0)
            return false;

        var newTorrent = new TorrentDetails
        {
            Url = torrent.Url,
            Types = torrent.Types,
            TrackerName = torrent.TrackerName,
            CreateTime = torrent.CreateTime,
            UpdateTime = torrent.UpdateTime,
            Title = torrent.Title,
            Name = torrent.Name,
            OriginalName = torrent.OriginalName,
            Magnet = torrent.Magnet,
            Sid = torrent.Sid,
            Pir = torrent.Pir,
            Relased = torrent.Relased,
            SizeName = torrent.SizeName,
            Ffprobe = torrent.Ffprobe
        };

        await _torrentEnricher.EnrichAndConvertAsync(newTorrent);
        data[torrent.Url] = newTorrent;

        return true;
    }

    private async Task SaveCollectionToFileAsync(string filePath, Dictionary<string, TorrentDetails> data)
    {
        if (data.Count == 0) return;

        var dir = Path.GetDirectoryName(filePath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        try
        {
            await Task.Run(() =>
            {
                using var fileStream = File.Create(filePath);
                JsonStream.Write(fileStream, data);
            });
        }
        catch
        {
            // Логирование при необходимости
        }
    }

    #endregion
}