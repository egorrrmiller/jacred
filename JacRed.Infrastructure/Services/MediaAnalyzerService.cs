using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;

namespace JacRed.Infrastructure.Services;

public class MediaAnalyzerService : IMediaAnalyzerService
{
	private readonly IMemoryCache _cache;

	private readonly ConcurrentDictionary<string, ffprobemodel> _database;

	private readonly IHttpClientFactory _httpClientFactory;

	private readonly ILogger<MediaAnalyzerService> _logger;

	private readonly string[] _tsuriEndpoints;

	public MediaAnalyzerService(IMemoryCache cache,
								ILogger<MediaAnalyzerService> logger,
								IHttpClientFactory httpClientFactory,
								IConfiguration configuration)
	{
		_cache = cache;
		_logger = logger;
		_httpClientFactory = httpClientFactory;
		_database = new();

		_tsuriEndpoints = configuration["tsuri"]
							?.Split(',')
						?? Array.Empty<string>();
	}

	public async Task LoadExistingDataAsync()
	{
		// Загрузка существующих данных из папки Data/tracks
		foreach (var folder1 in Directory.GetDirectories("Data/tracks"))
		{
			foreach (var folder2 in Directory.GetDirectories(folder1))
			{
				foreach (var file in Directory.GetFiles(folder2))
				{
					var infohash = folder1.Substring(12) + folder2.Substring(folder1.Length + 1) + Path.GetFileName(file);

					try
					{
						var json = await File.ReadAllTextAsync(file);
						var result = JsonConvert.DeserializeObject<ffprobemodel>(json);

						if (result?.streams != null && result.streams.Count > 0)
						{
							_database.TryAdd(infohash, result);
						}
					}
					catch (Exception ex)
					{
						_logger.LogWarning(ex, "Failed to load track data for {infohash}", infohash);
					}
				}
			}
		}
	}

	public bool ShouldAnalyze(string[] types)
	{
		if (types == null || types.Length == 0)
		{
			return true;
		}

		return !types.Contains("sport") && !types.Contains("tvshow") && !types.Contains("docuserial");
	}

	public async Task<List<ffStream>> GetStreamsAsync(string magnet, string[] types = null, bool onlyCache = false)
	{
		if (!ShouldAnalyze(types))
		{
			return new();
		}

		var infohash = ExtractInfoHash(magnet);

		if (string.IsNullOrEmpty(infohash))
		{
			return new();
		}

		// Проверяем кэш памяти
		if (_database.TryGetValue(infohash, out var result))
		{
			return result.streams;
		}

		// Если только кэш, возвращаем null
		if (onlyCache)
		{
			return new();
		}

		// Проверяем файловое хранилище
		var filePath = GetFilePath(infohash);

		if (!File.Exists(filePath))
		{
			return new();
		}

		try
		{
			var json = await File.ReadAllTextAsync(filePath);
			result = JsonConvert.DeserializeObject<ffprobemodel>(json);

			if (result?.streams == null || result.streams.Count == 0)
			{
				return new();
			}

			_database.AddOrUpdate(infohash, result, (k, v) => result);

			return result.streams;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to read track data for {infohash}", infohash);

			return new();
		}
	}

	public async Task AnalyzeAsync(string magnet, string[] types = null)
	{
		if (!ShouldAnalyze(types) || _tsuriEndpoints.Length == 0)
		{
			return;
		}

		var infohash = ExtractInfoHash(magnet);

		if (string.IsNullOrEmpty(infohash))
		{
			return;
		}

		// Выбираем случайный endpoint для балансировки нагрузки
		var random = new Random();
		var tsuri = _tsuriEndpoints[random.Next(_tsuriEndpoints.Length)];

		ffprobemodel analysisResult = null;

		try
		{
			// Выполняем анализ через ffprobe
			using var httpClient = _httpClientFactory.CreateClient();
			httpClient.Timeout = TimeSpan.FromMinutes(3);

			var mediaUrl = $"{tsuri}/stream/file?link={HttpUtility.UrlEncode(magnet)}&index=1&play";

			var process = new Process
			{
				StartInfo = new()
				{
					FileName = "ffprobe",
					Arguments = $"-v quiet -print_format json -show_format -show_streams \"{mediaUrl}\"",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					StandardOutputEncoding = Encoding.UTF8
				}
			};

			process.Start();
			await process.WaitForExitAsync();
			var output = await process.StandardOutput.ReadToEndAsync();

			analysisResult = JsonConvert.DeserializeObject<ffprobemodel>(output);

			// Очищаем торрент на сервере
			await httpClient.PostAsync($"{tsuri}/torrents",
				new StringContent($"{{\"action\":\"rem\",\"hash\":\"{infohash}\"}}", Encoding.UTF8,
					"application/json"));
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to analyze {magnet}", magnet);
		}

		if (analysisResult?.streams == null || analysisResult.streams.Count == 0)
		{
			return;
		}

		// Сохраняем в память и файл
		_database.AddOrUpdate(infohash, analysisResult, (k, v) => analysisResult);

		try
		{
			var filePath = GetFilePath(infohash, true);
			var json = JsonConvert.SerializeObject(analysisResult, Formatting.Indented);
			await File.WriteAllTextAsync(filePath, json);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save track data for {infohash}", infohash);
		}
	}

	public async Task<HashSet<string>> ExtractLanguagesAsync(TorrentDetails torrent, List<ffStream> streams = null)
	{
		try
		{
			var languages = new HashSet<string>();

			// Добавляем языки из метаданных торрента
			if (torrent.languages != null)
			{
				foreach (var lang in torrent.languages)
				{
					languages.Add(lang);
				}
			}

			// Если потоки не предоставлены, получаем их
			if (streams == null && !string.IsNullOrEmpty(torrent.magnet))
			{
				streams = await GetStreamsAsync(torrent.magnet, torrent.types);
			}

			// Извлекаем языки из аудио потоков
			if (streams != null)
			{
				foreach (var stream in streams)
				{
					if (!string.IsNullOrEmpty(stream.tags?.language) && stream.codec_type == "audio")
					{
						languages.Add(stream.tags.language);
					}
				}
			}

			return languages.Count > 0
				? languages
				: new();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to extract languages for {torrent}", torrent.name);

			return new();
		}
	}

	private string ExtractInfoHash(string magnet)
	{
		try
		{
			return MagnetLink.Parse(magnet)
				.InfoHashes.V1OrV2.ToHex();
		}
		catch
		{
			return null;
		}
	}

	private string GetFilePath(string infohash, bool createFolder = false)
	{
		var folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

		if (createFolder)
		{
			Directory.CreateDirectory(folder);
		}

		return $"{folder}/{infohash.Substring(3)}";
	}
}