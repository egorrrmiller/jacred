using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Web;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Services;

public class MediaAnalyzerService : IMediaAnalyzerService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, ffprobemodel> _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MediaAnalyzerService> _logger;
    private readonly string[] _tsuriEndpoints;

    public MediaAnalyzerService(
        IMemoryCache cache,
        ILogger<MediaAnalyzerService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _database = new();
        _tsuriEndpoints = (configuration["tsuri"]?.Split(',') ?? Array.Empty<string>())
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    public async Task LoadExistingDataAsync()
    {
        if (!Directory.Exists("Data/tracks")) return;

        foreach (var file in Directory.EnumerateFiles("Data/tracks", "*", SearchOption.AllDirectories))
        {
            var infohash = ExtractInfoHashFromPath(file);
            if (string.IsNullOrEmpty(infohash)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(file);
                var result = JsonConvert.DeserializeObject<ffprobemodel>(json);
                if (result?.streams?.Count > 0)
                    _database.TryAdd(infohash, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load track data for {infohash}", infohash);
            }
        }
    }

    public async Task<List<ffStream>> GetStreamsAsync(string magnet, string[] types = null, bool onlyCache = false)
    {
        if (!ShouldAnalyze(types) || string.IsNullOrEmpty(magnet))
            return new();

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash))
            return new();

        if (_database.TryGetValue(infohash, out var result))
            return result.streams;

        if (onlyCache)
            return new();

        var filePath = GetFilePath(infohash);
        if (!File.Exists(filePath)) return new();

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            result = JsonConvert.DeserializeObject<ffprobemodel>(json);
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

        return new();
    }

    public async Task AnalyzeAsync(string magnet, string[] types = null)
    {
        if (!ShouldAnalyze(types) || _tsuriEndpoints.Length == 0 || string.IsNullOrEmpty(magnet))
            return;

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash) || _database.ContainsKey(infohash))
            return;

        var tsuri = _tsuriEndpoints[Random.Shared.Next(_tsuriEndpoints.Length)];
        var mediaUrl = $"{tsuri}/stream/file?link={HttpUtility.UrlEncode(magnet)}&index=1&play";

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

        // Cleanup torrent
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            await client.PostAsync($"{tsuri}/torrents",
                new StringContent($"{{\"action\":\"rem\",\"hash\":\"{infohash}\"}}",
                    Encoding.UTF8, "application/json"));
        }
        catch { /* ignore */ }

        _database.TryAdd(infohash, result);

        try
        {
            var filePath = GetFilePath(infohash, createFolder: true);
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save analysis for {infohash}", infohash);
        }
    }

    public async Task<HashSet<string>> ExtractLanguagesAsync(TorrentDetails torrent, List<ffStream> streams = null)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // From torrent metadata
        if (torrent.Languages?.Count > 0)
            languages.UnionWith(torrent.Languages);

        // From streams
        streams ??= await GetStreamsAsync(torrent.Magnet, torrent.Types);
        if (streams?.Count > 0)
        {
            languages.UnionWith(streams
                .Where(s => s.codec_type == "audio" && !string.IsNullOrEmpty(s.tags?.language))
                .Select(s => s.tags.language));
        }

        return languages.Count > 0 ? languages : new();
    }

    public bool ShouldAnalyze(string[] types) =>
        types == null || types.Length == 0 ||
        !types.Contains("sport", StringComparer.OrdinalIgnoreCase) &&
        !types.Contains("tvshow", StringComparer.OrdinalIgnoreCase) &&
        !types.Contains("docuserial", StringComparer.OrdinalIgnoreCase);

    private string ExtractInfoHash(string magnet)
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

    private string ExtractInfoHashFromPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        var folder2 = Path.GetFileName(dir);
        var folder1 = Path.GetFileName(Path.GetDirectoryName(dir));
        var file = Path.GetFileNameWithoutExtension(filePath);
        return folder1 + folder2 + file;
    }

    private string GetFilePath(string infohash, bool createFolder = false)
    {
        var path = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}/{infohash.Substring(3)}";
        if (createFolder)
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }
}