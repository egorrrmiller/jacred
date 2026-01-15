using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITrackerCatalogEnricher
{
    Task<bool> TryEnrichAsync(
        TorrentDetails torrent,
        IReadOnlyDictionary<string, TorrentDetails> existing,
        CancellationToken cancellationToken = default);
}