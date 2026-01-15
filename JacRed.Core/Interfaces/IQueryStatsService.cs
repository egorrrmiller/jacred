namespace JacRed.Core.Interfaces;

/// <summary>
///     Сервис статистики запросов: учёт и получение топа поисковых фраз.
/// </summary>
public interface IQueryStatsService
{
    /// <summary>
    ///     Регистрирует поисковый запрос в статистике.
    /// </summary>
    Task TrackAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает самые популярные запросы в рамках заданного лимита.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetTopQueriesAsync(int take, CancellationToken cancellationToken = default);
}
