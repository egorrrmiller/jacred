namespace JacRed.Core.Models.Database;

public class SearchQuery
{
    public long TmdbId { get; set; } // PK
    public string Query { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public long Hits { get; set; }
    public DateTimeOffset? LastRefreshTime { get; set; }
}