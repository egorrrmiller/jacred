using JacRett.Api.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace JacRett.Api.Configuration;

public static class Extensions
{
    public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ModHeaders>();
    }
}