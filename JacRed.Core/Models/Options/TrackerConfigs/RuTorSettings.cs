using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class RuTorSettings : BaseTrackerConfig
{
    /// <summary>
    ///     Точечный рефреш всех торрентов
    /// </summary>
    [ConfigurationKeyName("refresh")]
    public RefreshSettings Refresh { get; set; } = new();
}