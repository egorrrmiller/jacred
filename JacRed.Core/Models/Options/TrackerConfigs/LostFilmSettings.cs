using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class LostFilmSettings
{
    [ConfigurationKeyName("enable")]
    public bool Enable { get; set; } = true;
    
    [ConfigurationKeyName("cookie")]
    public string? Cookie { get; set; }
}