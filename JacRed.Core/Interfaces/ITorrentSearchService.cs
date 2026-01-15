using JacRed.Core.Models;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Сервис высокого уровня для поиска раздач по названию или произвольной строке.
/// </summary>
public interface ITorrentSearchService
{
    /// <summary>
    ///     Поиск по локализованному и оригинальному названию с опциональными фильтрами года/типа.
    /// </summary>
    Task<List<TorrentDetails>> SearchByTitleAsync(
        string title,
        string originalTitle,
        int? year = null,
        int? mediaType = null,
        bool exact = false);

    /// <summary>
    ///     Поиск по произвольной строке (с типом и точностью) среди всех трекеров/категорий.
    /// </summary>
    Task<List<TorrentDetails>> SearchByQueryAsync(
        string query,
        int? mediaType = null,
        bool exact = false);

    /// <summary>
    ///     Возвращает информацию о качестве раздач для указанного тайтла (пагинация/тип опциональны).
    /// </summary>
    Task<Dictionary<string, Dictionary<int, TorrentQuality>>> GetQualityInfoAsync(
        string name,
        string originalName,
        string? type = null,
        int page = 1,
        int take = 1000);
}
