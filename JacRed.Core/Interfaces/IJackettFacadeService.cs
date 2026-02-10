using JacRed.Core.Models.Api;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Фасад для работы с Jackett и агрегированных поисков: запросы, качество, даты обновления.
/// </summary>
public interface IJackettFacadeService
{
    /// <summary>
    ///     Выполняет поиск через Jackett API с учётом категорий, года, названий и ключа доступа.
    /// </summary>
    Task<RootObject> SearchJackettAsync(TorrentSearchRequest request);

    /// <summary>
    ///     Ищет торренты по локальному каталогу с фильтрами (точное совпадение, тип, трекер, качество и т.п.).
    /// </summary>
    Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(TorrentSearchRequest request);
}