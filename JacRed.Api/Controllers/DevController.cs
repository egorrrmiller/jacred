using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Api.Controllers;

[Route("/dev/[action]")]
public class DevController : Controller
{
	private readonly ITorrentRepository _torrentRepository;
	private readonly IContentCatalog _contentCatalog;

	public DevController(ITorrentRepository torrentRepository, IContentCatalog contentCatalog)
	{
		_torrentRepository = torrentRepository;
		_contentCatalog = contentCatalog;
	}

	public async Task<JsonResult> UpdateSize()
	{
		if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
		{
			return Json(new
			{
				badip = true
			});
		}

		#region getSizeInfo

		long getSizeInfo(string sizeName)
		{
			if (string.IsNullOrWhiteSpace(sizeName))
			{
				return 0;
			}

			try
			{
				var size = 0.1;

				var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase)
					.Groups;

				if (!string.IsNullOrWhiteSpace(gsize[2].Value))
				{
					if (double.TryParse(gsize[1]
								.Value.Replace(",", "."), NumberStyles.Any,
							CultureInfo.InvariantCulture, out size)
						&& size != 0)
					{
						if (gsize[2]
								.Value.ToLower() is "gb" or "гб")
						{
							size *= 1024;
						}

						if (gsize[2]
								.Value.ToLower() is "tb" or "тб")
						{
							size *= 1048576;
						}

						return (long) (size * 1048576);
					}
				}
			}
			catch
			{
			}

			return 0;
		}

		#endregion

		/*var db = await _torrentRepository.GetAllKeysAsync();
		foreach (var item in db.OrderBy(i => i.Value.fileTime)
					.ToArray())
		{
			using (var fdb = await _torrentRepository.GetWritableCollectionAsync(item.Key))
			{
				foreach (var torrent in fdb.Database)
				{
					torrent.Value.size = getSizeInfo(torrent.Value.sizeName);
					torrent.Value.updateTime = DateTime.UtcNow;

					db[item.Key] = new()
					{
						updateTime = torrent.Value.updateTime,
						fileTime = torrent.Value.updateTime.ToFileTimeUtc()
					};
				}

				fdb.savechanges = true;
			}
		}*/

		await _contentCatalog.SaveToFileAsync();

		return Json(new
		{
			ok = true
		});
	}

	public JsonResult ResetCheckTime()
	{
		if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
		{
			return Json(new
			{
				badip = true
			});
		}

		/*foreach (var item in FileDB.masterDb.ToArray())
		{
			using (var fdb = FileDB.OpenWrite(item.Key))
			{
				foreach (var torrent in fdb.Database)
				{
					torrent.Value.checkTime = DateTime.Today.AddDays(-1);
				}

				fdb.savechanges = true;
			}
		}

		FileDB.SaveChangesToFile();
*/
		return Json(new
		{
			ok = true
		});
	}

	public JsonResult UpdateDetails()
	{
		if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
		{
			return Json(new
			{
				badip = true
			});
		}

		/*foreach (var item in FileDB.masterDb.ToArray())
		{
			using (var fdb = FileDB.OpenWrite(item.Key))
			{
				foreach (var torrent in fdb.Database)
				{
					FileDB.updateFullDetails(torrent.Value);
					torrent.Value.languages = null;

					torrent.Value.updateTime = DateTime.UtcNow;

					FileDB.masterDb[item.Key] = new()
					{
						updateTime = torrent.Value.updateTime,
						fileTime = torrent.Value.updateTime.ToFileTimeUtc()
					};
				}

				fdb.savechanges = true;
			}
		}

		FileDB.SaveChangesToFile();*/

		return Json(new
		{
			ok = true
		});
	}

	public JsonResult UpdateSearchName()
	{
		if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
		{
			return Json(new
			{
				badip = true
			});
		}

		/*foreach (var item in FileDB.masterDb.ToArray())
		{
			using (var fdb = FileDB.OpenWrite(item.Key))
			{
				foreach (var torrent in fdb.Database)
				{
					torrent.Value._sn = StringConvert.SearchName(torrent.Value.name);
					torrent.Value._so = StringConvert.SearchName(torrent.Value.originalname);
				}

				fdb.savechanges = true;
			}
		}

		FileDB.SaveChangesToFile();*/

		return Json(new
		{
			ok = true
		});
	}
}