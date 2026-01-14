using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Обогащает торрент: вычисляет размер, качество, сезоны и т.д.
/// </summary>
public interface ITorrentEnricher
{
    Task<TorrentDetails> EnrichAndConvertAsync(TorrentDetails torrent);
}
