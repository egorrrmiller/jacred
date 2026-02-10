namespace JacRed.Core.Models.Database;

public class SearchHistory
{
    public string Query { get; set; } = null!;
    public DateTime LastSearchTime { get; set; }
    public string TrackersHash { get; set; } = null!;
}