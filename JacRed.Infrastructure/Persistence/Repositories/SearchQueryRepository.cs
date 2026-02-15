using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Utils;
using JacRed.Infrastructure.Migrations.Configurations;
using Npgsql;

namespace JacRed.Infrastructure.Persistence.Repositories;

public class SearchQueryRepository : ISearchQueryRepository
{
    private const string Schema = DbSchema.Name;
    private readonly string _connectionString;

    public SearchQueryRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyCollection<string>> GetSearchQueriesAsync(int limit)
    {
        if (limit <= 0)
            return [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            SELECT query
            FROM {Schema}.search_queries
            ORDER BY last_seen DESC, hits DESC
            LIMIT @Limit";

        var rows = await connection.QueryAsync<string>(sql, new { Limit = limit });

        return rows
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<string>> GetStaleSearchQueriesAsync(TimeSpan olderThan, int limit)
    {
        if (limit <= 0)
            return [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var cutoff = DateTimeOffset.UtcNow - olderThan;

        var sql = $@"
            SELECT query
            FROM {Schema}.search_queries
            WHERE last_refresh_time IS NULL OR last_refresh_time < @Cutoff
            ORDER BY last_seen DESC, hits DESC
            LIMIT @Limit";

        var rows = await connection.QueryAsync<string>(sql, new { Cutoff = cutoff, Limit = limit });

        return rows
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task TrackSearchQueryAsync(string query)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            INSERT INTO {Schema}.search_queries (query, created_at, last_seen, hits)
            VALUES (@Query, now(), now(), 1)
            ON CONFLICT (query)
            DO UPDATE SET
                last_seen = now(),
                hits = {Schema}.search_queries.hits + 1";

        await connection.ExecuteAsync(sql, new { Query = query });
    }

    public async Task UpdateLastRefreshTimeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = $@"
            UPDATE {Schema}.search_queries
            SET last_refresh_time = now()
            WHERE query = @Query";

        await connection.ExecuteAsync(sql, new { Query = query });
    }
}