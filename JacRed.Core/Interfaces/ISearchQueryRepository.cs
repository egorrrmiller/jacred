namespace JacRed.Core.Interfaces;

public interface ISearchQueryRepository
{
    /// <summary>
    ///     Возвращает список популярных поисковых запросов.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetSearchQueriesAsync(int limit);

    /// <summary>
    ///     Возвращает список запросов, которые требуют обновления (last_refresh_time < olderThan или null).
    /// </summary>
    Task<IReadOnlyCollection<string>> GetStaleSearchQueriesAsync(TimeSpan olderThan, int limit);

    /// <summary>
    ///     Сохраняет или обновляет статистику по поисковому запросу.
    /// </summary>
    Task TrackSearchQueryAsync(string query);

    /// <summary>
    ///     Обновляет время последнего фонового обновления для запроса.
    /// </summary>
    Task UpdateLastRefreshTimeAsync(string query);
}