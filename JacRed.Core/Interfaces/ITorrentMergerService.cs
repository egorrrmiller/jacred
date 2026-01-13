using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentMergerService
{
    Task<List<TorrentDetails>> MergeAsync(IEnumerable<TorrentDetails> torrents);
}