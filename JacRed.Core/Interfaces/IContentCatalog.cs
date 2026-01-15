using System.Collections.Concurrent;
using JacRed.Core.Models;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Обеспечивает быстрый доступ к каталогу контента и предрасчитанным индексам.
/// </summary>
public interface IContentCatalog
{
    /// <summary>
    ///     Возвращает актуальный словарь всех торрентов по ключу (магнет/URL).
    /// </summary>
    ConcurrentDictionary<string, TorrentInfo>? GetAllKeys();

    /// <summary>
    ///     Строит быстрые индексы (например, по первым буквам) с возможностью принудительного обновления.
    /// </summary>
    Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false);
}
