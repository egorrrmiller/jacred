using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Досбавает недостающие данные в раздачи, полученные из каталогов трекеров.
/// </summary>
public interface ITrackerCatalogEnricher
{
    /// <summary>
    ///     Пытается обогатить раздачу, используя уже известные записи каталога.
    /// </summary>
    Task<bool> TryEnrichAsync(
        TorrentDetails torrent,
        IReadOnlyDictionary<string, TorrentDetails> existing);
}