using JacRett.Core.Enums;
using JacRett.Core.Models.Details;

namespace JacRett.Core.Interfaces;

/// <summary>
///     Поиск по конкретному трекеру.
/// </summary>
public interface ITrackerSearch
{
    TrackerType Tracker { get; }
    string TrackerName { get; }
    string Host { get; }

    /// <summary>
    ///     Выполняет поиск по строке запроса на выбранном трекере.
    /// </summary>
    Task<IReadOnlyCollection<TorrentDetails>> SearchAsync(
        string query);
}