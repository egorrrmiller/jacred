using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Models.Api;

public class Result
{
    public string Tracker { get; set; }

    public string Details { get; set; }

    public string Title { get; set; }

    public double Size { get; set; }

    public DateTime PublishDate { get; set; }

    public HashSet<int> Category { get; set; }

    public string CategoryDesc { get; set; }

    public int Seeders { get; set; }

    public int Peers { get; set; }

    public string MagnetUri { get; set; }

    public List<ffStream> ffprobe { get; set; }

    public HashSet<string> languages { get; set; }

    public TorrentInfo info { get; set; }
}