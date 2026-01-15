using JacRed.Core.Models;
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
    Task<RootObject> SearchJackettAsync(
        string apikey,
        string query,
        string title,
        string titleOriginal,
        int year,
        Dictionary<string, string> category,
        int isSerial,
        string? userAgent,
        string queryString);

    /// <summary>
    ///     Ищет торренты по локальному каталогу с фильтрами (точное совпадение, тип, трекер, качество и т.п.).
    /// </summary>
    Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(
        string search,
        string altname,
        bool exact,
        string? type,
        string? sort,
        string? tracker,
        string? voice,
        string? videotype,
        long relased,
        long quality,
        long season,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Возвращает агрегированную информацию о качествах раздач для указанного тайтла.
    /// </summary>
    Task<Dictionary<string, Dictionary<int, TorrentQuality>>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type,
        int page,
        int take);

    /// <summary>
    ///     Возвращает момент последнего обновления внутренней базы Jackett.
    /// </summary>
    DateTime GetLastUpdateDb();
}
