namespace JacRed.Core.Models;

public class StaleQuery
{
    public string Query { get; set; } = null!;
    public long TmdbId { get; set; }
}