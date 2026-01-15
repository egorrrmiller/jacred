namespace JacRed.Core.Models.Api;

public class V1TorrentResponse
{
    public string tracker { get; set; } = null!;
    public string? url { get; set; }
    public string title { get; set; } = null!;
    public double size { get; set; }
    public string? sizeName { get; set; }
    public DateTime createTime { get; set; }
    public int sid { get; set; }
    public int pir { get; set; }
    public string? magnet { get; set; }
    public string? name { get; set; }
    public string? originalname { get; set; }
    public int relased { get; set; }
    public string? videotype { get; set; }
    public int quality { get; set; }
    public IReadOnlyCollection<string>? voices { get; set; }
    public IReadOnlyCollection<int>? seasons { get; set; }
    public IReadOnlyCollection<string>? types { get; set; }
}