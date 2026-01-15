using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Хранилище торрентов: добавление, обновление и выдача коллекций по ключу.
/// </summary>
public interface ITorrentRepository
{
    /// <summary>
    ///     Добавляет новые торренты или обновляет существующие по ключам (name:originalname).
    /// </summary>
    Task AddOrUpdateAsync(IReadOnlyCollection<TorrentDetails> torrents);

    /// <summary>
    ///     Добавляет/обновляет с дополнительной проверкой через предикат, получающий текущие данные.
    /// </summary>
    Task AddOrUpdateAsync<T>(
        IReadOnlyCollection<T> torrents,
        Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate) where T : TorrentDetails;

    /// <summary>
    ///     Возвращает сохранённую коллекцию по ключу, опционально обновляя кеш.
    /// </summary>
    Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key, bool updateCache = false);
}
