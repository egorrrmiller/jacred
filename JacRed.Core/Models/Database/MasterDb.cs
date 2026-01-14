namespace JacRed.Core.Models.Database;

/// <summary>
/// Справочник masterDb: key="search_name:search_originalname" -> (updateTime, fileTime).
/// </summary>
public class MasterDb
{
    /// <summary>
    /// Ключ вида "search_name:search_originalname".
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Время последнего обновления информации о раздаче в системе.
    /// </summary>
    public DateTime UpdateTime { get; set; }

    /// <summary>
    /// FileTimeUtc (long) из старого формата.
    /// </summary>
    public long FileTime { get; set; }
}