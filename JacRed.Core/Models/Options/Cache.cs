using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

public class Cache
{
    /// <summary>
    /// Включение кеша
    /// </summary>
    [ConfigurationKeyName("enable")] 
    public bool Enable { get; set; } = true;
}