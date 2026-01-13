using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentCollection : IDisposable
{
	public bool HasChanges { get; }

	public Task AddOrUpdate(TorrentBaseDetails torrent);

	public Task SaveAsync();
}