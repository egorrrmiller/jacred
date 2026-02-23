using System;
using System.Text.Json.Serialization;

namespace JacRed.Core.Models;

public class UserSubscriptionItem
{
    [JsonPropertyName("tmdb_id")]
    public long TmdbId { get; set; }

    [JsonPropertyName("media")]
    public string Media { get; set; } = string.Empty;

    [JsonPropertyName("last_refresh_time")]
    public DateTimeOffset? LastRefreshTime { get; set; }
}
