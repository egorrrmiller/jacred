using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Details;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Services;

public class TorrentCollection : ITorrentCollection
{
	private readonly Dictionary<string, TorrentDetails> _database;

	private readonly string _key;

	private readonly ILogger _logger;

	private readonly IPathResolver _pathResolver;

	private readonly ITorrentEnricher _torrentEnricher;

	public TorrentCollection(string key,
							IReadOnlyDictionary<string, TorrentDetails> data,
							IPathResolver pathResolver, ITorrentEnricher torrentEnricher)
	{
		_key = key;
		_database = new(data);
		_pathResolver = pathResolver;
		_torrentEnricher = torrentEnricher;
		HasChanges = false;
	}

	public bool HasChanges { get; private set; }

	public async Task AddOrUpdate(TorrentBaseDetails torrent)
	{
		if (_database.TryGetValue(torrent.url, out var existing))

			// Логика обновления существующего торрента
		{
			await UpdateExistingTorrent(existing, torrent);
		} else

			// Логика добавления нового торрента
		{
			await AddNewTorrent(torrent);
		}

		HasChanges = true;
	}

	public async Task SaveAsync()
	{
		if (!HasChanges || _database.Count == 0)
		{
			return;
		}

		try
		{
			var filePath = _pathResolver.GenerateFilePath(_key);
			var directory = Path.GetDirectoryName(filePath);

			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
				true);

			await JsonSerializer.SerializeAsync(stream, _database);

			HasChanges = false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error saving collection to file: {Key}", _key);
		}
	}

	public void Dispose() => SaveAsync()
		.GetAwaiter()
		.GetResult();

	private async Task UpdateExistingTorrent(TorrentDetails existing, TorrentBaseDetails torrent)
	{
		var needsFullUpdate = false;

		if (torrent.types != null && !existing.types.SequenceEqual(torrent.types))
		{
			existing.types = torrent.types;
			needsFullUpdate = true;
		}

		if (torrent.title != existing.title)
		{
			existing.title = torrent.title;
			needsFullUpdate = true;
		}

		if (!string.IsNullOrWhiteSpace(torrent.magnet) && torrent.magnet != existing.magnet)
		{
			existing.magnet = torrent.magnet;
			existing.ffprobe_tryingdata = 0;
		}

		existing.updateTime = DateTime.UtcNow;
		existing.checkTime = DateTime.Now;

		if (needsFullUpdate)

			// Вызов обогащения данных
		{
			await _torrentEnricher.EnrichAndConvertAsync(existing);
		}
	}

	private async Task AddNewTorrent(TorrentBaseDetails torrent)
	{
		if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
			return;

		var newTorrent = new TorrentDetails
		{
			url = torrent.url,
			types = torrent.types,
			trackerName = torrent.trackerName,
			createTime = torrent.createTime,
			updateTime = torrent.updateTime,
			title = torrent.title,
			name = torrent.name,
			originalname = torrent.originalname,
			magnet = torrent.magnet,
			sid = torrent.sid,
			pir = torrent.pir,
			relased = torrent.relased,
			sizeName = torrent.sizeName,
			ffprobe = torrent.ffprobe
		};

		await _torrentEnricher.EnrichAndConvertAsync(newTorrent);
		_database[torrent.url] = newTorrent;
	}
}