using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Объединяет дублирующиеся торренты и возвращает консолидацию по правилам сервиса.
/// </summary>
public interface ITorrentMergerService
{
    /// <summary>
    ///     Схлопывает коллекцию торрентов, устраняя дубли и объединяя данные.
    /// </summary>
    Task<List<TorrentDetails>> MergeAsync(IEnumerable<TorrentDetails> torrents);
}