using System.Threading.Tasks;
using JacRed.Api.Engine;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Controllers;

[Route("/jsondb/[action]")]
public class DbController : BaseController
{
	private static bool _saveDbWork;
	private readonly IContentCatalog _contentCatalog;

	public DbController(IMemoryCache memoryCache, IContentCatalog contentCatalog) : base(memoryCache)
	{
		_contentCatalog = contentCatalog;
	}

	public async Task<string> Save()
	{
		if (_saveDbWork)
		{
			return "work";
		}

		if (!string.IsNullOrWhiteSpace(AppInit.conf.syncapi))
		{
			return "syncapi";
		}

		_saveDbWork = true;

		await _contentCatalog.SaveToFileAsync();

		_saveDbWork = false;

		return "ok";
	}
}