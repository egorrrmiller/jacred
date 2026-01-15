using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Хранилище потоков медиаданных, привязанных к магнету/раздаче.
/// </summary>
public interface ITracksDatabase
{
    /// <summary>
    ///     Возвращает сохранённые потоки для магнета, при необходимости фильтруя по типам.
    /// </summary>
    List<ffStream>? GetStreams(string magnet, string[]? types = null);

    /// <summary>
    ///     Возвращает набор языков аудиодорожек на основе торрента и списка потоков.
    /// </summary>
    HashSet<string> GetLanguages(TorrentDetails torrent, List<ffStream> streams);
}
