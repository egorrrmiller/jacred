using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class AnimeLayerSettings : BaseTrackerConfig
{
    [ConfigurationKeyName("authorization")]
    public Authorization Authorization { get; set; } = new();
}