using Microsoft.Extensions.Configuration;

namespace JacRett.Core.Models.Options.TrackerConfigs;

public class KinozalSettings : BaseTrackerConfig
{
    [ConfigurationKeyName("authorization")]
    public Authorization Authorization { get; set; } = new();
}