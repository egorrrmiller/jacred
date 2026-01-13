using System.Collections.Concurrent;
using JacRed.Core.Models;
using JacRed.Core.Models.Details;

namespace JacRed.Core.Interfaces;

public interface ITorrentRepository
{
	Task AddOrUpdateAsync(IReadOnlyCollection<TorrentBaseDetails> torrents);

	Task AddOrUpdateAsync<T>(IReadOnlyCollection<T> torrents,
							Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate)
		where T : TorrentBaseDetails;

	Task<IReadOnlyDictionary<string, TorrentDetails>> GetCollectionAsync(string key, bool updateCache = false);

	Task<ITorrentCollection> GetWritableCollectionAsync(string key);

	Task SaveMasterIndexAsync();

	Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync();
}