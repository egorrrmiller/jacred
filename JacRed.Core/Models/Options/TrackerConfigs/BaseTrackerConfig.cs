using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class BaseTrackerConfig
{
    /// <summary>
    ///     Включен ли поиск по трекеру.
    /// </summary>
    [ConfigurationKeyName("enable-search")]
    public bool EnableSearch { get; set; } = true;
}