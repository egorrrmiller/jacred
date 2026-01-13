using System.Collections.Concurrent;
using JacRed.Core.Models;

namespace JacRed.Core.Interfaces;

public interface IContentCatalog
{
	public Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync();

	Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false);

	Task SaveToFileAsync();
}