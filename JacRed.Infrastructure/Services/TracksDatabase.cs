using System.Collections.Concurrent;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Хранилище ffprobe-данных по infohash с локальным кэшем.
/// </summary>
public class TracksDatabase : ITracksDatabase
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, ffprobemodel> _database = new();
    private readonly ILogger<TracksDatabase> _logger;

    public TracksDatabase(
        ILogger<TracksDatabase> logger,
        string connectionString)
    {
        _logger = logger;
        _connectionString = connectionString;
    }

    /// <summary>
    ///     Возвращает потоки из кэша или БД по магнету, если тип не исключён.
    /// </summary>
    public List<ffStream>? GetStreams(string magnet, string[]? types = null)
    {
        if (IsExcludedType(types))
            return null;

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash))
            return null;

        if (_database.TryGetValue(infohash, out var res))
            return res.streams;

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();

            const string sql = "SELECT ffprobe FROM public.tracks WHERE infohash = @InfoHash";
            var token = connection.QueryFirstOrDefault<JToken>(sql, new { InfoHash = infohash });
            res = token?.ToObject<ffprobemodel>();

            if (res?.streams?.Count > 0)
            {
                _database[infohash] = res;
                return res.streams;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read track data for infohash: {InfoHash}", infohash);
        }

        return null;
    }

    /// <summary>
    ///     Возвращает набор языков аудиодорожек на основе торрента и списка потоков.
    /// </summary>
    public HashSet<string> GetLanguages(TorrentDetails torrent, List<ffStream> streams)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (torrent.Languages?.Count > 0)
            languages.UnionWith(torrent.Languages);

        if (streams?.Count > 0)
            languages.UnionWith(streams
                .Where(s => s.codec_type == "audio" && !string.IsNullOrEmpty(s.tags?.language))
                .Select(s => s.tags.language));

        return languages;
    }

    /// <summary>
    ///     Проверяет, нужно ли исключить анализ по типу (спорт/ток-шоу/док-сериалы).
    /// </summary>
    public bool IsExcludedType(string[]? types)
    {
        if (types == null || types.Length == 0)
            return false;

        return types.Contains("sport", StringComparer.OrdinalIgnoreCase) ||
               types.Contains("tvshow", StringComparer.OrdinalIgnoreCase) ||
               types.Contains("docuserial", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Достаёт infohash из magent-ссылки.
    /// </summary>
    private string? ExtractInfoHash(string magnet)
    {
        try
        {
            return MagnetLink.Parse(magnet).InfoHashes.V1OrV2.ToHex();
        }
        catch
        {
            return null;
        }
    }
}
