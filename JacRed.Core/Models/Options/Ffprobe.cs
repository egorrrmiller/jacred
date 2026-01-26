using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

public class Ffprobe
{
    [ConfigurationKeyName("enable")]
    public bool Enable { get; set; } = false;
    
    [ConfigurationKeyName("timeout")] 
    public long TimeOut { get; set; }
    
    [ConfigurationKeyName("tsuri")]
    public string? TsUri { get; set; } 
    
    [ConfigurationKeyName("authorization")]
    public Authorization Authorization { get; set; } = new();
}