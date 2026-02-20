using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Core.Interfaces;
using JacRed.Core.Models.Api;
using JacRed.Core.Models.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Controllers;

public class JackettController : ControllerBase
{
    private readonly Config _config;
    private readonly ISearchService _searchService;

    public JackettController(IOptions<Config> config, ISearchService searchService)
    {
        _searchService = searchService;
        _config = config.Value;
    }

    [Route("/")]
    public ActionResult Index()
    {
        return File(System.IO.File.OpenRead("wwwroot/index.html"), "text/html");
    }

    [Route("/health")]
    public IActionResult Health()
    {
        return Ok(new { status = "OK" });
    }

    [Route("api/v1.0/conf")]
    public IActionResult JacRedConf(string apikey)
    {
        if(apikey == _config.ApiKey)
            return Ok(new
            {
                apikey = true
            });

        return Unauthorized();
    }

    [Route("/api/v2.0/indexers/{status}/results")]
    public async Task<ActionResult> Jackett(
        string apikey,
        string query,
        string title,
        string title_original,
        int year,
        Dictionary<string, string> category,
        int is_serial = -1,
        bool force_search = false)
    {
        if (apikey != _config.ApiKey)
            return Unauthorized();
        
        var root = await _searchService.SearchJackettAsync(new TorrentSearchRequest
        {
            Query = query,
            Title = title,
            TitleOriginal = title_original,
            Year = year,
            Categories = category,
            IsSerial = is_serial,
            UserAgent = HttpContext.Request.Headers.UserAgent,
            QueryString = HttpContext.Request.QueryString.Value ?? string.Empty,
            ForceSearch = force_search
        });

        return Ok(root);
    }

    [Route("/api/v1.0/torrents")]
    public async Task<IActionResult> Torrents(
        string apikey,
        string search,
        string altname,
        bool exact = false,
        string type = null,
        string sort = null,
        string tracker = null,
        string voice = null,
        string videotype = null,
        int relased = 0,
        int quality = 0,
        int season = 0)
    {
        if (apikey != _config.ApiKey)
            return Unauthorized();
        
        var response = await _searchService.SearchTorrentsAsync(new TorrentSearchRequest
        {
            Title = search,
            TitleOriginal = altname,
            Year = relased,
            Exact = exact,
            Type = type,
            Sort = sort,
            Tracker = tracker,
            Voice = voice,
            VideoType = videotype,
            Quality = quality,
            Season = season
        });

        return Ok(response);
    }
}