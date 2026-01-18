using JacRed.Api.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace JacRed.Api.Configuration;

public static class Extensions
{
    public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ModHeaders>();
    }
}