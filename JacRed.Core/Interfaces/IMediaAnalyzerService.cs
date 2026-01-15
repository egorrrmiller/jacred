using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Анализ медиаконтента: проверка необходимости анализа, извлечение потоков и языков.
/// </summary>
public interface IMediaAnalyzerService
{
    /// <summary>
    ///     Определяет, требуется ли анализ медиа по набору типов (категорий).
    /// </summary>
    bool ShouldAnalyze(string[] types);

    /// <summary>
    ///     Возвращает список медиапотоков по магнету, опционально ограничивая типами и/или только кешем.
    /// </summary>
    Task<List<ffStream>> GetStreamsAsync(string magnet, string[]? types = null, bool onlyCache = false);

    /// <summary>
    ///     Извлекает языки дорожек на основе торрента и уже полученных потоков (если есть).
    /// </summary>
    Task<HashSet<string>> ExtractLanguagesAsync(TorrentDetails torrent, List<ffStream>? streams = null);
}
