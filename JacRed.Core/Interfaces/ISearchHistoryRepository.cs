using JacRed.Core.Models.Database;

namespace JacRed.Core.Interfaces;

public interface ISearchHistoryRepository
{
    Task<SearchHistory?> GetAsync(string query);
    Task AddOrUpdateAsync(string query, DateTime lastSearchTime, string trackersHash);
}