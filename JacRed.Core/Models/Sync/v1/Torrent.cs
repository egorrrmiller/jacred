using JacRed.Core.Models.Details;

namespace JacRed.Core.Models.Sync.v1;

public class Torrent
{
    public string key { get; set; }

    public TorrentDetails value { get; set; }
}