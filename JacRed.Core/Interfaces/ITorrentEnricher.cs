using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Обогащает данные торрента: подтягивает метаданные и приводит их к единому формату.
/// </summary>
public interface ITorrentEnricher
{
    /// <summary>
    ///     Обогащает и конвертирует раздачу в внутренний формат приложения.
    /// </summary>
    TorrentDetails EnrichAndConvert(TorrentDetails torrent);
}