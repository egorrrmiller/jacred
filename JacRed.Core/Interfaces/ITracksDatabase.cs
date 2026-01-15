using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;

namespace JacRed.Core.Interfaces;

public interface ITracksDatabase
{
    List<ffStream>? GetStreams(string magnet, string[]? types = null);
    HashSet<string> GetLanguages(TorrentDetails torrent, List<ffStream> streams);
}