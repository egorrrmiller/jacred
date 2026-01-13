namespace JacRed.Core.Models;

public class TorrentQuality
{
	public HashSet<int> qualitys { get; set; } = new();

	public HashSet<string> types { get; set; } = new();

	public HashSet<string> languages { get; set; } = new();

	public DateTime createTime { get; set; }

	public DateTime updateTime { get; set; }
}