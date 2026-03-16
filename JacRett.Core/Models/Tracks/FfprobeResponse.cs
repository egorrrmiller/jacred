using System.Text.Json.Serialization;

namespace JacRett.Core.Models.Tracks;

public sealed class FfprobeResponse
{
    [JsonPropertyName("streams")] public List<FfStream>? Streams { get; set; }
}