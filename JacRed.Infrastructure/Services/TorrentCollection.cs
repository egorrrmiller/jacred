using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;

namespace JacRed.Infrastructure.Services;

public class TorrentCollection : ITorrentCollection
{
    private readonly Dictionary<string, TorrentDetails> _database;
    private readonly ITorrentEnricher _torrentEnricher;

    public TorrentCollection(ITorrentEnricher torrentEnricher)
    {
        _database = new();
        _torrentEnricher = torrentEnricher;
    }

    public async Task AddOrUpdate(TorrentBaseDetails torrent)
    {
        if (_database.TryGetValue(torrent.Url, out var existing))
        {
            // Частичное обновление
            await UpdateExistingTorrent(existing, torrent);
        }
        else
        {
            // Добавление нового
            await AddNewTorrent(torrent);
        }
    }

    public IReadOnlyDictionary<string, TorrentDetails> GetSnapshot() => new Dictionary<string, TorrentDetails>(_database);

    private async Task UpdateExistingTorrent(TorrentDetails existing, TorrentBaseDetails torrent)
    {
        var needsFullUpdate = false;

        if (torrent.Types != null && !existing.Types.SequenceEqual(torrent.Types))
        {
            existing.Types = torrent.Types;
            needsFullUpdate = true;
        }

        if (torrent.Title != existing.Title)
        {
            existing.Title = torrent.Title;
            needsFullUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(torrent.Magnet) && torrent.Magnet != existing.Magnet)
        {
            existing.Magnet = torrent.Magnet;
            existing.FfprobeTryCount = 0;
        }

        existing.UpdateTime = DateTime.UtcNow;
        existing.CheckTime = DateTime.Now;

        if (needsFullUpdate)
        {
            await _torrentEnricher.EnrichAndConvertAsync(existing);
        }
    }

    private async Task AddNewTorrent(TorrentBaseDetails torrent)
    {
        if (string.IsNullOrWhiteSpace(torrent.Magnet) || torrent.Types == null || torrent.Types.Length == 0)
            return;

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
        _database[torrent.Url] = newTorrent;
    }

    public void Dispose() { }
}