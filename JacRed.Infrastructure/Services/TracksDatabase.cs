using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace JacRed.Infrastructure.Services;

public class TracksDatabase : ITracksDatabase
{
    private static readonly Random Random = new();
    private readonly ConcurrentDictionary<string, ffprobemodel> _database = new();
    private readonly HttpService _httpService;
    private readonly ILogger<TracksDatabase> _logger;
    private readonly string _connectionString;
    private readonly string[] _tsuriEndpoints;

    public TracksDatabase(
        ILogger<TracksDatabase> logger,
        HttpService httpService,
        IConfiguration configuration,
        string connectionString)
    {
        _logger = logger;
        _httpService = httpService;
        _connectionString = connectionString;
        _tsuriEndpoints = (configuration["tsuri"]?.Split(',') ?? Array.Empty<string>())
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    /// <summary>Загружает сохранённые треки из базы данных.</summary>
    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading TracksDB from database...");

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "SELECT infohash, ffprobe FROM public.tracks";
            var rows = await connection.QueryAsync<(string infohash, JToken ffprobe)>(sql);

            foreach (var row in rows)
            {
                var model = row.ffprobe?.ToObject<ffprobemodel>();
                if (model?.streams?.Count > 0)
                    _database[row.infohash] = model;
            }

            _logger.LogInformation("TracksDB loaded: {Count} entries", _database.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load TracksDB from database.");
        }
    }

    /// <summary>Возвращает потоки ffprobe из памяти или БД по магнит-ссылке.</summary>
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

    /// <summary>Запускает ffprobe анализ и сохраняет результат в БД.</summary>
    public async Task AddAsync(string magnet, string[]? types = null)
    {
        if (IsExcludedType(types) || _tsuriEndpoints.Length == 0)
            return;

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash) || _database.ContainsKey(infohash))
            return;

        var tsuri = _tsuriEndpoints[Random.Next(_tsuriEndpoints.Length)];
        var mediaUrl = $"{tsuri}/stream/file?link={Uri.EscapeDataString(magnet)}&index=1&play";

        ffprobemodel result = null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v quiet -print_format json -show_format -show_streams \"{mediaUrl}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            });

            if (process == null)
            {
                _logger.LogWarning("Failed to start ffprobe for infohash: {InfoHash}", infohash);
                return;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            result = JsonConvert.DeserializeObject<ffprobemodel>(output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFprobe analysis failed for infohash: {InfoHash}", infohash);
        }

        if (result?.streams?.Count <= 0)
            return;

        try
        {
            await _httpService.Post($"{tsuri}/torrents",
                $"{{\"action\":\"rem\",\"hash\":\"{infohash}\"}}");
        }
        catch
        {
            /* ignore */
        }

        _database[infohash] = result;

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO public.tracks (infohash, ffprobe, updated_at)
                VALUES (@InfoHash, @Ffprobe, now())
                ON CONFLICT (infohash)
                DO UPDATE SET ffprobe = EXCLUDED.ffprobe, updated_at = now()";

            await connection.ExecuteAsync(sql, new
            {
                InfoHash = infohash,
                Ffprobe = JToken.FromObject(result)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save track data: {InfoHash}", infohash);
        }
    }

    /// <summary>Извлекает языки из метаданных торрента и потоков.</summary>
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

    /// <summary>Определяет типы, для которых анализ не выполняется.</summary>
    public bool IsExcludedType(string[]? types)
    {
        if (types == null || types.Length == 0)
            return false;

        return types.Contains("sport", StringComparer.OrdinalIgnoreCase) ||
               types.Contains("tvshow", StringComparer.OrdinalIgnoreCase) ||
               types.Contains("docuserial", StringComparer.OrdinalIgnoreCase);
    }

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
