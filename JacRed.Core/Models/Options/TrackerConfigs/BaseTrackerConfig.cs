using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class BaseTrackerConfig
{
    /// <summary>
    ///     Включен ли поиск по трекеру.
    /// </summary>
    [ConfigurationKeyName("enable-search")]
    public bool EnableSearch { get; set; } = true;
    
    /// <summary>
    ///     Точечный рефреш всех торрентов
    /// </summary>
    [ConfigurationKeyName("refresh")]
    public RefreshSettings Refresh { get; set; } = new();
}