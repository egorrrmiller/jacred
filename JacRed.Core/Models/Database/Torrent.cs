using NpgsqlTypes;

namespace JacRed.Core.Models.Database;

/// <summary>
///     Модель хранения торрента (TorrentDetails) в БД.
/// </summary>
public class Torrent
{
    public Guid Id { get; set; }

    public string TrackerName { get; set; } = null!;

    public string[] Types { get; set; } = null!;

    public string Url { get; set; } = null!;

    public string Title { get; set; } = null!;

    public int Sid { get; set; }

    public int Pir { get; set; }

    public string? SizeName { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime UpdateTime { get; set; }

    public DateTime CheckTime { get; set; }

    public string? Magnet { get; set; }

    public string? Name { get; set; }

    public string? OriginalName { get; set; }

    public int Relased { get; set; }

    public string[]? Languages { get; set; }

    public string? SourceSeasonNumber { get; set; }

    public string? SourceSeasonOrder { get; set; }

    public double Size { get; set; }

    public int Quality { get; set; }

    public string? VideoType { get; set; }

    public string[]? Voices { get; set; }

    public int[]? Seasons { get; set; }

    public string? Ffprobe { get; set; }

    public int FfprobeAttempts { get; set; }

    public NpgsqlTsVector? SearchTsv { get; set; }

    public string? SearchName { get; set; }

    public string? OriginalSearchName { get; set; }
}