using JacRed.Core.Models.Details;

namespace JacRed.Core.Models.Sync.v2;

public class Value
{
    public DateTime time { get; set; }

    public long fileTime { get; set; }

    public Dictionary<string, TorrentDetails> torrents { get; set; }
}