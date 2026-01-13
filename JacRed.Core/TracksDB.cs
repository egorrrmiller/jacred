/*using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Web;
using JacRed.Core;
using JacRed.Core.Models.Details;
using JacRed.Core.Models.Tracks;
using MonoTorrent;
using Newtonsoft.Json;
using HttpClient = JacRed.Core.Utils.HttpClient;

namespace JacRed.Api.Engine.Tracks;

public static class TracksDB
{
	private static readonly Random random = new();

	private static readonly ConcurrentDictionary<string, ffprobemodel> Database = new();

	public static void Configuration()
	{
		Console.WriteLine("TracksDB load");

		foreach (var folder1 in Directory.GetDirectories("Data/tracks"))
		{
			foreach (var folder2 in Directory.GetDirectories(folder1))
			{
				foreach (var file in Directory.GetFiles(folder2))
				{
					var infohash = folder1.Substring(12) + folder2.Substring(folder1.Length + 1) + Path.GetFileName(file);

					try
					{
						var res = JsonConvert.DeserializeObject<ffprobemodel>(File.ReadAllText(file));

						if (res?.streams != null && res.streams.Count > 0)
						{
							Database.TryAdd(infohash, res);
						}
					}
					catch
					{
					}
				}
			}
		}
	}

	private static string pathDb(string infohash, bool createfolder = false)
	{
		var folder = $"Data/tracks/{infohash.Substring(0, 2)}/{infohash[2]}";

		if (createfolder)
		{
			Directory.CreateDirectory(folder);
		}

		return $"{folder}/{infohash.Substring(3)}";
	}

	public static bool theBad(string[] types)
	{
		if (types == null || types.Length == 0)
		{
			return true;
		}

		if (types.Contains("sport") || types.Contains("tvshow") || types.Contains("docuserial"))
		{
			return true;
		}

		return false;
	}

	public static List<ffStream> Get(string magnet, string[] types = null, bool onlydb = false)
	{
		if (types != null && theBad(types))
		{
			return null;
		}

		var infohash = MagnetLink.Parse(magnet)
			.InfoHashes.V1OrV2.ToHex();

		if (Database.TryGetValue(infohash, out var res))
		{
			return res.streams;
		}

		var path = pathDb(infohash);

		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			res = JsonConvert.DeserializeObject<ffprobemodel>(File.ReadAllText(path));

			if (res?.streams == null || res.streams.Count == 0)
			{
				return null;
			}
		}
		catch
		{
			return null;
		}

		Database.AddOrUpdate(infohash, res, (k, v) => res);

		return res.streams;
	}

	public static async Task Add(string magnet, string[] types = null)
	{
		if (types != null && theBad(types))
		{
			return;
		}

		if (AppInit.conf.tsuri == null || AppInit.conf.tsuri.Length == 0)
		{
			return;
		}

		var infohash = MagnetLink.Parse(magnet)
			.InfoHashes.V1OrV2.ToHex();

		if (string.IsNullOrEmpty(infohash))
		{
			return;
		}

		ffprobemodel res = null;
		var tsuri = AppInit.conf.tsuri[random.Next(0, AppInit.conf.tsuri.Length)];

		#region ffprobe

		try
		{
			var timeOut = TimeSpan.FromMinutes(3);
			var cancellationTokenSource = new CancellationTokenSource(timeOut);
			var token = cancellationTokenSource.Token;

			var media = $"{tsuri}/stream/file?link={HttpUtility.UrlEncode(magnet)}&index=1&play";

			using (var process = new Process())
			{
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
				process.StartInfo.FileName = "ffprobe";
				process.StartInfo.Arguments = $"-v quiet -print_format json -show_format -show_streams \"{media}\"";
				process.Start();

				await process.WaitForExitAsync(token);

				var outPut = await process.StandardOutput.ReadToEndAsync();
				res = JsonConvert.DeserializeObject<ffprobemodel>(outPut);
			}
		}
		catch
		{
		}

		await HttpClient.Post($"{tsuri}/torrents", "{\"action\":\"rem\",\"hash\":\"" + infohash + "\"}");

		if (res?.streams == null || res.streams.Count == 0)
		{
			return;
		}

		#endregion

		Database.AddOrUpdate(infohash, res, (k, v) => res);

		try
		{
			var path = pathDb(infohash, true);
			await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(res, Formatting.Indented));
		}
		catch
		{
		}
	}

	public static HashSet<string> Languages(TorrentDetails t, List<ffStream> streams)
	{
		try
		{
			var languages = new HashSet<string>();

			if (t.languages != null)
			{
				foreach (var l in t.languages)
				{
					languages.Add(l);
				}
			}

			if (streams != null)
			{
				foreach (var item in streams)
				{
					if (!string.IsNullOrEmpty(item.tags?.language) && item.codec_type == "audio")
					{
						languages.Add(item.tags.language);
					}
				}
			}

			if (languages.Count == 0)
			{
				return null;
			}

			return languages;
		}
		catch
		{
			return null;
		}
	}
}*/