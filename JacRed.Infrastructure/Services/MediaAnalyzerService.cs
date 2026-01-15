using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Web;
using Dapper;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace JacRed.Infrastructure.Services;

/// <summary>
///     Анализ медиапотоков: загрузка ffprobe из кеша/БД, запуск ffprobe, извлечение языков.
/// </summary>
public class MediaAnalyzerService : IMediaAnalyzerService
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, ffprobemodel> _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaAnalyzerService> _logger;
    private readonly string[] _tsuriEndpoints;

    public MediaAnalyzerService(
        ILogger<MediaAnalyzerService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        string connectionString)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _connectionString = connectionString;
        _database = new ConcurrentDictionary<string, ffprobemodel>();
        _tsuriEndpoints = (configuration["tsuri"]?.Split(',') ?? Array.Empty<string>())
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    /// <summary>
    ///     Загружает сохранённые ffprobe-данные из БД в память.
    /// </summary>
    public async Task LoadExistingDataAsync()
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "SELECT infohash, ffprobe FROM public.tracks";
            var rows = await connection.QueryAsync<(string infohash, JToken ffprobe)>(sql);

            foreach (var row in rows)
            {
                var result = row.ffprobe?.ToObject<ffprobemodel>();
                if (result?.streams?.Count > 0)
                    _database.TryAdd(row.infohash, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load track data from database");
        }
    }

    /// <summary>
    ///     Возвращает потоки из кэша/БД по магнету; при onlyCache=true не выполняет запросы к БД.
    /// </summary>
    public async Task<List<ffStream>> GetStreamsAsync(string? magnet, string[]? types = null, bool onlyCache = false)
    {
        if (!ShouldAnalyze(types) || string.IsNullOrEmpty(magnet))
            return new List<ffStream>();

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash))
            return new List<ffStream>();

        if (_database.TryGetValue(infohash, out var result))
            return result.streams;

        if (onlyCache)
            return new List<ffStream>();

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "SELECT ffprobe FROM public.tracks WHERE infohash = @InfoHash";
            var token = await connection.QueryFirstOrDefaultAsync<JToken>(sql, new { InfoHash = infohash });
            result = token?.ToObject<ffprobemodel>();

            if (result?.streams?.Count > 0)
            {
                _database.TryAdd(infohash, result);
                return result.streams;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read track data for {infohash}", infohash);
        }

        return new List<ffStream>();
    }

    /// <summary>
    ///     Запускает ffprobe для магнета через tsuri-эндпоинт и сохраняет результаты.
    /// </summary>
    public async Task AnalyzeAsync(string magnet, string[]? types = null)
    {
        if (!ShouldAnalyze(types) || _tsuriEndpoints.Length == 0 || string.IsNullOrEmpty(magnet))
            return;

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash) || _database.ContainsKey(infohash))
            return;

        var tsuri = _tsuriEndpoints[Random.Shared.Next(_tsuriEndpoints.Length)];
        var mediaUrl = $"{tsuri}/stream/file?link={HttpUtility.UrlEncode(magnet)}&index=1&play";

        ffprobemodel? result = null;

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
                _logger.LogWarning("Failed to start ffprobe for {infohash}", infohash);
                return;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();

            result = JsonConvert.DeserializeObject<ffprobemodel>(output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analysis failed for {infohash}", infohash);
        }

        if (result?.streams?.Count <= 0)
            return;

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            await client.PostAsync($"{tsuri}/torrents",
                new StringContent($"{{\"action\":\"rem\",\"hash\":\"{infohash}\"}}",
                    Encoding.UTF8, "application/json"));
        }
        catch
        {
            /* ignore */
        }

        _database.TryAdd(infohash, result);

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
            _logger.LogWarning(ex, "Failed to save analysis for {infohash}", infohash);
        }
    }

    /// <summary>
    ///     Извлекает языки из торрента и потоков (если они есть).
    /// </summary>
    public async Task<HashSet<string>> ExtractLanguagesAsync(TorrentDetails torrent, List<ffStream>? streams = null)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (torrent.Languages?.Count > 0)
            languages.UnionWith(torrent.Languages);

        streams ??= await GetStreamsAsync(torrent.Magnet, torrent.Types);
        if (streams?.Count > 0)
            languages.UnionWith(streams
                .Where(s => s.codec_type == "audio" && !string.IsNullOrEmpty(s.tags?.language))
                .Select(s => s.tags.language));

        return languages.Count > 0 ? languages : new HashSet<string>();
    }

    /// <summary>
    ///     Проверяет, нужно ли анализировать медиапотоки для указанных типов.
    /// </summary>
    public bool ShouldAnalyze(string[]? types)
    {
        return types == null || types.Length == 0 ||
               (!types.Contains("sport", StringComparer.OrdinalIgnoreCase) &&
                !types.Contains("tvshow", StringComparer.OrdinalIgnoreCase) &&
                !types.Contains("docuserial", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Достаёт infohash из magnet-ссылки.
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
