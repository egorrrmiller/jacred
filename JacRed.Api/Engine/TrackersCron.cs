using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JacRed.Core;
using JacRed.Core.Interfaces;

namespace JacRed.Api.Engine;

public class TrackersCron
{
	private readonly IContentCatalog _contentCatalog;

	private readonly ITorrentRepository _torrentRepository;

	public TrackersCron(ITorrentRepository torrentRepository, IContentCatalog contentCatalog)
	{
		_torrentRepository = torrentRepository;
		_contentCatalog = contentCatalog;
	}

	public async Task Run()
	{
		await Task.Delay(20_000);

		while (true)
		{
			if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
			{
				await Task.Delay(TimeSpan.FromMinutes(1));

				continue;
			}

			await Task.Delay(TimeSpan.FromHours(1));

			try
			{
				var trackers = new HashSet<string>();

				var db = await _contentCatalog.GetAllKeysAsync();
				foreach (var item in db.ToArray())
				{
					foreach (var t in (await _torrentRepository.GetCollectionAsync(item.Key /*, cache: false*/))
								.Values)
					{
						if (string.IsNullOrEmpty(t.magnet))
						{
							continue;
						}

						try
						{
							if (t.magnet.Contains("&"))
							{
								foreach (Match tr in Regex.Matches(t.magnet, "tr=([^&]+)"))
								{
									var tracker = HttpUtility.UrlDecode(tr.Groups[1]
											.Value.Split("?")[0])
										.Trim()
										.ToLower();

									if (string.IsNullOrWhiteSpace(tracker)
										|| tracker.Contains("[")
										|| !tracker.Replace("://", "")
											.Contains(":")
										|| tracker.Contains(" ")
										|| tracker.Contains("torrentsmd.eu"))
									{
										continue;
									}

									if (Regex.IsMatch(tracker, "[^/]+/[^/]+/announce"))
									{
										continue;
									}

									if (await ckeck(tracker))
									{
										trackers.Add(tracker);
									}
								}
							}
						}
						catch
						{
						}
					}
				}

				File.WriteAllLines("wwwroot/trackers.txt", trackers);
			}
			catch
			{
			}
		}
	}

	private static async Task<bool> ckeck(string tracker)
	{
		if (string.IsNullOrWhiteSpace(tracker) || tracker.Contains("["))
		{
			return false;
		}

		if (tracker.StartsWith("http"))
		{
			try
			{
				using (var handler = new HttpClientHandler())
				{
					handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

					using (var client = new HttpClient(handler))
					{
						client.Timeout = TimeSpan.FromSeconds(7);
						await client.GetAsync(tracker, HttpCompletionOption.ResponseHeadersRead);

						return true;
					}
				}
			}
			catch
			{
			}
		} else if (tracker.StartsWith("udp:"))
		{
			try
			{
				tracker = tracker.Replace("udp://", "");

				var host = tracker.Split(':')[0]
					.Split('/')[0];

				var port = tracker.Contains(":")
					? int.Parse(tracker.Split(':')[1]
						.Split('/')[0])
					: 6969;

				using (var client = new UdpClient(host, port))
				{
					var cts = new CancellationTokenSource();
					cts.CancelAfter(7000);

					var uri = Regex.Match(tracker, "^[^/]/(.*)")
						.Groups[1].Value;

					await client.SendAsync(Encoding.UTF8.GetBytes($"GET /{uri} HTTP/1.1\r\nHost: {host}\r\n\r\n"),
						cts.Token);

					return true;
				}
			}
			catch
			{
			}
		}

		return false;
	}
}