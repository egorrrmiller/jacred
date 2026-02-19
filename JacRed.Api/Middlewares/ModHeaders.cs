using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Core.Models.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace JacRed.Api.Middlewares;

public partial class ModHeaders
{
    private readonly RequestDelegate _next;

    public ModHeaders(RequestDelegate next)
    {
        _next = next;
    }

    public Task Invoke(HttpContext httpContext, IOptionsSnapshot<Config> configOptions)
    {
        var config = configOptions.Value;

        httpContext.Response.Headers.AccessControlAllowCredentials = "true";
        httpContext.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        httpContext.Response.Headers.AccessControlAllowHeaders = "Accept, Origin, Content-Type";
        httpContext.Response.Headers.AccessControlAllowMethods = "POST, GET, OPTIONS";

        if (httpContext.Request.Headers.TryGetValue("origin", out var origin))
            httpContext.Response.Headers.AccessControlAllowOrigin = origin.ToString();
        else if (httpContext.Request.Headers.TryGetValue("referer", out var referer))
            httpContext.Response.Headers.AccessControlAllowOrigin = referer.ToString();
        else
            httpContext.Response.Headers.AccessControlAllowOrigin = "*";

        /*if (httpContext.Connection.RemoteIpAddress.ToString() == "127.0.0.1") return _next(httpContext);

        if (httpContext.Request.Path.Value.StartsWith("/cron/")
            || httpContext.Request.Path.Value.StartsWith("/jsondb")
            || httpContext.Request.Path.Value.StartsWith("/dev/"))
            return Task.CompletedTask;

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            if (httpContext.Request.Path.Value == "/" ||
                Regex.IsMatch(httpContext.Request.Path.Value, "^/(api/v1\\.0/(conf|subscribe)|stats/|sync/)"))
                return _next(httpContext);

            if (config.ApiKey
                != MyRegex()
                    .Match(httpContext.Request.QueryString.Value)
                    .Groups[2].Value)
                return Task.CompletedTask;
        }*/

        return _next(httpContext);
    }

    [GeneratedRegex("(\\?|&)apikey=([^&]+)")]
    private static partial Regex MyRegex();
}