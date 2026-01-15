using JacRed.Core.Enums;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

/// <summary>
///     Источник данных для крон-задач трекера: метаданные и загрузка каталога.
/// </summary>
public interface ITrackerCronProvider
{
    TrackerType Tracker { get; }
    string TrackerName { get; }
    string Url { get; }

    /// <summary>
    ///     Загружает каталог трекера для дальнейшей индексации.
    /// </summary>
    Task<IReadOnlyCollection<TorrentDetails>> FetchCatalogAsync(CancellationToken cancellationToken = default);
}
