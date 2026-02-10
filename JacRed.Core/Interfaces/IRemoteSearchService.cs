using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Координатор поисковых запросов по набору поддерживаемых трекеров.
/// </summary>
public interface IRemoteSearchService
{
    /// <summary>
    ///     Возвращает список поддерживаемых трекеров.
    /// </summary>
    IReadOnlyCollection<TrackerType> GetSupportedTrackers();

    /// <summary>
    ///     Выполняет поиск по запросу в заданных трекерах или во всех доступных.
    /// </summary>
    Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query,
        IReadOnlyCollection<TrackerType>? trackers = null);
}