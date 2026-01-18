namespace JacRed.Core.Models.Api;

public class TorrentSearchRequest
{
    public string? Search { get; set; }
    public string? AltName { get; set; }
    public bool Exact { get; set; }
    public string? Type { get; set; }
    public string? Sort { get; set; }
    public string? Tracker { get; set; }
    public string? Voice { get; set; }
    public string? VideoType { get; set; }
    public long Relased { get; set; }
    public long Quality { get; set; }
    public long Season { get; set; }
}