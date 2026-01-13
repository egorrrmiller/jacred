using System.Collections.Concurrent;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentRepository
{
    /// <summary>
    /// Добавляет или обновляет коллекцию торрентов по ключу (name:originalname)
    /// </summary>
    Task AddOrUpdateAsync(IReadOnlyCollection<TorrentBaseDetails> torrents);

    /// <summary>
    /// Добавляет или обновляет с предикатом для фильтрации
    /// </summary>
    Task AddOrUpdateAsync<T>(
        IReadOnlyCollection<T> torrents,
        Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate) where T : TorrentBaseDetails;

    /// <summary>
    /// Получает коллекцию торрентов по ключу
    /// </summary>
    Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key, bool updateCache = false);

    /// <summary>
    /// Сохраняет master-индекс (например, lastsync.txt)
    /// </summary>
    Task SaveMasterIndexAsync();

    /// <summary>
    /// Получает все ключи (name:originalname → TorrentInfo)
    /// </summary>
    Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync();
}