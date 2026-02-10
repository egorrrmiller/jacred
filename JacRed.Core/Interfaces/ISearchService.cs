using JacRed.Core.Models.Api;

namespace JacRed.Core.Interfaces;

public interface ISearchService
{
    /// <summary>
    ///     Поиск для API v1.0 (Lampa и др.)
    /// </summary>
    Task<IReadOnlyCollection<V1TorrentResponse>> SearchTorrentsAsync(TorrentSearchRequest request);

    /// <summary>
    ///     Поиск для Jackett API v2.0
    /// </summary>
    Task<RootObject> SearchJackettAsync(TorrentSearchRequest request);
}