using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using JacRed.Api;
using JacRed.Api.Configuration;
using JacRed.Core.Models.Options;
using JacRed.Infrastructure.Migrations.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var cleanTheme = new AnsiConsoleTheme(new Dictionary<ConsoleThemeStyle, string>
{
    // основной текст
    [ConsoleThemeStyle.Text] = "\x1b[37m",

    // второстепенное
    [ConsoleThemeStyle.SecondaryText] = "\x1b[90m",
    [ConsoleThemeStyle.TertiaryText] = "\x1b[90m",

    // данные
    [ConsoleThemeStyle.String] = "\x1b[32m", // мягкий зелёный
    [ConsoleThemeStyle.Number] = "\x1b[35m", // фиолетовый
    [ConsoleThemeStyle.Boolean] = "\x1b[36m", // бирюзовый
    [ConsoleThemeStyle.Scalar] = "\x1b[32m",

    // уровни
    [ConsoleThemeStyle.LevelVerbose] = "\x1b[90m",
    [ConsoleThemeStyle.LevelDebug] = "\x1b[90m",

    [ConsoleThemeStyle.LevelInformation] = "\x1b[36m", // спокойный cyan
    [ConsoleThemeStyle.LevelWarning] = "\x1b[33m", // amber
    [ConsoleThemeStyle.LevelError] = "\x1b[31m", // red
    [ConsoleThemeStyle.LevelFatal] = "\x1b[31;1m", // яркий красный только для fatal

    // прочее
    [ConsoleThemeStyle.Name] = "\x1b[37m",
    [ConsoleThemeStyle.Null] = "\x1b[90m",
    [ConsoleThemeStyle.Invalid] = "\x1b[33m"
});

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        theme: cleanTheme,
        applyThemeToRedirectedOutput: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Host.UseSerilog(Log.Logger, dispose: true);

// Dapper: сопоставление snake_case колонок с PascalCase свойствами
DefaultTypeMap.MatchNamesWithUnderscores = true;

// 1. Добавляем файл в общую конфигурацию приложения
builder.Configuration.AddYamlFile("config.local.yml", false, true);

// 2. Регистрируем IOptions (теперь builder.Configuration содержит данные из YAML)
builder.Services.Configure<Config>(builder.Configuration);

// 3. Настраиваем Kestrel
builder.WebHost.UseKestrel((context, kestrelOptions) =>
{
    var serverOpts = context.Configuration.Get<Config>() ?? new Config();

    var listenIp = serverOpts.ListenIp;
    var port = serverOpts.ListenPort;

    var ip = listenIp.Equals("any", StringComparison.OrdinalIgnoreCase)
        ? IPAddress.Any
        : IPAddress.Parse(listenIp);

    kestrelOptions.Listen(ip, port);
});

// --- Глобальные настройки ---
CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Сервисы ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddControllers();

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
        .Concat(["application/vnd.apple.mpegurl", "image/svg+xml"]);
});

builder.Services.AddRouting(options => options.LowercaseUrls = true);

// --- Регистрация PostgreSQL ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Регистрация строки подключения как именованной строки (если нужно)
builder.Services.AddSingleton(connectionString);

// --- Регистрация зависимостей ---
builder.Services.RegisterServices();
builder.Services.AddJacRedMigrations(connectionString);

var app = builder.Build();

// --- Middleware ---
app.MapOpenApi();
app.MapScalarApiReference();

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

var options = app.Configuration.Get<Config>();
if (options.Web) app.UseStaticFiles();

app.UseModHeaders();

app.UseSerilogRequestLogging(loggingOptions =>
{
    loggingOptions.MessageTemplate =
        "Incoming Request: {RequestMethod} {Url} | Status: {StatusCode} | Time: {Elapsed:0}ms";

    loggingOptions.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var fullUrl = httpContext.Request.GetDisplayUrl();

        diagnosticContext.Set("Url", fullUrl);
    };
});

app.MapControllers();

// --- Миграция БД ---
app.Services.RunJacRedMigrations();

// --- Запуск приложения ---
await app.RunAsync();

// --- Вспомогательные методы ---
namespace JacRed.Api
{
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
}