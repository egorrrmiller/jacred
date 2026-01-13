using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentCollection : IDisposable
{
	Task AddOrUpdate(TorrentBaseDetails torrent);
	IReadOnlyDictionary<string, TorrentDetails> GetSnapshot();
}