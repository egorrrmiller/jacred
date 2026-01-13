using JacRed.Api.Middlewares;
using Microsoft.AspNetCore.Builder;

namespace JacRed.Api.Engine;

public static class Extensions
{
	public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder) => builder.UseMiddleware<ModHeaders>();
}