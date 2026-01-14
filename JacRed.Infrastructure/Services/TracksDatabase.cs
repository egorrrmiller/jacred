using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using JacRed.Core.Utils;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Services;

public class TracksDatabase : ITracksDatabase
{
    private static readonly Random Random = new();
    private readonly ConcurrentDictionary<string, ffprobemodel> _database = new();
    private readonly HttpService _httpService;
    private readonly ILogger<TracksDatabase> _logger;
    private readonly string[] _tsuriEndpoints = [];

    public TracksDatabase(ILogger<TracksDatabase> logger, HttpService httpService)
    {
        _logger = logger;
        _httpService = httpService;
    }

    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading TracksDB from disk...");

        foreach (var folder1 in Directory.EnumerateDirectories("Data/tracks"))
        foreach (var folder2 in Directory.EnumerateDirectories(folder1))
        foreach (var file in Directory.EnumerateFiles(folder2))
        {
            var infohash = Path.GetFileName(folder1).Substring(0, 2) +
                           Path.GetFileName(folder2) +
                           Path.GetFileNameWithoutExtension(file);

            try
            {
                var json = await File.ReadAllTextAsync(file);
                var model = JsonConvert.DeserializeObject<ffprobemodel>(json);

                if (model?.streams?.Count > 0)
                    _database[infohash] = model;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load track file: {Path}", file);
            }
        }

        _logger.LogInformation("TracksDB loaded: {Count} entries", _database.Count);
    }

    public List<ffStream>? GetStreams(string magnet, string[]? types = null)
    {
        if (IsExcludedType(types))
            return null;

        var infohash = ExtractInfoHash(magnet);
        if (string.IsNullOrEmpty(infohash))
            return null;

        if (_database.TryGetValue(infohash, out var res))
            return res.streams;

        var path = GetFilePath(infohash);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            res = JsonConvert.DeserializeObject<ffprobemodel>(json);

            if (res?.streams?.Count > 0)
            {
                _database[infohash] = res;
                return res.streams;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read track file: {Path}", path);
        }

        return null;
    }

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

        // Удалить торрент
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
            var path = GetFilePath(infohash, true);
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save track data: {InfoHash}", infohash);
        }
    }

    public HashSet<string> GetLanguages(TorrentDetails torrent, List<ffStream> streams)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (torrent.Languages?.Count > 0)
            languages.UnionWith(torrent.Languages);

        if (streams?.Count > 0)
            languages.UnionWith(streams
                .Where(s => s.codec_type == "audio" && !string.IsNullOrEmpty(s.tags?.language))
                .Select(s => s.tags.language));

        return languages.Count > 0 ? languages : null;
    }

    public bool IsExcludedType(string[] types)
    {
        if (types == null || types.Length == 0)
            return true;

        return types.Contains("sport") ||
               types.Contains("tvshow") ||
               types.Contains("docuserial");
    }

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

    private string GetFilePath(string infohash, bool createFolder = false)
    {
        var path = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}/{infohash.Substring(3)}";
        if (createFolder)
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }
}