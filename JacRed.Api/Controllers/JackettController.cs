using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Core;
using JacRed.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JacRed.Api.Controllers;

public class JackettController : ControllerBase
{
    private readonly IJackettFacadeService _facade;

    public JackettController(IJackettFacadeService facade)
    {
        _facade = facade;
    }

    [Route("/")]
    public ActionResult Index()
    {
        return File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");
    }

    [Route("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "OK" });
    }

    [Route("version")]
    public ActionResult Version()
    {
        return Content("11", "text/plain; charset=utf-8");
    }

    [Route("lastupdatedb")]
    public ActionResult LastUpdateDB()
    {
        var latest = _facade.GetLastUpdateDb();
        return Content(latest.ToString("dd.MM.yyyy HH:mm"), "text/plain; charset=utf-8");
    }

    [Route("api/v1.0/conf")]
    public IActionResult JacRedConf(string apikey)
    {
        return Ok(new
        {
            apikey = string.IsNullOrWhiteSpace(AppInit.conf.apikey) || apikey == AppInit.conf.apikey
        });
    }

    [Route("/api/v2.0/indexers/{status}/results")]
    public async Task<ActionResult> Jackett(
        string apikey,
        string query,
        string title,
        string title_original,
        int year,
        Dictionary<string, string> category,
        int is_serial = -1)
    {
        var root = await _facade.SearchJackettAsync(
            apikey,
            query,
            title,
            title_original,
            year,
            category,
            is_serial,
            HttpContext.Request.Headers.UserAgent,
            HttpContext.Request.QueryString.Value ?? string.Empty);

        return Ok(root);
    }

    [Route("/api/v1.0/torrents")]
    public async Task<IActionResult> Torrents(
        string search,
        string altname,
        bool exact = false,
        string type = null,
        string sort = null,
        string tracker = null,
        string voice = null,
        string videotype = null,
        long relased = 0,
        long quality = 0,
        long season = 0)
    {
        var response = await _facade.SearchTorrentsAsync(
            search,
            altname,
            exact,
            type,
            sort,
            tracker,
            voice,
            videotype,
            relased,
            quality,
            season,
            HttpContext.RequestAborted);

        return Ok(response);
    }
}