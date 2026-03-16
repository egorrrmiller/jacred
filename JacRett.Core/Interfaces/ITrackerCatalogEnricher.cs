using JacRett.Core.Models.Details;

namespace JacRett.Core.Interfaces;

/// <summary>
///     Добавляет недостающие данные, которые невозможно вытащить со страницы поиска
/// </summary>
public interface ITrackerCatalogEnricher
{
    /// <summary>
    ///     Пытается обогатить раздачу, используя уже известные записи каталога.
    /// </summary>
    Task<bool> FetchDetailsAsync(TorrentDetails torrent);
}