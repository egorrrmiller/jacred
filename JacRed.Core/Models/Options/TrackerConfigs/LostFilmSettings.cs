using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options.TrackerConfigs;

public class LostFilmSettings
{
    [ConfigurationKeyName("cookie")] public string? Cookie { get; set; }
}