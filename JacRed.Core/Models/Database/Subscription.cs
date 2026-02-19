namespace JacRed.Core.Models.Database;

public class Subscription
{
    public Guid Id { get; set; } // PK
    public long TmdbId { get; set; } // FK
    public string Uid { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}