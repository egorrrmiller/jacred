using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Database;
using JacRed.Infrastructure.Migrations.Configurations;
using Npgsql;

namespace JacRed.Infrastructure.Persistence.Repositories;

public class SearchHistoryRepository : ISearchHistoryRepository
{
    private const string Schema = DbSchema.Name;
    private readonly string _connectionString;

    public SearchHistoryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<SearchHistory?> GetAsync(string query)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            SELECT 
                query           AS ""Query"",
                last_search_time AS ""LastSearchTime"",
                trackers_hash   AS ""TrackersHash""
            FROM {Schema}.search_history
            WHERE query = @Query";

        return await connection.QueryFirstOrDefaultAsync<SearchHistory>(sql, new { Query = query });
    }

    public async Task AddOrUpdateAsync(string query, DateTime lastSearchTime, string trackersHash)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            INSERT INTO {Schema}.search_history (query, last_search_time, trackers_hash)
            VALUES (@Query, @LastSearchTime, @TrackersHash)
            ON CONFLICT (query)
            DO UPDATE SET
                last_search_time = @LastSearchTime,
                trackers_hash = @TrackersHash";

        await connection.ExecuteAsync(sql, new
        {
            Query = query,
            LastSearchTime = lastSearchTime,
            TrackersHash = trackersHash
        });
    }
}