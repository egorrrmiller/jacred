using System.Text.Json.Serialization;

namespace JacRed.Core.Models.Tracks;

public sealed class FfprobeResponse
{
    [JsonPropertyName("streams")] public List<FfStream>? Streams { get; set; }
}