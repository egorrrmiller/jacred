using Microsoft.Extensions.Configuration;

namespace JacRed.Core.Models.Options;

public class Config
{
    [ConfigurationKeyName("listen-ip")]
    public string ListenIp { get; set; } = "any";

    [ConfigurationKeyName("listen-port")]
    public int ListenPort { get; set; } = 9117;

    [ConfigurationKeyName("api-key")]
    public string? ApiKey { get; set; }

    [ConfigurationKeyName("merge-duplicates")] 
    public bool MergeDuplicates { get; set; } = true;
}
