using System.Collections.Concurrent;
using JacRed.Core.Models;

namespace JacRed.Core.Interfaces;

public interface IContentCatalog
{
	public bool IsUpdated(string key, DateTime updatedTime);

	public void Update(string key, DateTime updatedTime);

	public Task<ConcurrentDictionary<string, TorrentInfo>> GetAllKeysAsync();

	Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false);

	public DateTime GetLastUpdateTime(string key);

	Task SaveToFileAsync();
}