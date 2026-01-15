using System.Text.RegularExpressions;
using Dapper;
using JacRed.Core.Interfaces;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Учет и выдача статистики поисковых запросов.
/// </summary>
public class QueryStatsService : IQueryStatsService
{
    private readonly string _connectionString;

    public QueryStatsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    ///     Сохраняет (инкрементирует) статистику для запроса.
    /// </summary>
    public async Task TrackAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var normalized = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        const string sql = """
                           INSERT INTO public.search_stats (normalized_query, raw_query, hits, last_seen)
                           VALUES (@Normalized, @Raw, 1, now())
                           ON CONFLICT (normalized_query)
                           DO UPDATE SET hits = public.search_stats.hits + 1,
                                         raw_query = EXCLUDED.raw_query,
                                         last_seen = now()
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { Normalized = normalized, Raw = query });
    }

    /// <summary>
    ///     Возвращает топ популярных запросов.
    /// </summary>
    public async Task<IReadOnlyCollection<string>> GetTopQueriesAsync(int take,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
            return Array.Empty<string>();

        const string sql = """
                           SELECT raw_query
                           FROM public.search_stats
                           ORDER BY hits DESC, last_seen DESC
                           LIMIT @Take
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var results = await connection.QueryAsync<string>(sql, new { Take = take });
        return results.Where(q => !string.IsNullOrWhiteSpace(q)).ToArray();
    }

    /// <summary>
    ///     Нормализует строку запроса (нижний регистр, убирает лишние символы).
    /// </summary>
    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var normalized = Regex.Replace(query.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ");
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();

        return normalized;
    }
}
