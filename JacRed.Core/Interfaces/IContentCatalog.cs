using System.Collections.Concurrent;
using JacRed.Core.Models;

namespace JacRed.Core.Interfaces;

public interface IContentCatalog
{
    public ConcurrentDictionary<string, TorrentInfo> GetAllKeys();

    Task<Dictionary<string, List<string>>> GetFastIndexes(bool forceUpdate = false);

    Task SaveToFileAsync();
}