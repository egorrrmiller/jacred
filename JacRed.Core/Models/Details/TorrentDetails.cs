namespace JacRed.Core.Models.Details;

public class TorrentDetails : TorrentBaseDetails, ICloneable
{
	public double size { get; set; }

	public int quality { get; set; }

	public string videotype { get; set; }

	public HashSet<string> voices { get; set; } = new();

	public HashSet<int> seasons { get; set; } = new();

	public object Clone() => MemberwiseClone();
}