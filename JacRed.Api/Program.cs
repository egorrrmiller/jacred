using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JacRed.Api.Configuration;
using JacRed.Api.Engine;
using JacRed.Api.Services;
using JacRed.Core;
using JacRed.Core.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// --- Настройка Kestrel ---
builder.WebHost.UseKestrel(options =>
{
    var ip = AppInit.conf.listenip?.ToLower() == "any"
        ? IPAddress.Any
        : IPAddress.Parse(AppInit.conf.listenip ?? "127.0.0.1");

    options.Listen(ip, AppInit.conf.listenport);
});

// --- Глобальные настройки ---
CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Сервисы ---
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .Distinct()
            .ToArray();

        return new BadRequestObjectResult(new
        {
            error = "Validation failed",
            details = errors
        });
    };
});

builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/vnd.apple.mpegurl", "image/svg+xml" });
});

builder.Services.AddRouting(options => options.LowercaseUrls = true);

// --- Регистрация зависимостей ---
builder.Services.RegisterServices();

// --- Регистрация PostgreSQL ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Регистрация строки подключения как именованной строки (если нужно)
builder.Services.AddSingleton(connectionString);

// --- HTTP Client ---
builder.Services.AddHttpClient<HttpService>(client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpService.UserAgent);
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true // only if you need to ignore SSL
    });


var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(new
            {
                error = "Internal server error",
                message = "An unexpected error occurred. Please try again later."
            }.ToJson());
        });
    });

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseResponseCompression();

if (AppInit.conf.web) app.UseStaticFiles();

app.UseModHeaders();
app.MapControllers();

// --- Запуск приложения ---
await app.RunAsync();

// --- Вспомогательные методы ---
internal static class Extensions
{
    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });
    }
}