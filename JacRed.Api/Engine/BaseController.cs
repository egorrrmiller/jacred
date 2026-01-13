using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Api.Engine;

public class BaseController : Controller, IDisposable
{
	public IMemoryCache MemoryCache;

	public BaseController(IMemoryCache memoryCache) => MemoryCache = memoryCache;

	public JsonResult OnError(string msg) => new(new
	{
		success = false,
		msg
	});
}