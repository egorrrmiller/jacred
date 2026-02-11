using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class RefreshSettings
{
    [ConfigurationKeyName("enable")] public bool Enable { get; set; } = false;

    [ConfigurationKeyName("timeout")] public int TimeOut { get; set; }

    [ConfigurationKeyName("older-than-min")]
    public long OlderThanMin { get; set; } = 120;

    [ConfigurationKeyName("limit")] public int Limit { get; set; } = 100;
}